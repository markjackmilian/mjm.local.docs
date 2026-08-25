using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;
using Mjm.LocalDocs.Core.Services;
using NSubstitute;

namespace Mjm.LocalDocs.Tests.Services;

/// <summary>
/// Unit tests for <see cref="DashboardMetricsService"/>.
/// </summary>
public sealed class DashboardMetricsServiceTests
{
    private readonly IDocumentRepository _repository = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IDocumentFileStorage _fileStorage = Substitute.For<IDocumentFileStorage>();

    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Fixed clock so bucket boundaries are deterministic, without taking a
    /// dependency on Microsoft.Extensions.TimeProvider.Testing.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private DashboardMetricsService CreateSut() =>
        new(_repository, _vectorStore, new FixedTimeProvider(Now), _fileStorage);

    private void GivenContributions(params DocumentContribution[] contributions)
    {
        _repository.GetContributionsSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(contributions.ToList());
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(0L);
    }

    private static DocumentChunkTally Tally(string id, int chunkCount) =>
        new(id, "proj-1", $"{id}.txt", chunkCount);

    [Fact]
    public async Task GetIndexHealthAsync_FlagsDocumentsWithZeroChunks()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 3), Tally("doc-2", 0)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(3L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(3L);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal(2, health.ActiveDocuments);
        Assert.Equal(1, health.FullyIndexed);
        Assert.Equal("doc-2", Assert.Single(health.Broken).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenCountsAgree_SkipsTheProbeEntirely()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 3)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(3L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(3L);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Empty(health.Broken);
        await _vectorStore.DidNotReceive().GetExistingChunkIdsAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenCountsDiverge_FindsDocumentsMissingEmbeddings()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 2), Tally("doc-2", 2)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(4L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(2L);

        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkOwnership("doc-1", "doc-1_chunk_0"),
                new ChunkOwnership("doc-1", "doc-1_chunk_1"),
                new ChunkOwnership("doc-2", "doc-2_chunk_0"),
                new ChunkOwnership("doc-2", "doc-2_chunk_1")
            ]);

        // Only doc-1's embeddings made it into the store.
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(["doc-1_chunk_0", "doc-1_chunk_1"]);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal("doc-2", Assert.Single(health.Broken).DocumentId);
        Assert.Equal(1, health.FullyIndexed);
    }

    [Fact]
    public async Task GetIndexHealthAsync_TreatsPartiallyEmbeddedDocumentAsBroken()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 2)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(2L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkOwnership("doc-1", "doc-1_chunk_0"),
                new ChunkOwnership("doc-1", "doc-1_chunk_1")
            ]);
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(["doc-1_chunk_0"]);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal("doc-1", Assert.Single(health.Broken).DocumentId);
        Assert.Equal(0, health.FullyIndexed);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithForceFlag_ProbesEvenWhenCountsAgree()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChunkOwnership("doc-1", "doc-1_chunk_0")]);
        // An orphan embedding elsewhere made the totals agree while this chunk has none.
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        Assert.Equal("doc-1", Assert.Single(health.Broken).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_ProbesInBatchesOfFiveHundred()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1200)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1200L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1199L);

        var ownership = Enumerable.Range(0, 1200)
            .Select(i => new ChunkOwnership("doc-1", $"doc-1_chunk_{i}"))
            .ToList();
        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ownership);
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateSut().GetIndexHealthAsync();

        // 1200 ids => 500 + 500 + 200
        await _vectorStore.Received(3).GetExistingChunkIdsAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithNoActiveDocuments_ReportsAllClear()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(0L);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal(0, health.ActiveDocuments);
        Assert.Equal(0, health.FullyIndexed);
        Assert.Empty(health.Broken);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_ReturnsTwelveBucketsOldestFirst()
    {
        GivenContributions();

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(12, metrics.Growth.Count);
        Assert.Equal(new DateTimeOffset(2025, 9, 1, 0, 0, 0, TimeSpan.Zero), metrics.Growth[0].Start);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), metrics.Growth[11].Start);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_MarksOnlyTheCurrentBucketPartial()
    {
        GivenContributions();

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.True(metrics.Growth[11].IsPartial);
        Assert.All(metrics.Growth.Take(11), b => Assert.False(b.IsPartial));
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_SplitsNewDocumentsFromVersions()
    {
        GivenContributions(
            new DocumentContribution(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero), false));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(2, metrics.Growth[11].NewDocuments);
        Assert.Equal(1, metrics.Growth[11].NewVersions);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_KeepsEmptyBucketsSoStallsStayVisible()
    {
        GivenContributions(
            new DocumentContribution(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        var march = metrics.Growth.Single(b => b.Start.Month == 3 && b.Start.Year == 2026);
        Assert.Equal(1, march.NewDocuments);
        Assert.Equal(11, metrics.Growth.Count(b => b.NewDocuments == 0 && b.NewVersions == 0));
    }

    [Fact]
    public async Task GetMetricsAsync_Weekly_BucketsStartOnMonday()
    {
        GivenContributions();

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Weekly);

        Assert.Equal(12, metrics.Growth.Count);
        Assert.All(metrics.Growth, b => Assert.Equal(DayOfWeek.Monday, b.Start.DayOfWeek));
        // 2026-08-20 is a Thursday, so the current week starts Monday 2026-08-17.
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero), metrics.Growth[11].Start);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_ComparesRollingThirtyDayWindows()
    {
        GivenContributions(
            // Inside the last 30 days (on or after 2026-07-21).
            new DocumentContribution(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), true),
            // Inside the previous 30 days (2026-06-21 .. 2026-07-20).
            new DocumentContribution(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), true),
            // A version, which must not count towards either window.
            new DocumentContribution(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero), false));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(2, metrics.NewInPeriod);
        Assert.Equal(1, metrics.NewInPreviousPeriod);
    }

    [Fact]
    public async Task GetMetricsAsync_Weekly_ComparesRollingSevenDayWindows()
    {
        GivenContributions(
            new DocumentContribution(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Weekly);

        Assert.Equal(1, metrics.NewInPeriod);
        Assert.Equal(1, metrics.NewInPreviousPeriod);
    }

    [Fact]
    public async Task GetMetricsAsync_SurfacesLastContributionFromOutsideTheChartWindow()
    {
        GivenContributions();
        var ancient = new DateTimeOffset(2024, 2, 1, 9, 0, 0, TimeSpan.Zero);
        _repository.GetLastContributionAtAsync(Arg.Any<CancellationToken>()).Returns(ancient);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(ancient, metrics.LastContributionAt);
    }

    [Fact]
    public async Task GetMetricsAsync_WithEmptyKnowledgeBase_ReportsNullLastContribution()
    {
        GivenContributions();
        _repository.GetLastContributionAtAsync(Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Null(metrics.LastContributionAt);
        Assert.Equal(0, metrics.ActiveDocumentCount);
    }

    [Fact]
    public async Task GetMetricsAsync_ReadsActiveCountFromTheRepository()
    {
        GivenContributions();
        _repository.CountActiveDocumentsAsync(Arg.Any<CancellationToken>()).Returns(412);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(412, metrics.ActiveDocumentCount);
    }

    [Fact]
    public async Task GetMetricsAsync_CarriesIndexHealthIntoThePayload()
    {
        GivenContributions();
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 0)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(0L);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal("doc-1", Assert.Single(metrics.Health.Broken).DocumentId);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_SplitsABucketBoundaryInstantIntoTheLaterBucket()
    {
        var augustStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        GivenContributions(
            // Exactly the August bucket's start instant.
            new DocumentContribution(augustStart, true),
            // One tick earlier, so still inside July.
            new DocumentContribution(augustStart.AddTicks(-1), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        // Buckets are half-open [start, end), so the boundary instant belongs to August alone:
        // one contribution each, never both in one bucket and never dropped from both.
        Assert.Equal(1, metrics.Growth[10].NewDocuments);
        Assert.Equal(1, metrics.Growth[11].NewDocuments);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_CountsAWindowBoundaryInstantAsCurrent()
    {
        // The clock is 2026-08-20T10:00:00Z, so the rolling 30-day window starts exactly here.
        var windowStart = new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero);

        GivenContributions(
            new DocumentContribution(windowStart, true),
            new DocumentContribution(windowStart.AddTicks(-1), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        // The window is half-open too: the boundary instant is current, one tick earlier is previous.
        Assert.Equal(1, metrics.NewInPeriod);
        Assert.Equal(1, metrics.NewInPreviousPeriod);
    }

    [Fact]
    public async Task GetIndexHealthAsync_CarriesInterruptedUpdates()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 2), Tally("doc-2", 2)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(4L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(4L);
        _repository.GetInterruptedUpdatesAsync(Arg.Any<CancellationToken>())
            .Returns([new InterruptedUpdate("doc-2", "doc-2.txt", "doc-1", "doc-1.txt")]);

        var health = await CreateSut().GetIndexHealthAsync();

        // Both documents are perfectly indexed — that is exactly why this needs its own list
        // rather than being folded into Broken.
        Assert.Empty(health.Broken);
        Assert.Equal("doc-2", Assert.Single(health.InterruptedUpdates).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithoutForcing_DoesNotTouchFileStorage()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var health = await CreateSut().GetIndexHealthAsync();

        // One storage round trip per document, against a blob account, is not something the home
        // page pays on every load.
        Assert.Empty(health.MissingFiles);
        await _repository.DidNotReceive().GetActiveExternalFilesAsync(Arg.Any<CancellationToken>());
        await _fileStorage.DidNotReceive().FileExistsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenForced_ReportsAFileThatNoLongerExists()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _repository.GetActiveExternalFilesAsync(Arg.Any<CancellationToken>())
            .Returns([new MissingFile("doc-1", "doc-1.pdf", "proj-1/doc-1.pdf")]);
        _fileStorage.FileExistsAsync("doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>())
            .Returns(false);

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        // Searchable, fully indexed, and undownloadable. Nothing else in this payload can say so.
        Assert.Empty(health.Broken);
        Assert.Equal("doc-1", Assert.Single(health.MissingFiles).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenForced_IgnoresFilesThatStillExist()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _repository.GetActiveExternalFilesAsync(Arg.Any<CancellationToken>())
            .Returns([new MissingFile("doc-1", "doc-1.pdf", "proj-1/doc-1.pdf")]);
        _fileStorage.FileExistsAsync("doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>())
            .Returns(true);

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        Assert.Empty(health.MissingFiles);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithNoFileStorageConfigured_ReportsNoMissingFiles()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var sut = new DashboardMetricsService(_repository, _vectorStore, new FixedTimeProvider(Now), fileStorage: null);

        var health = await sut.GetIndexHealthAsync(forceFullReconciliation: true);

        // Nothing to check, and no read wasted asking.
        Assert.Empty(health.MissingFiles);
        await _repository.DidNotReceive().GetActiveExternalFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenAFileProbeThrows_TheBrokenListComputedEarlierStillComesBack()
    {
        // Broken is computed from the chunk tallies, entirely before the file check runs.
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 0)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _repository.GetActiveExternalFilesAsync(Arg.Any<CancellationToken>())
            .Returns([new MissingFile("doc-2", "doc-2.pdf", "proj-1/doc-2.pdf")]);
        _fileStorage.FileExistsAsync("doc-2", "proj-1/doc-2.pdf", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("storage unavailable")));

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        // A storage hiccup unrelated to doc-1 must not discard a result that has nothing to
        // do with storage.
        Assert.Equal("doc-1", Assert.Single(health.Broken).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenAFileProbeThrows_CountsItAsUnverifiedNotMissing()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _repository.GetActiveExternalFilesAsync(Arg.Any<CancellationToken>())
            .Returns([new MissingFile("doc-1", "doc-1.pdf", "proj-1/doc-1.pdf")]);
        _fileStorage.FileExistsAsync("doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("storage unavailable")));

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        // A document whose probe threw is not confirmed missing — MissingFiles tells the user
        // reindexing will not help, which would be false for a file that was never actually
        // checked.
        Assert.Equal(1, health.UnverifiedFiles);
        Assert.Empty(health.MissingFiles);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithOneThrowingAndOneMissingProbe_ReportsOneOfEach()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1), Tally("doc-2", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(2L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(2L);
        _repository.GetActiveExternalFilesAsync(Arg.Any<CancellationToken>())
            .Returns([
                new MissingFile("doc-1", "doc-1.pdf", "proj-1/doc-1.pdf"),
                new MissingFile("doc-2", "doc-2.pdf", "proj-1/doc-2.pdf")
            ]);
        _fileStorage.FileExistsAsync("doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>())
            .Returns(false);
        _fileStorage.FileExistsAsync("doc-2", "proj-1/doc-2.pdf", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("storage unavailable")));

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        Assert.Equal("doc-1", Assert.Single(health.MissingFiles).DocumentId);
        Assert.Equal(1, health.UnverifiedFiles);
    }
}
