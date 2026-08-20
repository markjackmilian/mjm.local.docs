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
}
