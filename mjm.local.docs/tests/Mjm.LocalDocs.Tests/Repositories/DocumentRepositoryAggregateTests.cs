using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;

namespace Mjm.LocalDocs.Tests.Repositories;

/// <summary>
/// Dashboard aggregate reads, specified once and run against every
/// <see cref="IDocumentRepository"/> implementation via a concrete subclass.
/// </summary>
public abstract class DocumentRepositoryAggregateTests
{
    /// <summary>The repository under test, supplied by the concrete fixture.</summary>
    protected abstract IDocumentRepository Sut { get; }

    /// <summary>
    /// Ensures a project row exists, for fixtures backed by a store that enforces the
    /// Documents-to-Projects foreign key. The in-memory repository has no such constraint,
    /// so the default does nothing.
    /// </summary>
    protected virtual Task EnsureProjectAsync(string projectId) => Task.CompletedTask;

    // All timestamps are UTC on purpose: EF Core stores DateTimeOffset as TEXT on SQLite,
    // so MAX() is lexicographic and only agrees with chronological order at a fixed offset.
    protected static readonly DateTimeOffset Jan = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    protected static readonly DateTimeOffset Feb = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);
    protected static readonly DateTimeOffset Mar = new(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);

    protected async Task SeedDocumentAsync(
        string id,
        string projectId = "proj-1",
        string? parentDocumentId = null,
        bool isSuperseded = false,
        DateTimeOffset? createdAt = null,
        int chunkCount = 0)
    {
        var document = new Document
        {
            Id = id,
            ProjectId = projectId,
            FileName = $"{id}.txt",
            FileExtension = ".txt",
            FileSizeBytes = 10,
            ExtractedText = "text",
            ParentDocumentId = parentDocumentId,
            IsSuperseded = isSuperseded,
            CreatedAt = createdAt ?? Jan
        };

        await EnsureProjectAsync(projectId);
        await Sut.AddDocumentAsync(document);

        if (chunkCount > 0)
        {
            var chunks = Enumerable.Range(0, chunkCount).Select(i => new DocumentChunk
            {
                Id = $"{id}_chunk_{i}",
                DocumentId = id,
                Content = $"chunk {i}",
                ChunkIndex = i,
                FileName = $"{id}.txt"
            });

            await Sut.AddChunksAsync(chunks);
        }
    }

    [Fact]
    public async Task GetContributionsSinceAsync_ReturnsOnlyDocumentsAtOrAfterTheBoundary()
    {
        await SeedDocumentAsync("doc-old", createdAt: Jan);
        await SeedDocumentAsync("doc-new", createdAt: Mar);

        var contributions = await Sut.GetContributionsSinceAsync(Feb);

        Assert.Single(contributions);
        Assert.Equal(Mar, contributions[0].CreatedAt);
    }

    [Fact]
    public async Task GetContributionsSinceAsync_MarksParentedDocumentsAsVersions()
    {
        await SeedDocumentAsync("doc-1", createdAt: Feb);
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1", createdAt: Mar);

        var contributions = await Sut.GetContributionsSinceAsync(Jan);

        Assert.Equal(1, contributions.Count(c => c.IsNewDocument));
        Assert.Equal(1, contributions.Count(c => !c.IsNewDocument));
    }

    [Fact]
    public async Task GetContributionsSinceAsync_IncludesSupersededDocuments()
    {
        await SeedDocumentAsync("doc-1", isSuperseded: true, createdAt: Feb);

        var contributions = await Sut.GetContributionsSinceAsync(Jan);

        Assert.Single(contributions);
    }

    [Fact]
    public async Task CountActiveDocumentsAsync_ExcludesSuperseded()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", isSuperseded: true);

        var count = await Sut.CountActiveDocumentsAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetLastContributionAtAsync_WithNoDocuments_ReturnsNull()
    {
        var last = await Sut.GetLastContributionAtAsync();

        Assert.Null(last);
    }

    [Fact]
    public async Task GetLastContributionAtAsync_ReturnsMostRecentIncludingSuperseded()
    {
        await SeedDocumentAsync("doc-1", createdAt: Jan);
        await SeedDocumentAsync("doc-2", isSuperseded: true, createdAt: Mar);

        var last = await Sut.GetLastContributionAtAsync();

        Assert.Equal(Mar, last);
    }

    [Fact]
    public async Task GetActiveDocumentCountsByProjectAsync_GroupsAndExcludesSuperseded()
    {
        await SeedDocumentAsync("doc-1", projectId: "proj-a");
        await SeedDocumentAsync("doc-2", projectId: "proj-a");
        await SeedDocumentAsync("doc-3", projectId: "proj-b");
        await SeedDocumentAsync("doc-4", projectId: "proj-b", isSuperseded: true);

        var counts = await Sut.GetActiveDocumentCountsByProjectAsync();

        Assert.Equal(2, counts["proj-a"]);
        Assert.Equal(1, counts["proj-b"]);
    }

    [Fact]
    public async Task CountChunksAsync_ReturnsTotalAcrossDocuments()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 3);
        await SeedDocumentAsync("doc-2", chunkCount: 2);

        var count = await Sut.CountChunksAsync();

        Assert.Equal(5L, count);
    }

    [Fact]
    public async Task GetActiveDocumentChunkTalliesAsync_ExcludesSupersededAndReportsCounts()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 3);
        await SeedDocumentAsync("doc-2", chunkCount: 0);
        await SeedDocumentAsync("doc-3", isSuperseded: true, chunkCount: 4);

        var tallies = await Sut.GetActiveDocumentChunkTalliesAsync();

        Assert.Equal(2, tallies.Count);
        Assert.Equal(3, tallies.Single(t => t.DocumentId == "doc-1").ChunkCount);
        Assert.Equal(0, tallies.Single(t => t.DocumentId == "doc-2").ChunkCount);
    }

    [Fact]
    public async Task GetActiveDocumentChunkTalliesAsync_CarriesProjectAndFileName()
    {
        await SeedDocumentAsync("doc-1", projectId: "proj-x", chunkCount: 1);

        var tallies = await Sut.GetActiveDocumentChunkTalliesAsync();

        var tally = Assert.Single(tallies);
        Assert.Equal("proj-x", tally.ProjectId);
        Assert.Equal("doc-1.txt", tally.FileName);
    }

    [Fact]
    public async Task GetChunkOwnershipAsync_ReturnsChunksPairedWithTheirDocument()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 2);
        await SeedDocumentAsync("doc-2", chunkCount: 1);

        var ownership = await Sut.GetChunkOwnershipAsync(["doc-1"]);

        Assert.Equal(2, ownership.Count);
        Assert.All(ownership, o => Assert.Equal("doc-1", o.DocumentId));
        Assert.Contains(ownership, o => o.ChunkId == "doc-1_chunk_0");
    }

    [Fact]
    public async Task GetChunkOwnershipAsync_WithEmptyInput_ReturnsEmpty()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 2);

        var ownership = await Sut.GetChunkOwnershipAsync([]);

        Assert.Empty(ownership);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_WhenTheParentWasRetired_ReturnsNothing()
    {
        await SeedDocumentAsync("doc-1", isSuperseded: true);
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_WhenBothVersionsAreActive_ReturnsThePair()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        var pair = Assert.Single(interrupted);
        Assert.Equal("doc-2", pair.DocumentId);
        Assert.Equal("doc-1", pair.ParentDocumentId);
        Assert.Equal("doc-2.txt", pair.FileName);
        Assert.Equal("doc-1.txt", pair.ParentFileName);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_IgnoresADocumentWithNoParent()
    {
        await SeedDocumentAsync("doc-1");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_IgnoresASupersededChild()
    {
        // A retired child whose parent is somehow still active is not an interrupted update the
        // reindex path can close, so it must not be offered as one.
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1", isSuperseded: true);

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_IgnoresAChildWhoseParentNoLongerExists()
    {
        // ParentDocumentId carries no foreign-key constraint, so a hard-deleted parent leaves a
        // dangling reference. That is not an interrupted update — there is no older version left
        // to retire — and reporting one would offer a repair with nothing to repair.
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-gone");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_ReportsEachLinkOfADoublyInterruptedChain()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");
        await SeedDocumentAsync("doc-3", parentDocumentId: "doc-2");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Equal(2, interrupted.Count);
        Assert.Contains(interrupted, i => i.DocumentId == "doc-2" && i.ParentDocumentId == "doc-1");
        Assert.Contains(interrupted, i => i.DocumentId == "doc-3" && i.ParentDocumentId == "doc-2");
    }

    [Fact]
    public async Task HasActiveChildAsync_WithAnActiveNewerVersion_ReturnsTrue()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");

        Assert.True(await Sut.HasActiveChildAsync("doc-1"));
    }

    [Fact]
    public async Task HasActiveChildAsync_WhenTheNewerVersionWasRetired_ReturnsFalse()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1", isSuperseded: true);

        Assert.False(await Sut.HasActiveChildAsync("doc-1"));
    }

    [Fact]
    public async Task HasActiveChildAsync_WithNoChildAtAll_ReturnsFalse()
    {
        await SeedDocumentAsync("doc-1");

        Assert.False(await Sut.HasActiveChildAsync("doc-1"));
    }

    [Fact]
    public async Task GetFileLocationsByProjectAsync_ReturnsEveryDocumentInTheProject()
    {
        await SeedDocumentAsync("doc-1", projectId: "proj-a");
        await SeedDocumentAsync("doc-2", projectId: "proj-a");
        await SeedDocumentAsync("doc-3", projectId: "proj-b");

        var locations = await Sut.GetFileLocationsByProjectAsync("proj-a");

        Assert.Equal(2, locations.Count);
        Assert.Contains(locations, l => l.DocumentId == "doc-1");
        Assert.Contains(locations, l => l.DocumentId == "doc-2");
    }

    [Fact]
    public async Task GetFileLocationsByProjectAsync_IncludesSupersededDocuments()
    {
        // A superseded version still owns an external file and may still own embeddings, so
        // deletion has to visit it too.
        await SeedDocumentAsync("doc-1", projectId: "proj-a", isSuperseded: true);

        var locations = await Sut.GetFileLocationsByProjectAsync("proj-a");

        Assert.Single(locations);
    }

    [Fact]
    public async Task GetFileLocationsByProjectAsync_WithNoDocuments_ReturnsEmpty()
    {
        var locations = await Sut.GetFileLocationsByProjectAsync("proj-none");

        Assert.Empty(locations);
    }
}
