using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Models.Dashboard;
using Mjm.LocalDocs.Infrastructure.Persistence.Entities;

namespace Mjm.LocalDocs.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of document repository.
/// Supports SQLite, SQL Server, and other EF Core providers.
/// </summary>
public sealed class EfCoreDocumentRepository : IDocumentRepository
{
    private readonly LocalDocsDbContext _context;

    public EfCoreDocumentRepository(LocalDocsDbContext context)
    {
        _context = context;
    }

    #region Document Operations

    /// <inheritdoc />
    public async Task<Document> AddDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        var entity = new DocumentEntity
        {
            Id = document.Id,
            ProjectId = document.ProjectId,
            FileName = document.FileName,
            FileExtension = document.FileExtension,
            FileContent = document.FileContent,
            FileStorageLocation = document.FileStorageLocation,
            FileSizeBytes = document.FileSizeBytes,
            ExtractedText = document.ExtractedText,
            ContentHash = document.ContentHash,
            MetadataJson = document.Metadata != null
                ? JsonSerializer.Serialize(document.Metadata)
                : null,
            VersionNumber = document.VersionNumber,
            ParentDocumentId = document.ParentDocumentId,
            IsSuperseded = document.IsSuperseded,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt
        };

        _context.Documents.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return document;
    }

    /// <inheritdoc />
    public async Task<Document?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        return entity == null ? null : MapToModel(entity);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetDocumentFileAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Documents
            .AsNoTracking()
            .Select(d => new { d.Id, d.FileContent })
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        return entity?.FileContent;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> GetDocumentsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.Documents
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.FileName)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (entity == null)
        {
            return false;
        }

        _context.Documents.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DocumentExistsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AnyAsync(d => d.Id == documentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Document?> GetDocumentByHashAsync(
        string projectId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.ProjectId == projectId && d.ContentHash == contentHash,
                cancellationToken);

        return entity == null ? null : MapToModel(entity);
    }

    /// <inheritdoc />
    public async Task SupersedeDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (entity is null)
            return;

        entity.IsSuperseded = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> GetDocumentVersionsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        // Start from the given document
        var startDoc = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (startDoc is null)
            return [];

        // Collect all documents in this version chain.
        // Walk up to find the root (oldest version with no parent).
        var rootId = startDoc.Id;
        var currentParentId = startDoc.ParentDocumentId;
        var visited = new HashSet<string> { rootId };

        while (!string.IsNullOrEmpty(currentParentId) && visited.Add(currentParentId))
        {
            var parent = await _context.Documents
                .AsNoTracking()
                .Select(d => new { d.Id, d.ParentDocumentId })
                .FirstOrDefaultAsync(d => d.Id == currentParentId, cancellationToken);

            if (parent is null)
                break;

            rootId = parent.Id;
            currentParentId = parent.ParentDocumentId;
        }

        // Now walk down from the root collecting all versions.
        // Use a single query: all documents in the same project that share the version chain.
        // Since the chain is linked via ParentDocumentId, we collect iteratively.
        var chain = new List<DocumentEntity>();
        var currentId = rootId;
        var visitedDown = new HashSet<string>();

        while (!string.IsNullOrEmpty(currentId) && visitedDown.Add(currentId))
        {
            var doc = await _context.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == currentId, cancellationToken);

            if (doc is null)
                break;

            chain.Add(doc);

            // Find the child that has this document as parent
            var child = await _context.Documents
                .AsNoTracking()
                .Select(d => new { d.Id, d.ParentDocumentId })
                .FirstOrDefaultAsync(d => d.ParentDocumentId == currentId, cancellationToken);

            currentId = child?.Id;
        }

        return chain
            .OrderByDescending(e => e.VersionNumber)
            .Select(MapToModel)
            .ToList();
    }

    #endregion

    #region Chunk Operations

    /// <inheritdoc />
    public async Task AddChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var entities = chunks.Select(c => new DocumentChunkEntity
        {
            Id = c.Id,
            DocumentId = c.DocumentId,
            Content = c.Content,
            FileName = c.FileName,
            ChunkIndex = c.ChunkIndex,
            CreatedAt = c.CreatedAt
        }).ToList();

        _context.DocumentChunks.AddRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DocumentChunk?> GetChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentChunks
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chunkId, cancellationToken);

        return entity == null ? null : MapToChunkModel(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunk>> GetChunksByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunkModel).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var idList = chunkIds.ToList();

        var entities = await _context.DocumentChunks
            .AsNoTracking()
            .Where(c => idList.Contains(c.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunkModel).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteChunksByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        _context.DocumentChunks.RemoveRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Project Operations

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetProjectsWithDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .Select(d => d.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteDocumentsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.Documents
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        _context.Documents.RemoveRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Dashboard Aggregates

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentContribution>> GetContributionsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        // The projection translates; a `Where(d => d.CreatedAt >= since)` does not — the SQLite
        // provider refuses relational comparisons on DateTimeOffset. So read the two scalar
        // columns and apply the window in memory. This stays cheap because the projection never
        // touches FileContent; materialising DocumentEntity is the bug this method exists to avoid.
        var contributions = await _context.Documents
            .AsNoTracking()
            .Select(d => new DocumentContribution(d.CreatedAt, d.ParentDocumentId == null))
            .ToListAsync(cancellationToken);

        return contributions.Where(c => c.CreatedAt >= since).ToList();
    }

    /// <inheritdoc />
    public Task<int> CountActiveDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return _context.Documents
            .AsNoTracking()
            .CountAsync(d => !d.IsSuperseded, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastContributionAtAsync(
        CancellationToken cancellationToken = default)
    {
        // MaxAsync on a DateTimeOffset column throws on the SQLite provider, so project the
        // single column and aggregate in memory.
        var timestamps = await _context.Documents
            .AsNoTracking()
            .Select(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        return timestamps.Count == 0 ? null : timestamps.Max();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> GetActiveDocumentCountsByProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Documents
            .AsNoTracking()
            .Where(d => !d.IsSuperseded)
            .GroupBy(d => d.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ProjectId, r => r.Count);
    }

    /// <inheritdoc />
    public Task<long> CountChunksAsync(CancellationToken cancellationToken = default)
    {
        return _context.DocumentChunks
            .AsNoTracking()
            .LongCountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunkTally>> GetActiveDocumentChunkTalliesAsync(
        CancellationToken cancellationToken = default)
    {
        // d.Chunks.Count becomes a correlated COUNT subquery: no chunk rows are materialised.
        return await _context.Documents
            .AsNoTracking()
            .Where(d => !d.IsSuperseded)
            .Select(d => new DocumentChunkTally(d.Id, d.ProjectId, d.FileName, d.Chunks.Count))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkOwnership>> GetChunkOwnershipAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = documentIds.ToList();
        if (ids.Count == 0)
            return [];

        return await _context.DocumentChunks
            .AsNoTracking()
            .Where(c => ids.Contains(c.DocumentId))
            .Select(c => new ChunkOwnership(c.DocumentId, c.Id))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InterruptedUpdate>> GetInterruptedUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        // A self-join over the two flags. Only four scalar columns are projected, so this never
        // touches ExtractedText or FileContent.
        return await _context.Documents
            .AsNoTracking()
            .Where(child => !child.IsSuperseded && child.ParentDocumentId != null)
            .Join(
                _context.Documents.AsNoTracking().Where(parent => !parent.IsSuperseded),
                child => child.ParentDocumentId,
                parent => parent.Id,
                (child, parent) => new InterruptedUpdate(
                    child.Id,
                    child.FileName,
                    parent.Id,
                    parent.FileName))
            .ToListAsync(cancellationToken);
    }

    #endregion

    #region Private Helpers

    private static Document MapToModel(DocumentEntity entity)
    {
        return new Document
        {
            Id = entity.Id,
            ProjectId = entity.ProjectId,
            FileName = entity.FileName,
            FileExtension = entity.FileExtension,
            FileContent = entity.FileContent,
            FileStorageLocation = entity.FileStorageLocation,
            FileSizeBytes = entity.FileSizeBytes,
            ExtractedText = entity.ExtractedText,
            ContentHash = entity.ContentHash,
            Metadata = !string.IsNullOrEmpty(entity.MetadataJson)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MetadataJson)
                : null,
            VersionNumber = entity.VersionNumber,
            ParentDocumentId = entity.ParentDocumentId,
            IsSuperseded = entity.IsSuperseded,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static DocumentChunk MapToChunkModel(DocumentChunkEntity entity)
    {
        return new DocumentChunk
        {
            Id = entity.Id,
            DocumentId = entity.DocumentId,
            Content = entity.Content,
            FileName = entity.FileName,
            ChunkIndex = entity.ChunkIndex,
            CreatedAt = entity.CreatedAt
        };
    }

    #endregion
}
