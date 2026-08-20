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

    private DashboardMetricsService CreateSut() => new(_repository, _vectorStore);

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
}
