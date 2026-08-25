using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Models.Dashboard;
using Mjm.LocalDocs.Core.Services;
using NSubstitute;

namespace Mjm.LocalDocs.Tests.Services;

/// <summary>
/// Unit tests for <see cref="DocumentService"/>.
/// </summary>
public sealed class DocumentServiceTests
{
    private readonly IDocumentRepository _repository;
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentProcessor _processor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentLockRegistry _locks;
    private readonly IAsyncDisposable _lockHandle;
    private readonly DocumentService _sut;

    public DocumentServiceTests()
    {
        _repository = Substitute.For<IDocumentRepository>();
        _vectorStore = Substitute.For<IVectorStore>();
        _processor = Substitute.For<IDocumentProcessor>();
        _embeddingService = Substitute.For<IEmbeddingService>();
        _locks = Substitute.For<IDocumentLockRegistry>();
        _lockHandle = Substitute.For<IAsyncDisposable>();

        _locks.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_lockHandle);

        _sut = new DocumentService(
            _repository,
            _vectorStore,
            _processor,
            _embeddingService,
            _locks);
    }

    #region Helper Methods

    private static Document CreateTestDocument(
        string id = "doc-1",
        string projectId = "proj-1",
        int versionNumber = 1,
        string? parentDocumentId = null,
        bool isSuperseded = false)
    {
        return new Document
        {
            Id = id,
            ProjectId = projectId,
            FileName = "test-document.txt",
            FileExtension = ".txt",
            FileContent = "Test content"u8.ToArray(),
            FileSizeBytes = 12,
            ExtractedText = "Test content for chunking",
            VersionNumber = versionNumber,
            ParentDocumentId = parentDocumentId,
            IsSuperseded = isSuperseded
        };
    }

    private static DocumentChunk CreateTestChunk(string id, string documentId, int chunkIndex = 0)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = documentId,
            Content = $"Chunk content {chunkIndex}",
            ChunkIndex = chunkIndex,
            FileName = "test-document.txt"
        };
    }

    private static ReadOnlyMemory<float> CreateTestEmbedding()
    {
        return new float[] { 0.1f, 0.2f, 0.3f };
    }

    #endregion

    #region AddDocumentAsync Tests

    [Fact]
    public async Task AddDocumentAsync_WithChunks_StoresDocumentChunksAndEmbeddings()
    {
        // Arrange
        var document = CreateTestDocument();
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("doc-1_chunk_0", "doc-1", 0),
            CreateTestChunk("doc-1_chunk_1", "doc-1", 1)
        };
        var embeddings = new List<ReadOnlyMemory<float>>
        {
            CreateTestEmbedding(),
            CreateTestEmbedding()
        };

        _repository.AddDocumentAsync(document, Arg.Any<CancellationToken>())
            .Returns(document);
        _processor.ChunkDocumentAsync(document, Arg.Any<CancellationToken>())
            .Returns(chunks);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(embeddings);

        // Act
        var result = await _sut.AddDocumentAsync(document);

        // Assert
        Assert.Equal(document.Id, result.Id);

        await _repository.Received(1).AddDocumentAsync(document, Arg.Any<CancellationToken>());
        await _processor.Received(1).ChunkDocumentAsync(document, Arg.Any<CancellationToken>());
        await _repository.Received(1).AddChunksAsync(chunks, Arg.Any<CancellationToken>());
        await _embeddingService.Received(1).GenerateEmbeddingsAsync(
            Arg.Is<IEnumerable<string>>(texts => texts.Count() == 2),
            Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).UpsertBatchAsync(
            Arg.Is<IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>>>(e => e.Count() == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddDocumentAsync_WithNoChunks_StoresOnlyDocument()
    {
        // Arrange
        var document = CreateTestDocument();
        var emptyChunks = new List<DocumentChunk>();

        _repository.AddDocumentAsync(document, Arg.Any<CancellationToken>())
            .Returns(document);
        _processor.ChunkDocumentAsync(document, Arg.Any<CancellationToken>())
            .Returns(emptyChunks);

        // Act
        var result = await _sut.AddDocumentAsync(document);

        // Assert
        Assert.Equal(document.Id, result.Id);

        await _repository.Received(1).AddDocumentAsync(document, Arg.Any<CancellationToken>());
        await _processor.Received(1).ChunkDocumentAsync(document, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().AddChunksAsync(Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>());
        await _embeddingService.DidNotReceive().GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        await _vectorStore.DidNotReceive().UpsertBatchAsync(Arg.Any<IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>>>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_WithResults_ReturnsOrderedSearchResults()
    {
        // Arrange
        var query = "test query";
        var queryEmbedding = CreateTestEmbedding();
        var vectorResults = new List<VectorSearchResult>
        {
            new() { ChunkId = "chunk-1", Score = 0.95 },
            new() { ChunkId = "chunk-2", Score = 0.85 }
        };
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("chunk-1", "doc-1", 0),
            CreateTestChunk("chunk-2", "doc-1", 1)
        };

        _embeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>())
            .Returns(queryEmbedding);
        _vectorStore.SearchAsync(queryEmbedding, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(vectorResults);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(chunks);
        // Both chunks are owned by an active document, so the new join admits them. Without this
        // stub every chunk is now an "unknown owner" and gets dropped by design.
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkDocumentContext("chunk-1", "doc-1", "proj-1", false),
                new ChunkDocumentContext("chunk-2", "doc-1", "proj-1", false)
            ]);

        // Act
        var results = await _sut.SearchAsync(query, limit: 10);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("chunk-1", results[0].Chunk.Id);
        Assert.Equal(0.95, results[0].Score);
        Assert.Equal("chunk-2", results[1].Chunk.Id);
        Assert.Equal(0.85, results[1].Score);
    }

    [Fact]
    public async Task SearchAsync_WithNoVectorResults_ReturnsEmptyList()
    {
        // Arrange
        var query = "test query";
        var queryEmbedding = CreateTestEmbedding();
        var emptyVectorResults = new List<VectorSearchResult>();

        _embeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>())
            .Returns(queryEmbedding);
        _vectorStore.SearchAsync(queryEmbedding, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(emptyVectorResults);

        // Act
        var results = await _sut.SearchAsync(query);

        // Assert
        Assert.Empty(results);
        await _repository.DidNotReceive().GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WithProjectFilter_FiltersResultsByProject()
    {
        // Arrange
        var query = "test query";
        var projectId = "proj-1";
        var queryEmbedding = CreateTestEmbedding();
        var vectorResults = new List<VectorSearchResult>
        {
            new() { ChunkId = "chunk-1", Score = 0.95 },
            new() { ChunkId = "chunk-2", Score = 0.85 },
            new() { ChunkId = "chunk-3", Score = 0.75 }
        };
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("chunk-1", "doc-1", 0),
            CreateTestChunk("chunk-2", "doc-2", 0),
            CreateTestChunk("chunk-3", "doc-1", 1)
        };
        // doc-1's chunks are in the requested project; doc-2's are in a different one. Expressed
        // against the new collaborator (GetChunkDocumentContextAsync) rather than the old
        // GetDocumentsByProjectAsync — same behaviour under test, only the collaborator moved.
        var chunkContext = new List<ChunkDocumentContext>
        {
            new("chunk-1", "doc-1", projectId, false),
            new("chunk-2", "doc-2", "proj-2", false),
            new("chunk-3", "doc-1", projectId, false)
        };

        _embeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>())
            .Returns(queryEmbedding);
        _vectorStore.SearchAsync(queryEmbedding, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(vectorResults);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(chunks);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(chunkContext);

        // Act
        var results = await _sut.SearchAsync(query, projectId: projectId, limit: 10);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("doc-1", r.Chunk.DocumentId));
    }

    #endregion

    #region DeleteDocumentAsync Tests

    [Fact]
    public async Task DeleteDocumentAsync_WhenDocumentExists_DeletesEmbeddingsAndDocument()
    {
        // Arrange
        var documentId = "doc-1";
        _repository.DeleteDocumentAsync(documentId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _sut.DeleteDocumentAsync(documentId);

        // Assert
        Assert.True(result);
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(documentId, Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteDocumentAsync(documentId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteDocumentAsync_WhenDocumentNotFound_ReturnsFalse()
    {
        // Arrange
        var documentId = "non-existent-doc";
        _repository.DeleteDocumentAsync(documentId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.DeleteDocumentAsync(documentId);

        // Assert
        Assert.False(result);
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(documentId, Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteDocumentAsync(documentId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetDocumentAsync Tests

    [Fact]
    public async Task GetDocumentAsync_WhenExists_ReturnsDocument()
    {
        // Arrange
        var documentId = "doc-1";
        var document = CreateTestDocument(documentId);
        _repository.GetDocumentAsync(documentId, Arg.Any<CancellationToken>())
            .Returns(document);

        // Act
        var result = await _sut.GetDocumentAsync(documentId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(documentId, result.Id);
    }

    [Fact]
    public async Task GetDocumentAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        var documentId = "non-existent-doc";
        _repository.GetDocumentAsync(documentId, Arg.Any<CancellationToken>())
            .Returns((Document?)null);

        // Act
        var result = await _sut.GetDocumentAsync(documentId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetDocumentFileAsync Tests

    [Fact]
    public async Task GetDocumentFileAsync_WhenDocumentHasInlineContent_ReturnsFileContent()
    {
        // Arrange
        var documentId = "doc-1";
        var fileContent = "Test file content"u8.ToArray();
        var document = new Document
        {
            Id = documentId,
            ProjectId = "proj-1",
            FileName = "test.txt",
            FileExtension = ".txt",
            FileContent = fileContent, // Inline content (legacy/database storage)
            FileSizeBytes = fileContent.Length,
            ExtractedText = "Test"
        };
        _repository.GetDocumentAsync(documentId, Arg.Any<CancellationToken>())
            .Returns(document);

        // Act
        var result = await _sut.GetDocumentFileAsync(documentId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(fileContent, result);
    }

    [Fact]
    public async Task GetDocumentFileAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        var documentId = "non-existent-doc";
        _repository.GetDocumentAsync(documentId, Arg.Any<CancellationToken>())
            .Returns((Document?)null);

        // Act
        var result = await _sut.GetDocumentFileAsync(documentId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDocumentFileAsync_WhenNoInlineContent_FallsBackToRepository()
    {
        // Arrange
        var documentId = "doc-1";
        var fileContent = "Test file content"u8.ToArray();
        var document = new Document
        {
            Id = documentId,
            ProjectId = "proj-1",
            FileName = "test.txt",
            FileExtension = ".txt",
            FileContent = null, // No inline content
            FileStorageLocation = null, // No external storage location
            FileSizeBytes = fileContent.Length,
            ExtractedText = "Test"
        };
        _repository.GetDocumentAsync(documentId, Arg.Any<CancellationToken>())
            .Returns(document);
        _repository.GetDocumentFileAsync(documentId, Arg.Any<CancellationToken>())
            .Returns(fileContent);

        // Act
        var result = await _sut.GetDocumentFileAsync(documentId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(fileContent, result);
    }

    #endregion

    #region GetDocumentsByProjectAsync Tests

    [Fact]
    public async Task GetDocumentsByProjectAsync_ReturnsDocumentsFromRepository()
    {
        // Arrange
        var projectId = "proj-1";
        var documents = new List<Document>
        {
            CreateTestDocument("doc-1", projectId),
            CreateTestDocument("doc-2", projectId)
        };
        _repository.GetDocumentsByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(documents);

        // Act
        var result = await _sut.GetDocumentsByProjectAsync(projectId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Equal(projectId, d.ProjectId));
    }

    #endregion

    #region GetProjectsWithDocumentsAsync Tests

    [Fact]
    public async Task GetProjectsWithDocumentsAsync_ReturnsProjectsFromRepository()
    {
        // Arrange
        var projects = new List<string> { "proj-1", "proj-2", "proj-3" };
        _repository.GetProjectsWithDocumentsAsync(Arg.Any<CancellationToken>())
            .Returns(projects);

        // Act
        var result = await _sut.GetProjectsWithDocumentsAsync();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("proj-1", result);
        Assert.Contains("proj-2", result);
        Assert.Contains("proj-3", result);
    }

    #endregion

    #region UpdateDocumentAsync Tests

    [Fact]
    public async Task UpdateDocumentAsync_WithValidDocument_CreatesNewVersionAndSupersedesOld()
    {
        // Arrange
        var existingDoc = CreateTestDocument("doc-1", "proj-1");
        var newVersionDoc = CreateTestDocument("doc-2", "proj-1", versionNumber: 2, parentDocumentId: "doc-1");
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("doc-2_chunk_0", "doc-2", 0)
        };
        var embeddings = new List<ReadOnlyMemory<float>> { CreateTestEmbedding() };

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .Returns(existingDoc);
        _repository.AddDocumentAsync(newVersionDoc, Arg.Any<CancellationToken>())
            .Returns(newVersionDoc);
        _processor.ChunkDocumentAsync(newVersionDoc, Arg.Any<CancellationToken>())
            .Returns(chunks);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(embeddings);

        // Act
        var result = await _sut.UpdateDocumentAsync("doc-1", newVersionDoc);

        // Assert
        Assert.Equal("doc-2", result.Id);

        // Verify new version was added
        await _repository.Received(1).AddDocumentAsync(newVersionDoc, Arg.Any<CancellationToken>());

        // Verify old document was superseded
        await _repository.Received(1).SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());

        // Verify old chunks and embeddings were removed
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateDocumentAsync_WhenDocumentNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var newVersionDoc = CreateTestDocument("doc-2", "proj-1");
        _repository.GetDocumentAsync("non-existent", Arg.Any<CancellationToken>())
            .Returns((Document?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateDocumentAsync("non-existent", newVersionDoc));

        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task UpdateDocumentAsync_WhenDocumentAlreadySuperseded_ThrowsInvalidOperationException()
    {
        // Arrange
        var supersededDoc = CreateTestDocument("doc-1", "proj-1", isSuperseded: true);
        var newVersionDoc = CreateTestDocument("doc-2", "proj-1");

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .Returns(supersededDoc);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateDocumentAsync("doc-1", newVersionDoc));

        Assert.Contains("already superseded", exception.Message);
    }

    [Fact]
    public async Task UpdateDocumentAsync_CreatesNewVersion_BeforeSupersedingOld()
    {
        // Arrange - verify ordering: add new first, then supersede old
        var existingDoc = CreateTestDocument("doc-1", "proj-1");
        var newVersionDoc = CreateTestDocument("doc-2", "proj-1", versionNumber: 2, parentDocumentId: "doc-1");
        var emptyChunks = new List<DocumentChunk>();

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .Returns(existingDoc);
        _repository.AddDocumentAsync(newVersionDoc, Arg.Any<CancellationToken>())
            .Returns(newVersionDoc);
        _processor.ChunkDocumentAsync(newVersionDoc, Arg.Any<CancellationToken>())
            .Returns(emptyChunks);

        // Act
        await _sut.UpdateDocumentAsync("doc-1", newVersionDoc);

        // Assert - AddDocumentAsync should be called before SupersedeDocumentAsync
        Received.InOrder(() =>
        {
            _repository.AddDocumentAsync(newVersionDoc, Arg.Any<CancellationToken>());
            _repository.SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        });
    }

    #endregion

    #region SearchAsync Superseded Filter Tests

    [Fact]
    public async Task SearchAsync_ExcludesSupersededDocuments()
    {
        // Arrange
        var query = "test query";
        var queryEmbedding = CreateTestEmbedding();
        var vectorResults = new List<VectorSearchResult>
        {
            new() { ChunkId = "chunk-1", Score = 0.95 },
            new() { ChunkId = "chunk-2", Score = 0.85 }
        };
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("chunk-1", "doc-1", 0),
            CreateTestChunk("chunk-2", "doc-2", 0)
        };
        // Expressed against the new collaborator (GetChunkDocumentContextAsync) rather than one
        // GetDocumentAsync call per distinct result document — same behaviour under test, only the
        // collaborator moved.
        var chunkContext = new List<ChunkDocumentContext>
        {
            new("chunk-1", "doc-1", "proj-1", false),
            new("chunk-2", "doc-2", "proj-1", true)
        };

        _embeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>())
            .Returns(queryEmbedding);
        _vectorStore.SearchAsync(queryEmbedding, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(vectorResults);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(chunks);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(chunkContext);

        // Act
        var results = await _sut.SearchAsync(query, limit: 10);

        // Assert - only the active document's chunk should be returned
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].Chunk.Id);
        Assert.Equal(0.95, results[0].Score);
    }

    #endregion

    #region GetDocumentsByProjectAsync Filter Tests

    [Fact]
    public async Task GetDocumentsByProjectAsync_WithIncludeSupersededFalse_FiltersOutSupersededDocuments()
    {
        // Arrange
        var projectId = "proj-1";
        var documents = new List<Document>
        {
            CreateTestDocument("doc-1", projectId),
            CreateTestDocument("doc-2", projectId, isSuperseded: true),
            CreateTestDocument("doc-3", projectId)
        };
        _repository.GetDocumentsByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(documents);

        // Act
        var result = await _sut.GetDocumentsByProjectAsync(projectId, includeSuperseded: false);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.False(d.IsSuperseded));
    }

    [Fact]
    public async Task GetDocumentsByProjectAsync_WithIncludeSupersededTrue_ReturnsAllDocuments()
    {
        // Arrange
        var projectId = "proj-1";
        var documents = new List<Document>
        {
            CreateTestDocument("doc-1", projectId),
            CreateTestDocument("doc-2", projectId, isSuperseded: true),
            CreateTestDocument("doc-3", projectId)
        };
        _repository.GetDocumentsByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(documents);

        // Act
        var result = await _sut.GetDocumentsByProjectAsync(projectId, includeSuperseded: true);

        // Assert
        Assert.Equal(3, result.Count);
    }

    #endregion

    #region SearchAsync ChunkDocumentContext Tests

    [Fact]
    public async Task SearchAsync_WithProjectFilter_DoesNotEnumerateTheProjectsDocuments()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new VectorSearchResult { ChunkId = "doc-1_chunk_0", Score = 0.9 }]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([CreateTestChunk("doc-1_chunk_0", "doc-1")]);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChunkDocumentContext("doc-1_chunk_0", "doc-1", "proj-a", false)]);

        var results = await _sut.SearchAsync("query", projectId: "proj-a");

        Assert.Single(results);
        // The old filter materialised every document in the project, FileContent included, to
        // build a set of ids.
        await _repository.DidNotReceive().GetDocumentsByProjectAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ExcludesChunksFromAnotherProject()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new VectorSearchResult { ChunkId = "doc-1_chunk_0", Score = 0.9 },
                new VectorSearchResult { ChunkId = "doc-2_chunk_0", Score = 0.8 }
            ]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                CreateTestChunk("doc-1_chunk_0", "doc-1"),
                CreateTestChunk("doc-2_chunk_0", "doc-2")
            ]);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkDocumentContext("doc-1_chunk_0", "doc-1", "proj-a", false),
                new ChunkDocumentContext("doc-2_chunk_0", "doc-2", "proj-b", false)
            ]);

        var results = await _sut.SearchAsync("query", projectId: "proj-a");

        Assert.Equal("doc-1_chunk_0", Assert.Single(results).Chunk.Id);
    }

    [Fact]
    public async Task SearchAsync_ExcludesSupersededOwnersWithoutQueryingPerDocument()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new VectorSearchResult { ChunkId = "doc-1_chunk_0", Score = 0.9 },
                new VectorSearchResult { ChunkId = "doc-2_chunk_0", Score = 0.8 }
            ]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                CreateTestChunk("doc-1_chunk_0", "doc-1"),
                CreateTestChunk("doc-2_chunk_0", "doc-2")
            ]);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkDocumentContext("doc-1_chunk_0", "doc-1", "proj-a", false),
                new ChunkDocumentContext("doc-2_chunk_0", "doc-2", "proj-a", true)
            ]);

        var results = await _sut.SearchAsync("query");

        Assert.Equal("doc-1_chunk_0", Assert.Single(results).Chunk.Id);
        // The old safety net loaded each distinct result document one at a time.
        await _repository.DidNotReceive().GetDocumentAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_DropsAChunkWhoseOwnerIsUnknown()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new VectorSearchResult { ChunkId = "orphan_chunk_0", Score = 0.9 }]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([CreateTestChunk("orphan_chunk_0", "doc-gone")]);
        // An orphaned embedding whose chunk row survived but whose document did not.
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var results = await _sut.SearchAsync("query");

        Assert.Empty(results);
    }

    #endregion
}
