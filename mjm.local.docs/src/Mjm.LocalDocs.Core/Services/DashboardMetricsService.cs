using System.Globalization;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;

namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Computes the home dashboard's growth series and health indicators.
/// </summary>
/// <remarks>
/// Bucketing happens in memory rather than in SQL: date bucketing is provider-specific
/// (<c>strftime</c> on SQLite, <c>DATEPART</c> on SQL Server) and this application supports
/// both, so a C# fold keeps the service provider-agnostic.
/// </remarks>
public sealed class DashboardMetricsService
{
    private const int ProbeBatchSize = 500;
    private const int BucketCount = 12;

    private readonly IDocumentRepository _repository;
    private readonly IVectorStore _vectorStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a new <see cref="DashboardMetricsService"/>.
    /// </summary>
    /// <param name="repository">Document repository.</param>
    /// <param name="vectorStore">Vector store, for the embedding count and existence probe.</param>
    /// <param name="timeProvider">Clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    public DashboardMetricsService(
        IDocumentRepository repository,
        IVectorStore vectorStore,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _vectorStore = vectorStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets everything the dashboard renders.
    /// </summary>
    /// <param name="granularity">Bucket size for the growth chart.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dashboard payload.</returns>
    public async Task<DashboardMetrics> GetMetricsAsync(
        GrowthGranularity granularity,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var firstBucketStart = GetFirstBucketStart(now, granularity);

        var contributions = await _repository.GetContributionsSinceAsync(firstBucketStart, cancellationToken);
        var growth = BuildBuckets(contributions, firstBucketStart, now, granularity);

        // Rolling windows, not calendar buckets: comparing an in-progress month against a
        // complete one would report a fictitious drop on the third of every month.
        var windowDays = granularity == GrowthGranularity.Weekly ? 7 : 30;
        var currentWindowStart = now.AddDays(-windowDays);
        var previousWindowStart = now.AddDays(-windowDays * 2);

        var newInPeriod = contributions.Count(c =>
            c.IsNewDocument && c.CreatedAt >= currentWindowStart);

        var newInPreviousPeriod = contributions.Count(c =>
            c.IsNewDocument && c.CreatedAt >= previousWindowStart && c.CreatedAt < currentWindowStart);

        var activeCount = await _repository.CountActiveDocumentsAsync(cancellationToken);
        var lastContribution = await _repository.GetLastContributionAtAsync(cancellationToken);
        var health = await GetIndexHealthAsync(forceFullReconciliation: false, cancellationToken);

        return new DashboardMetrics(
            activeCount,
            newInPeriod,
            newInPreviousPeriod,
            lastContribution,
            health,
            growth);
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
        var interrupted = await _repository.GetInterruptedUpdatesAsync(cancellationToken);

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

        // A list the UI renders should not reshuffle between refreshes, so order the
        // interrupted list the same way.
        var orderedInterrupted = interrupted
            .OrderBy(t => t.FileName, StringComparer.CurrentCulture)
            .ToList();

        return new IndexHealth(activeCount, activeCount - ordered.Count, ordered, orderedInterrupted);
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

    private static DateTimeOffset GetFirstBucketStart(
        DateTimeOffset now,
        GrowthGranularity granularity)
    {
        if (granularity == GrowthGranularity.Monthly)
        {
            var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
            return startOfMonth.AddMonths(-(BucketCount - 1));
        }

        return StartOfWeek(now).AddDays(-7 * (BucketCount - 1));
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset value)
    {
        var daysSinceMonday = ((int)value.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return new DateTimeOffset(value.Date.AddDays(-daysSinceMonday), value.Offset);
    }

    private static IReadOnlyList<GrowthBucket> BuildBuckets(
        IReadOnlyList<DocumentContribution> contributions,
        DateTimeOffset firstBucketStart,
        DateTimeOffset now,
        GrowthGranularity granularity)
    {
        var buckets = new List<GrowthBucket>(BucketCount);

        for (var i = 0; i < BucketCount; i++)
        {
            var start = granularity == GrowthGranularity.Monthly
                ? firstBucketStart.AddMonths(i)
                : firstBucketStart.AddDays(7 * i);

            var end = granularity == GrowthGranularity.Monthly
                ? start.AddMonths(1)
                : start.AddDays(7);

            var inBucket = contributions
                .Where(c => c.CreatedAt >= start && c.CreatedAt < end)
                .ToList();

            // Empty buckets are kept, never collapsed: a five-month stall must stay visible.
            buckets.Add(new GrowthBucket(
                start,
                FormatLabel(start, granularity),
                inBucket.Count(c => c.IsNewDocument),
                inBucket.Count(c => !c.IsNewDocument),
                now < end));
        }

        return buckets;
    }

    private static string FormatLabel(DateTimeOffset start, GrowthGranularity granularity)
    {
        return granularity == GrowthGranularity.Monthly
            ? start.ToString("MMM", CultureInfo.InvariantCulture)
            : start.ToString("dd/MM", CultureInfo.InvariantCulture);
    }
}
