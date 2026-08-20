using System.Collections.Concurrent;
using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Infrastructure.VectorStore;

/// <summary>
/// In-memory implementation of vector store for development/testing.
/// Uses brute-force cosine similarity search.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, ReadOnlyMemory<float>> _embeddings = new();

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // No initialization needed for in-memory store
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertAsync(
        string chunkId,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        _embeddings[chunkId] = embedding;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertBatchAsync(
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>> embeddings,
        CancellationToken cancellationToken = default)
    {
        foreach (var (chunkId, embedding) in embeddings)
        {
            _embeddings[chunkId] = embedding;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        _embeddings.TryRemove(chunkId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        // Chunk IDs follow pattern: {documentId}_chunk_{index}
        var keysToRemove = _embeddings.Keys
            .Where(k => k.StartsWith($"{documentId}_chunk_"))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _embeddings.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((long)_embeddings.Count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var existing = chunkIds.Where(_embeddings.ContainsKey).ToList();
        return Task.FromResult<IReadOnlyList<string>>(existing);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var results = new List<VectorSearchResult>();

        foreach (var (chunkId, embedding) in _embeddings)
        {
            var score = CosineSimilarity(queryEmbedding.Span, embedding.Span);
            results.Add(new VectorSearchResult
            {
                ChunkId = chunkId,
                Score = score
            });
        }

        var topResults = results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(topResults);
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// </summary>
    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            return 0;

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var magnitude = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);

        return magnitude == 0 ? 0 : dotProduct / magnitude;
    }
}
