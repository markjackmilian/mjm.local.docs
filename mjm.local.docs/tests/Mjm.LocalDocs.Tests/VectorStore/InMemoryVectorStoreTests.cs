using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Infrastructure.VectorStore;

namespace Mjm.LocalDocs.Tests.VectorStore;

/// <summary>
/// Unit tests for <see cref="InMemoryVectorStore"/>.
/// </summary>
public sealed class InMemoryVectorStoreTests
{
    private readonly InMemoryVectorStore _sut;

    public InMemoryVectorStoreTests()
    {
        _sut = new InMemoryVectorStore();
    }

    #region Helper Methods

    private static ReadOnlyMemory<float> CreateTestEmbedding(int seed = 42)
    {
        var random = new Random(seed);
        var vector = new float[128];
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)random.NextDouble();
        }
        // Normalize for cosine similarity
        var magnitude = Math.Sqrt(vector.Sum(v => v * v));
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] /= (float)magnitude;
        }
        return vector;
    }

    private static ReadOnlyMemory<float> CreateSimilarEmbedding(ReadOnlyMemory<float> original, double similarity = 0.9)
    {
        var random = new Random(123);
        var originalSpan = original.Span;
        var vector = new float[originalSpan.Length];
        
        // Mix original with random noise based on similarity
        for (int i = 0; i < vector.Length; i++)
        {
            var noise = (float)(random.NextDouble() - 0.5) * 2;
            vector[i] = (float)(originalSpan[i] * similarity + noise * (1 - similarity));
        }
        
        // Normalize
        var magnitude = Math.Sqrt(vector.Sum(v => v * v));
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] /= (float)magnitude;
        }
        return vector;
    }

    #endregion

    #region UpsertAsync Tests

    [Fact]
    public async Task UpsertAsync_SingleVector_CanBeSearched()
    {
        // Arrange
        var embedding = CreateTestEmbedding();

        // Act
        await _sut.UpsertAsync("chunk-1", embedding);

        // Assert
        var results = await _sut.SearchAsync(embedding, 10);
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].ChunkId);
    }

    [Fact]
    public async Task UpsertAsync_SameIdTwice_UpdatesExisting()
    {
        // Arrange
        var embedding1 = CreateTestEmbedding(42);
        var embedding2 = CreateTestEmbedding(99);

        // Act
        await _sut.UpsertAsync("chunk-1", embedding1);
        await _sut.UpsertAsync("chunk-1", embedding2);

        // Assert - search with embedding2 should find it with high similarity
        var results = await _sut.SearchAsync(embedding2, 10);
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].ChunkId);
        Assert.True(results[0].Score > 0.99, "Updated embedding should match search query");
    }

    [Fact]
    public async Task UpsertAsync_MultipleVectors_AllStored()
    {
        // Arrange & Act
        for (int i = 0; i < 10; i++)
        {
            await _sut.UpsertAsync($"chunk-{i}", CreateTestEmbedding(i));
        }

        // Assert
        var results = await _sut.SearchAsync(CreateTestEmbedding(0), 100);
        Assert.Equal(10, results.Count);
    }

    #endregion

    #region UpsertBatchAsync Tests

    [Fact]
    public async Task UpsertBatchAsync_MultipleVectors_AllStored()
    {
        // Arrange
        var embeddings = Enumerable.Range(0, 10)
            .Select(i => new KeyValuePair<string, ReadOnlyMemory<float>>(
                $"chunk-{i}",
                CreateTestEmbedding(i)))
            .ToList();

        // Act
        await _sut.UpsertBatchAsync(embeddings);

        // Assert
        var results = await _sut.SearchAsync(CreateTestEmbedding(0), 100);
        Assert.Equal(10, results.Count);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ExistingVector_Removes()
    {
        // Arrange
        await _sut.UpsertAsync("chunk-1", CreateTestEmbedding());

        // Act
        await _sut.DeleteAsync("chunk-1");

        // Assert
        var results = await _sut.SearchAsync(CreateTestEmbedding(), 10);
        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingVector_NoError()
    {
        // Act & Assert (should not throw)
        await _sut.DeleteAsync("non-existing");
    }

    #endregion

    #region DeleteByDocumentIdAsync Tests

    [Fact]
    public async Task DeleteByDocumentIdAsync_RemovesAllChunksForDocument()
    {
        // Arrange
        await _sut.UpsertAsync("doc1_chunk_0", CreateTestEmbedding(0));
        await _sut.UpsertAsync("doc1_chunk_1", CreateTestEmbedding(1));
        await _sut.UpsertAsync("doc1_chunk_2", CreateTestEmbedding(2));
        await _sut.UpsertAsync("doc2_chunk_0", CreateTestEmbedding(100));

        // Act
        await _sut.DeleteByDocumentIdAsync("doc1");

        // Assert
        var results = await _sut.SearchAsync(CreateTestEmbedding(100), 10);
        Assert.Single(results);
        Assert.Equal("doc2_chunk_0", results[0].ChunkId);
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_EmptyStore_ReturnsEmptyList()
    {
        // Act
        var results = await _sut.SearchAsync(CreateTestEmbedding(), 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_SingleVector_ReturnsIt()
    {
        // Arrange
        var embedding = CreateTestEmbedding();
        await _sut.UpsertAsync("chunk-1", embedding);

        // Act
        var results = await _sut.SearchAsync(embedding, 10);

        // Assert
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].ChunkId);
        Assert.True(results[0].Score > 0.99, "Self-similarity should be ~1.0");
    }

    [Fact]
    public async Task SearchAsync_ReturnsResultsOrderedByScore()
    {
        // Arrange
        var queryEmbedding = CreateTestEmbedding(42);
        var similarEmbedding = CreateSimilarEmbedding(queryEmbedding, 0.95);
        var lessSimilarEmbedding = CreateSimilarEmbedding(queryEmbedding, 0.7);
        var differentEmbedding = CreateTestEmbedding(999);

        await _sut.UpsertAsync("similar", similarEmbedding);
        await _sut.UpsertAsync("less-similar", lessSimilarEmbedding);
        await _sut.UpsertAsync("different", differentEmbedding);

        // Act
        var results = await _sut.SearchAsync(queryEmbedding, 10);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("similar", results[0].ChunkId);
        Assert.True(results[0].Score > results[1].Score, "Results should be ordered by score descending");
        Assert.True(results[1].Score > results[2].Score, "Results should be ordered by score descending");
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            await _sut.UpsertAsync($"chunk-{i}", CreateTestEmbedding(i));
        }

        // Act
        var results = await _sut.SearchAsync(CreateTestEmbedding(0), 5);

        // Assert
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task SearchAsync_ScoreIsBetweenZeroAndOne()
    {
        // Arrange
        await _sut.UpsertAsync("chunk-1", CreateTestEmbedding(1));
        await _sut.UpsertAsync("chunk-2", CreateTestEmbedding(2));

        // Act
        var results = await _sut.SearchAsync(CreateTestEmbedding(1), 10);

        // Assert
        foreach (var result in results)
        {
            Assert.InRange(result.Score, 0.0, 1.0);
        }
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentUpserts_AllSucceed()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 100)
            .Select(i => _sut.UpsertAsync($"chunk-{i}", CreateTestEmbedding(i)));

        // Act
        await Task.WhenAll(tasks);

        // Assert
        var results = await _sut.SearchAsync(CreateTestEmbedding(0), 200);
        Assert.Equal(100, results.Count);
    }

    [Fact]
    public async Task ConcurrentSearches_AllSucceed()
    {
        // Arrange
        for (int i = 0; i < 50; i++)
        {
            await _sut.UpsertAsync($"chunk-{i}", CreateTestEmbedding(i));
        }

        var tasks = Enumerable.Range(0, 100)
            .Select(i => _sut.SearchAsync(CreateTestEmbedding(i % 50), 10));

        // Act
        var allResults = await Task.WhenAll(tasks);

        // Assert
        Assert.All(allResults, results => Assert.NotEmpty(results));
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_WithNoEmbeddings_ReturnsZero()
    {
        var count = await _sut.CountAsync();

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task CountAsync_AfterUpserts_ReturnsNumberOfEmbeddings()
    {
        await _sut.UpsertAsync("doc-1_chunk_0", new float[] { 0.1f, 0.2f });
        await _sut.UpsertAsync("doc-1_chunk_1", new float[] { 0.3f, 0.4f });

        var count = await _sut.CountAsync();

        Assert.Equal(2L, count);
    }

    #endregion

    #region GetExistingChunkIdsAsync Tests

    [Fact]
    public async Task GetExistingChunkIdsAsync_ReturnsOnlyStoredIds()
    {
        await _sut.UpsertAsync("doc-1_chunk_0", new float[] { 0.1f, 0.2f });

        var existing = await _sut.GetExistingChunkIdsAsync(
            ["doc-1_chunk_0", "doc-1_chunk_1", "doc-2_chunk_0"]);

        Assert.Equal(["doc-1_chunk_0"], existing);
    }

    [Fact]
    public async Task GetExistingChunkIdsAsync_WithEmptyInput_ReturnsEmpty()
    {
        await _sut.UpsertAsync("doc-1_chunk_0", new float[] { 0.1f, 0.2f });

        var existing = await _sut.GetExistingChunkIdsAsync([]);

        Assert.Empty(existing);
    }

    #endregion
}
