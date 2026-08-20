using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;

namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Computes the home dashboard's growth series and health indicators.
/// </summary>
public sealed class DashboardMetricsService
{
    private const int ProbeBatchSize = 500;

    private readonly IDocumentRepository _repository;
    private readonly IVectorStore _vectorStore;

    /// <summary>
    /// Creates a new <see cref="DashboardMetricsService"/>.
    /// </summary>
    /// <param name="repository">Document repository.</param>
    /// <param name="vectorStore">Vector store, for the embedding count and existence probe.</param>
    public DashboardMetricsService(
        IDocumentRepository repository,
        IVectorStore vectorStore)
    {
        _repository = repository;
        _vectorStore = vectorStore;
    }

    /// <summary>
    /// Reports which active documents are not fully searchable.
    /// </summary>
    /// <param name="forceFullReconciliation">
    /// When true, probes every chunk instead of trusting the count comparison.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The index health of the active document set.</returns>
    public async Task<IndexHealth> GetIndexHealthAsync(
        bool forceFullReconciliation = false,
        CancellationToken cancellationToken = default)
    {
        var tallies = await _repository.GetActiveDocumentChunkTalliesAsync(cancellationToken);
        var activeCount = tallies.Count;

        var broken = tallies.Where(t => t.ChunkCount == 0).ToList();
        var chunked = tallies.Where(t => t.ChunkCount > 0).ToList();

        // Fast path: two cheap counts. Reconciling every chunk on every page load would
        // cost O(total chunks), and in a healthy system it would find nothing.
        var chunkCount = await _repository.CountChunksAsync(cancellationToken);
        var embeddingCount = await _vectorStore.CountAsync(cancellationToken);

        if (forceFullReconciliation || chunkCount != embeddingCount)
        {
            broken.AddRange(await FindDocumentsMissingEmbeddingsAsync(chunked, cancellationToken));
        }

        var ordered = broken
            .OrderBy(t => t.FileName, StringComparer.CurrentCulture)
            .ToList();

        return new IndexHealth(activeCount, activeCount - ordered.Count, ordered);
    }

    private async Task<IReadOnlyList<DocumentChunkTally>> FindDocumentsMissingEmbeddingsAsync(
        IReadOnlyList<DocumentChunkTally> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return [];

        var ownership = await _repository.GetChunkOwnershipAsync(
            candidates.Select(c => c.DocumentId),
            cancellationToken);

        if (ownership.Count == 0)
            return [];

        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var batch in ownership.Select(o => o.ChunkId).Chunk(ProbeBatchSize))
        {
            var found = await _vectorStore.GetExistingChunkIdsAsync(batch, cancellationToken);
            foreach (var chunkId in found)
            {
                existing.Add(chunkId);
            }
        }

        // A document with even one unembedded chunk is only partly searchable, which is a
        // trap rather than a partial success — so it counts as broken.
        var affected = ownership
            .Where(o => !existing.Contains(o.ChunkId))
            .Select(o => o.DocumentId)
            .ToHashSet(StringComparer.Ordinal);

        return candidates.Where(c => affected.Contains(c.DocumentId)).ToList();
    }
}
