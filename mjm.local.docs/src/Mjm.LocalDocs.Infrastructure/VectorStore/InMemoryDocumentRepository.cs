using System.Collections.Concurrent;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Models.Dashboard;

namespace Mjm.LocalDocs.Infrastructure.VectorStore;

/// <summary>
/// In-memory implementation of document repository for development/testing.
/// </summary>
public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<string, Document> _documents = new();
    private readonly ConcurrentDictionary<string, DocumentChunk> _chunks = new();

    #region Document Operations

    /// <inheritdoc />
    public Task<Document> AddDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        _documents[document.Id] = document;
        return Task.FromResult(document);
    }

    /// <inheritdoc />
    public Task<Document?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        _documents.TryGetValue(documentId, out var document);
        return Task.FromResult(document);
    }

    /// <inheritdoc />
    /// <remarks>
    /// For in-memory storage, this returns the FileContent property.
    /// If the document uses external storage, FileContent will be null.
    /// </remarks>
    public Task<byte[]?> GetDocumentFileAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (_documents.TryGetValue(documentId, out var document))
        {
            return Task.FromResult(document.FileContent);
        }
        return Task.FromResult<byte[]?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Document>> GetDocumentsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var documents = _documents.Values
            .Where(d => d.ProjectId == projectId)
            .ToList();
        return Task.FromResult<IReadOnlyList<Document>>(documents);
    }

    /// <inheritdoc />
    public Task<bool> DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        // Delete chunks first
        var chunkKeysToRemove = _chunks
            .Where(kvp => kvp.Value.DocumentId == documentId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in chunkKeysToRemove)
        {
            _chunks.TryRemove(key, out _);
        }

        return Task.FromResult(_documents.TryRemove(documentId, out _));
    }

    /// <inheritdoc />
    public Task<bool> DocumentExistsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.ContainsKey(documentId));
    }

    /// <inheritdoc />
    public Task<Document?> GetDocumentByHashAsync(
        string projectId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var document = _documents.Values
            .FirstOrDefault(d => d.ProjectId == projectId && d.ContentHash == contentHash);
        return Task.FromResult(document);
    }

    /// <inheritdoc />
    public Task SupersedeDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (_documents.TryGetValue(documentId, out var existing))
        {
            // Create a new Document instance with IsSuperseded = true (init-only)
            var superseded = new Document
            {
                Id = existing.Id,
                ProjectId = existing.ProjectId,
                FileName = existing.FileName,
                FileExtension = existing.FileExtension,
                FileContent = existing.FileContent,
                FileStorageLocation = existing.FileStorageLocation,
                FileSizeBytes = existing.FileSizeBytes,
                ExtractedText = existing.ExtractedText,
                ContentHash = existing.ContentHash,
                Metadata = existing.Metadata,
                VersionNumber = existing.VersionNumber,
                ParentDocumentId = existing.ParentDocumentId,
                IsSuperseded = true,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _documents[documentId] = superseded;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Document>> GetDocumentVersionsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(documentId, out var startDoc))
            return Task.FromResult<IReadOnlyList<Document>>([]);

        // Walk up to find root
        var rootId = startDoc.Id;
        var currentParentId = startDoc.ParentDocumentId;
        var visited = new HashSet<string> { rootId };

        while (!string.IsNullOrEmpty(currentParentId) && visited.Add(currentParentId))
        {
            if (!_documents.TryGetValue(currentParentId, out var parent))
                break;

            rootId = parent.Id;
            currentParentId = parent.ParentDocumentId;
        }

        // Walk down from root collecting the chain
        var chain = new List<Document>();
        var currentId = rootId;
        var visitedDown = new HashSet<string>();

        while (!string.IsNullOrEmpty(currentId) && visitedDown.Add(currentId))
        {
            if (!_documents.TryGetValue(currentId, out var doc))
                break;

            chain.Add(doc);

            // Find child that has this document as parent
            var child = _documents.Values
                .FirstOrDefault(d => d.ParentDocumentId == currentId);

            currentId = child?.Id;
        }

        var result = chain
            .OrderByDescending(d => d.VersionNumber)
            .ToList();

        return Task.FromResult<IReadOnlyList<Document>>(result);
    }

    #endregion

    #region Chunk Operations

    /// <inheritdoc />
    public Task AddChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            _chunks[chunk.Id] = chunk;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DocumentChunk?> GetChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(chunkId, out var chunk);
        return Task.FromResult(chunk);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentChunk>> GetChunksByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var chunks = _chunks.Values
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToList();
        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var idSet = chunkIds.ToHashSet();
        var chunks = _chunks.Values
            .Where(c => idSet.Contains(c.Id))
            .ToList();
        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }

    /// <inheritdoc />
    public Task DeleteChunksByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var keysToRemove = _chunks
            .Where(kvp => kvp.Value.DocumentId == documentId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _chunks.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Project Operations

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetProjectsWithDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        var projectIds = _documents.Values
            .Select(d => d.ProjectId)
            .Distinct()
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(projectIds);
    }

    /// <inheritdoc />
    public Task DeleteDocumentsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var documentIds = _documents.Values
            .Where(d => d.ProjectId == projectId)
            .Select(d => d.Id)
            .ToList();

        foreach (var documentId in documentIds)
        {
            // Delete chunks
            var chunkKeysToRemove = _chunks
                .Where(kvp => kvp.Value.DocumentId == documentId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in chunkKeysToRemove)
            {
                _chunks.TryRemove(key, out _);
            }

            // Delete document
            _documents.TryRemove(documentId, out _);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Dashboard Aggregates

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentContribution>> GetContributionsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var contributions = _documents.Values
            .Where(d => d.CreatedAt >= since)
            .Select(d => new DocumentContribution(d.CreatedAt, d.ParentDocumentId == null))
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentContribution>>(contributions);
    }

    /// <inheritdoc />
    public Task<int> CountActiveDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.Values.Count(d => !d.IsSuperseded));
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLastContributionAtAsync(CancellationToken cancellationToken = default)
    {
        if (_documents.IsEmpty)
            return Task.FromResult<DateTimeOffset?>(null);

        return Task.FromResult<DateTimeOffset?>(_documents.Values.Max(d => d.CreatedAt));
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, int>> GetActiveDocumentCountsByProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = _documents.Values
            .Where(d => !d.IsSuperseded)
            .GroupBy(d => d.ProjectId)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult<IReadOnlyDictionary<string, int>>(counts);
    }

    /// <inheritdoc />
    public Task<long> CountChunksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((long)_chunks.Count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentChunkTally>> GetActiveDocumentChunkTalliesAsync(
        CancellationToken cancellationToken = default)
    {
        var chunkCounts = _chunks.Values
            .GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var tallies = _documents.Values
            .Where(d => !d.IsSuperseded)
            .Select(d => new DocumentChunkTally(
                d.Id,
                d.ProjectId,
                d.FileName,
                chunkCounts.TryGetValue(d.Id, out var count) ? count : 0))
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentChunkTally>>(tallies);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChunkOwnership>> GetChunkOwnershipAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = documentIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return Task.FromResult<IReadOnlyList<ChunkOwnership>>([]);

        var ownership = _chunks.Values
            .Where(c => ids.Contains(c.DocumentId))
            .Select(c => new ChunkOwnership(c.DocumentId, c.Id))
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkOwnership>>(ownership);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InterruptedUpdate>> GetInterruptedUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        var active = _documents.Values
            .Where(d => !d.IsSuperseded)
            .ToDictionary(d => d.Id, StringComparer.Ordinal);

        var interrupted = active.Values
            .Where(child => child.ParentDocumentId is not null
                            && active.ContainsKey(child.ParentDocumentId))
            .Select(child => new InterruptedUpdate(
                child.Id,
                child.FileName,
                child.ParentDocumentId!,
                active[child.ParentDocumentId!].FileName))
            .ToList();

        return Task.FromResult<IReadOnlyList<InterruptedUpdate>>(interrupted);
    }

    /// <inheritdoc />
    public Task<bool> HasActiveChildAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var hasChild = _documents.Values.Any(d =>
            !d.IsSuperseded
            && string.Equals(d.ParentDocumentId, documentId, StringComparison.Ordinal));

        return Task.FromResult(hasChild);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentFileLocation>> GetFileLocationsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var locations = _documents.Values
            .Where(d => string.Equals(d.ProjectId, projectId, StringComparison.Ordinal))
            .Select(d => new DocumentFileLocation(d.Id, d.FileStorageLocation))
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentFileLocation>>(locations);
    }

    #endregion
}
