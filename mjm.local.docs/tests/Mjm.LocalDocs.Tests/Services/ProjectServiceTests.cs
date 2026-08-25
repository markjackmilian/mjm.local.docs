using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;
using Mjm.LocalDocs.Core.Services;
using NSubstitute;

namespace Mjm.LocalDocs.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ProjectService"/>.
/// </summary>
public sealed class ProjectServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly IDocumentRepository _documents = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IDocumentFileStorage _fileStorage = Substitute.For<IDocumentFileStorage>();

    private ProjectService CreateSut(bool withFileStorage = true) =>
        new(_projects, _documents, _vectorStore, withFileStorage ? _fileStorage : null);

    [Fact]
    public async Task DeleteProjectAsync_RemovesEveryDocumentsEmbeddings()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([
                new DocumentFileLocation("doc-1", null),
                new DocumentFileLocation("doc-2", null)
            ]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // chunk_embeddings is not an EF table, so no cascade reaches it.
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteProjectAsync_RemovesExternallyStoredFiles()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", "proj-1/doc-1.pdf")]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // With FileSystem or AzureBlob storage the row holds only a path, so the cascade leaves
        // the file behind — on disk, or costing money in a blob account.
        await _fileStorage.Received(1).DeleteFileAsync(
            "doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteProjectAsync_WithDatabaseStorage_DoesNotCallFileStorage()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", null)]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // A null location means the content lives in the row and goes with the cascade.
        await _fileStorage.DidNotReceive().DeleteFileAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteProjectAsync_StripsContentBeforeDeletingTheProject()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", null)]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // Deleting the project first would cascade the document rows away, losing the very ids
        // needed to find the embeddings and files that outlive them.
        Received.InOrder(() =>
        {
            _vectorStore.DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
            _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DeleteProjectAsync_WhenTheProjectDoesNotExist_ReturnsFalse()
    {
        _documents.GetFileLocationsByProjectAsync("proj-none", Arg.Any<CancellationToken>())
            .Returns([]);
        _projects.DeleteAsync("proj-none", Arg.Any<CancellationToken>()).Returns(false);

        var deleted = await CreateSut().DeleteProjectAsync("proj-none");

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteProjectAsync_WithNoFileStorageConfigured_StillRemovesEmbeddings()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", "proj-1/doc-1.pdf")]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut(withFileStorage: false).DeleteProjectAsync("proj-1");

        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
    }
}
