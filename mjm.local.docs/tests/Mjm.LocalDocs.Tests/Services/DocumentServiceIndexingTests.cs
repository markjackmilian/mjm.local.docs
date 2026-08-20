using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Mjm.LocalDocs.Tests.Services;

/// <summary>
/// Tests for indexing failure handling and repair in <see cref="DocumentService"/>.
/// </summary>
public sealed class DocumentServiceIndexingTests
{
    private readonly IDocumentRepository _repository = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IDocumentProcessor _processor = Substitute.For<IDocumentProcessor>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly DocumentService _sut;

    public DocumentServiceIndexingTests()
    {
        _sut = new DocumentService(_repository, _vectorStore, _processor, _embeddingService);
    }

    private static Document CreateDocument(
        string id = "doc-1",
        string? parentDocumentId = null,
        bool isSuperseded = false)
    {
        return new Document
        {
            Id = id,
            ProjectId = "proj-1",
            FileName = $"{id}.txt",
            FileExtension = ".txt",
            FileContent = "Test content"u8.ToArray(),
            FileSizeBytes = 12,
            ExtractedText = "Test content for chunking",
            ParentDocumentId = parentDocumentId,
            IsSuperseded = isSuperseded
        };
    }

    private void GivenChunks(string documentId, int count)
    {
        var chunks = Enumerable.Range(0, count).Select(i => new DocumentChunk
        {
            Id = $"{documentId}_chunk_{i}",
            DocumentId = documentId,
            Content = $"chunk {i}",
            ChunkIndex = i,
            FileName = $"{documentId}.txt"
        }).ToList();

        _processor.ChunkDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(chunks);
    }

    [Fact]
    public async Task AddDocumentAsync_WhenEmbeddingFails_ThrowsDocumentIndexingException()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.AddDocumentAsync(document));

        Assert.Equal("doc-1", ex.DocumentId);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task AddDocumentAsync_WhenEmbeddingFails_KeepsDocumentButDiscardsChunks()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

        await Assert.ThrowsAsync<DocumentIndexingException>(() => _sut.AddDocumentAsync(document));

        // The uploaded file is not thrown away...
        await _repository.Received(1).AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        // ...but the half-written index is.
        await _repository.Received(1).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddDocumentAsync_WhenCallerCancels_PropagatesCancellationUnwrapped()
    {
        var document = CreateDocument();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.AddDocumentAsync(document, cts.Token));

        // The compensation must not inherit the cancelled token, or it could not run at all.
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(
            "doc-1", Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
        await _repository.Received(1).DeleteChunksByDocumentAsync(
            "doc-1", Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
    }

    [Fact]
    public async Task AddDocumentAsync_WhenProviderTimesOut_WrapsTheTaskCanceledException()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        // An HttpClient timeout surfaces as TaskCanceledException with the caller's token intact,
        // so it must be wrapped like any other provider failure, not mistaken for cancellation.
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.AddDocumentAsync(document));

        Assert.Equal("doc-1", ex.DocumentId);
        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task AddDocumentAsync_WhenCleanupAlsoFails_StillReportsTheOriginalCause()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider unreachable"));
        _vectorStore.DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cleanup exploded"));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.AddDocumentAsync(document));

        Assert.IsType<HttpRequestException>(ex.InnerException);
        // The chunk delete must still run even though the vector delete threw.
        await _repository.Received(1).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddDocumentAsync_OnSuccess_StoresEmbeddingsAndDoesNotClean()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }, new float[] { 0.2f }]);

        await _sut.AddDocumentAsync(document);

        await _vectorStore.Received(1).UpsertBatchAsync(
            Arg.Any<IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>>>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_RebuildsChunksAndEmbeddings()
    {
        var document = CreateDocument();
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }, new float[] { 0.2f }]);

        await _sut.ReindexDocumentAsync("doc-1");

        await _repository.Received(1).AddChunksAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).UpsertBatchAsync(
            Arg.Any<IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WipesBeforeRebuildingSoRetriesAreIdempotent()
    {
        var document = CreateDocument();
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(document);
        GivenChunks("doc-1", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-1");
        await _sut.ReindexDocumentAsync("doc-1");

        await _repository.Received(2).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _repository.Received(2).AddChunksAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenDocumentMissing_Throws()
    {
        _repository.GetDocumentAsync("nope", Arg.Any<CancellationToken>())
            .Returns((Document?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ReindexDocumentAsync("nope"));
    }

    [Fact]
    public async Task ReindexDocumentAsync_RefusesSupersededDocuments()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .Returns(CreateDocument(isSuperseded: true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ReindexDocumentAsync("doc-1"));

        await _repository.DidNotReceive().AddChunksAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenEmbeddingFailsAgain_ThrowsAndLeavesNoChunks()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("still down"));

        await Assert.ThrowsAsync<DocumentIndexingException>(() => _sut.ReindexDocumentAsync("doc-1"));

        // Once for the pre-wipe, once for the compensating cleanup.
        await _repository.Received(2).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenProviderTimesOut_WrapsTheTaskCanceledException()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 2);
        // Same trap as the insert path: a timeout is a provider failure, not a cancellation.
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.ReindexDocumentAsync("doc-1"));

        Assert.Equal("doc-1", ex.DocumentId);
        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenCallerCancels_PropagatesCancellationUnwrapped()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.ReindexDocumentAsync("doc-1", cts.Token));
    }

    [Fact]
    public async Task ReindexDocumentAsync_ClosesAnInterruptedUpdateBySupersedingTheParent()
    {
        var version2 = CreateDocument("doc-2", parentDocumentId: "doc-1");
        var parentStillActive = CreateDocument("doc-1");

        _repository.GetDocumentAsync("doc-2", Arg.Any<CancellationToken>()).Returns(version2);
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(parentStillActive);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-2");

        await _repository.Received(1).SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenParentAlreadySuperseded_LeavesItAlone()
    {
        var version2 = CreateDocument("doc-2", parentDocumentId: "doc-1");
        var parentAlreadyDone = CreateDocument("doc-1", isSuperseded: true);

        _repository.GetDocumentAsync("doc-2", Arg.Any<CancellationToken>()).Returns(version2);
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(parentAlreadyDone);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-2");

        await _repository.DidNotReceive().SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenIndexingFails_DoesNotSupersedeTheParent()
    {
        var version2 = CreateDocument("doc-2", parentDocumentId: "doc-1");

        _repository.GetDocumentAsync("doc-2", Arg.Any<CancellationToken>()).Returns(version2);
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument("doc-1"));
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("still down"));

        await Assert.ThrowsAsync<DocumentIndexingException>(() => _sut.ReindexDocumentAsync("doc-2"));

        await _repository.DidNotReceive().SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }
}
