using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Infrastructure.VectorStore.Hnsw;

/// <summary>
/// HNSW-based vector store implementation using approximate nearest neighbor search.
/// Provides O(log n) search performance instead of O(n) brute-force.
/// </summary>
/// <remarks>
/// This implementation:
/// - Uses an in-memory HNSW graph for fast approximate searches
/// - Persists the graph to a file for durability
/// - Automatically saves after modifications (with debouncing)
/// - Loads existing index on startup if available
/// </remarks>
public sealed class HnswVectorStore : IVectorStore, IDisposable
{
    private readonly HnswGraph _graph;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Timer _saveTimer;
    private bool _isDirty;
    private bool _disposed;

    /// <summary>
    /// Configuration options for the HNSW vector store.
    /// </summary>
    public sealed class Options
    {
        /// <summary>
        /// Path to the index file for persistence.
        /// </summary>
        public string IndexPath { get; init; } = "hnsw_index.bin";

        /// <summary>
        /// Maximum number of connections per node (M parameter).
        /// Higher values improve recall but increase memory and build time.
        /// Recommended: 12-48. Default: 16.
        /// </summary>
        public int MaxConnections { get; init; } = 16;

        /// <summary>
        /// Size of dynamic candidate list during construction (efConstruction).
        /// Higher values improve index quality but slow down construction.
        /// Recommended: 100-500. Default: 200.
        /// </summary>
        public int EfConstruction { get; init; } = 200;

        /// <summary>
        /// Size of dynamic candidate list during search (efSearch).
        /// Higher values improve recall but slow down search.
        /// Recommended: 50-500. Default: 50.
        /// </summary>
        public int EfSearch { get; init; } = 50;

        /// <summary>
        /// Delay before auto-saving after modifications (in milliseconds).
        /// Set to 0 to disable auto-save. Default: 5000 (5 seconds).
        /// </summary>
        public int AutoSaveDelayMs { get; init; } = 5000;
    }

    private readonly Options _options;

    /// <summary>
    /// Creates a new HNSW vector store.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    public HnswVectorStore(Options options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _indexPath = options.IndexPath;
        _graph = new HnswGraph(options.MaxConnections, options.EfConstruction);

        // Load existing index if available
        if (File.Exists(_indexPath))
        {
            try
            {
                var data = File.ReadAllBytes(_indexPath);
                _graph.Deserialize(data);
            }
            catch (Exception ex)
            {
                // Log warning and start fresh
                Console.Error.WriteLine($"Warning: Failed to load HNSW index from {_indexPath}: {ex.Message}. Starting with empty index.");
            }
        }

        // Setup auto-save timer
        if (options.AutoSaveDelayMs > 0)
        {
            _saveTimer = new Timer(
                _ => SaveIfDirtyAsync().GetAwaiter().GetResult(),
                null,
                Timeout.Infinite,
                Timeout.Infinite);
        }
        else
        {
            _saveTimer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Creates a new HNSW vector store with default options.
    /// </summary>
    /// <param name="indexPath">Path to the index file.</param>
    public HnswVectorStore(string indexPath)
        : this(new Options { IndexPath = indexPath })
    {
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // HNSW index is loaded in constructor, no additional initialization needed
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertAsync(
        string chunkId,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _graph.Add(chunkId, embedding);
        MarkDirtyAndScheduleSave();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertBatchAsync(
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>> embeddings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var (chunkId, embedding) in embeddings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _graph.Add(chunkId, embedding);
        }

        MarkDirtyAndScheduleSave();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _graph.Remove(chunkId);
        MarkDirtyAndScheduleSave();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Chunk IDs follow pattern: {documentId}_chunk_{index}
        var prefix = $"{documentId}_chunk_";
        var idsToRemove = _graph.GetAllIds()
            .Where(id => id.StartsWith(prefix))
            .ToList();

        foreach (var id in idsToRemove)
        {
            _graph.Remove(id);
        }

        if (idsToRemove.Count > 0)
        {
            MarkDirtyAndScheduleSave();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.FromResult((long)_graph.Count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var all = _graph.GetAllIds().ToHashSet(StringComparer.Ordinal);
        var existing = chunkIds.Where(all.Contains).ToList();
        return Task.FromResult<IReadOnlyList<string>>(existing);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_graph.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
        }

        var results = _graph.Search(queryEmbedding, limit, _options.EfSearch);

        // Convert distance to similarity score (1 - distance for cosine distance)
        var searchResults = results
            .Select(r => new VectorSearchResult
            {
                ChunkId = r.Id,
                Score = 1.0 - r.Distance // Convert distance back to similarity
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(searchResults);
    }

    /// <summary>
    /// Forces an immediate save of the index to disk.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            var data = _graph.Serialize();
            
            // Write to temp file first, then rename for atomicity
            var tempPath = _indexPath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, data, cancellationToken);
            
            // Atomic rename
            File.Move(tempPath, _indexPath, overwrite: true);
            
            _isDirty = false;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Number of vectors in the index.
    /// </summary>
    public int Count => _graph.Count;

    private void MarkDirtyAndScheduleSave()
    {
        _isDirty = true;
        if (_options.AutoSaveDelayMs > 0)
        {
            _saveTimer.Change(_options.AutoSaveDelayMs, Timeout.Infinite);
        }
    }

    private async Task SaveIfDirtyAsync()
    {
        if (_isDirty && !_disposed)
        {
            try
            {
                await SaveAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to auto-save HNSW index: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _saveTimer.Dispose();

        // Final save on dispose (before marking as disposed)
        if (_isDirty)
        {
            try
            {
                SaveAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to save HNSW index on dispose: {ex.Message}");
            }
        }

        _disposed = true;
        _saveLock.Dispose();
    }
}
