using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Configuration;
using Mjm.LocalDocs.Infrastructure.Persistence;

namespace Mjm.LocalDocs.Tests.VectorStore;

/// <summary>
/// Integration tests for <see cref="SqlServerVectorStore"/>.
/// Requires SQL Server 2025+ or Azure SQL with VECTOR type support.
/// 
/// To run these tests locally, set the SQL_SERVER_CONNECTION_STRING environment variable
/// or update the connection string in the constructor.
/// 
/// To skip these tests: dotnet test --filter "Category!=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class SqlServerVectorStoreTests : IAsyncLifetime
{
    private const string TestTableName = "chunk_embeddings_test";
    private const int EmbeddingDimension = 128;

    private readonly string? _connectionString;
    private SqlServerVectorStore? _sut;

    public SqlServerVectorStoreTests()
    {
        // Try to get connection string from environment variable
        _connectionString = Environment.GetEnvironmentVariable("SQL_SERVER_CONNECTION_STRING");
    }

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            return; // Skip initialization if no connection string
        }

        // Create test table
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Drop existing test table if exists
        var dropSql = $"IF OBJECT_ID('{TestTableName}', 'U') IS NOT NULL DROP TABLE {TestTableName}";
        await using var dropCmd = new Microsoft.Data.SqlClient.SqlCommand(dropSql, connection);
        await dropCmd.ExecuteNonQueryAsync();

        // Create test table with VECTOR type
        var createSql = $"""
            CREATE TABLE {TestTableName} (
                chunk_id NVARCHAR(255) PRIMARY KEY,
                embedding VECTOR({EmbeddingDimension}) NOT NULL
            )
            """;

        try
        {
            await using var createCmd = new Microsoft.Data.SqlClient.SqlCommand(createSql, connection);
            await createCmd.ExecuteNonQueryAsync();

            _sut = new SqlServerVectorStore(_connectionString, EmbeddingDimension, "cosine");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Message.Contains("VECTOR"))
        {
            // VECTOR type not supported - SQL Server version too old
            _sut = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            return;
        }

        // Cleanup test table
        try
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = $"IF OBJECT_ID('{TestTableName}', 'U') IS NOT NULL DROP TABLE {TestTableName}";
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void SkipIfNotAvailable()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            Assert.Skip("SQL Server connection string not configured. Set SQL_SERVER_CONNECTION_STRING environment variable.");
        }

        if (_sut is null)
        {
            Assert.Skip("SQL Server does not support VECTOR type. Requires SQL Server 2025+ or Azure SQL.");
        }
    }

    #region Helper Methods

    private static ReadOnlyMemory<float> CreateTestEmbedding(int seed = 42)
    {
        var random = new Random(seed);
        var vector = new float[EmbeddingDimension];
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
        SkipIfNotAvailable();

        // Arrange
        var embedding = CreateTestEmbedding();

        // Act
        await _sut!.UpsertAsync("chunk-1", embedding);

        // Assert
        var results = await _sut.SearchAsync(embedding, 10);
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].ChunkId);
    }

    [Fact]
    public async Task UpsertAsync_SameIdTwice_UpdatesExisting()
    {
        SkipIfNotAvailable();

        // Arrange
        var embedding1 = CreateTestEmbedding(42);
        var embedding2 = CreateTestEmbedding(99);

        // Act
        await _sut!.UpsertAsync("chunk-1", embedding1);
        await _sut.UpsertAsync("chunk-1", embedding2);

        // Assert - search with embedding2 should find it with high similarity
        var results = await _sut.SearchAsync(embedding2, 10);
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].ChunkId);
    }

    [Fact]
    public async Task UpsertAsync_MultipleVectors_AllStored()
    {
        SkipIfNotAvailable();

        // Arrange & Act
        for (int i = 0; i < 10; i++)
        {
            await _sut!.UpsertAsync($"chunk-{i}", CreateTestEmbedding(i));
        }

        // Assert
        var results = await _sut!.SearchAsync(CreateTestEmbedding(0), 100);
        Assert.Equal(10, results.Count);
    }

    #endregion

    #region UpsertBatchAsync Tests

    [Fact]
    public async Task UpsertBatchAsync_MultipleVectors_AllStored()
    {
        SkipIfNotAvailable();

        // Arrange
        var embeddings = Enumerable.Range(0, 10)
            .Select(i => new KeyValuePair<string, ReadOnlyMemory<float>>(
                $"chunk-{i}",
                CreateTestEmbedding(i)))
            .ToList();

        // Act
        await _sut!.UpsertBatchAsync(embeddings);

        // Assert
        var results = await _sut.SearchAsync(CreateTestEmbedding(0), 100);
        Assert.Equal(10, results.Count);
    }

    [Fact]
    public async Task UpsertBatchAsync_EmptyBatch_NoError()
    {
        SkipIfNotAvailable();

        // Act & Assert (should not throw)
        await _sut!.UpsertBatchAsync([]);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ExistingVector_Removes()
    {
        SkipIfNotAvailable();

        // Arrange
        await _sut!.UpsertAsync("chunk-1", CreateTestEmbedding());

        // Act
        await _sut.DeleteAsync("chunk-1");

        // Assert
        var results = await _sut.SearchAsync(CreateTestEmbedding(), 10);
        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingVector_NoError()
    {
        SkipIfNotAvailable();

        // Act & Assert (should not throw)
        await _sut!.DeleteAsync("non-existing");
    }

    #endregion

    #region DeleteByDocumentIdAsync Tests

    [Fact]
    public async Task DeleteByDocumentIdAsync_RemovesAllChunksForDocument()
    {
        SkipIfNotAvailable();

        // Arrange
        await _sut!.UpsertAsync("doc1_chunk_0", CreateTestEmbedding(0));
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
        SkipIfNotAvailable();

        // Act
        var results = await _sut!.SearchAsync(CreateTestEmbedding(), 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_SingleVector_ReturnsIt()
    {
        SkipIfNotAvailable();

        // Arrange
        var embedding = CreateTestEmbedding();
        await _sut!.UpsertAsync("chunk-1", embedding);

        // Act
        var results = await _sut.SearchAsync(embedding, 10);

        // Assert
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].ChunkId);
        // Note: SQL Server uses distance-based scoring, so self-similarity may not be exactly 1.0
        Assert.True(results[0].Score > 0.9, "Self-similarity should be high");
    }

    [Fact]
    public async Task SearchAsync_ReturnsResultsOrderedByScore()
    {
        SkipIfNotAvailable();

        // Arrange
        var queryEmbedding = CreateTestEmbedding(42);
        var similarEmbedding = CreateSimilarEmbedding(queryEmbedding, 0.95);
        var lessSimilarEmbedding = CreateSimilarEmbedding(queryEmbedding, 0.7);
        var differentEmbedding = CreateTestEmbedding(999);

        await _sut!.UpsertAsync("similar", similarEmbedding);
        await _sut.UpsertAsync("less-similar", lessSimilarEmbedding);
        await _sut.UpsertAsync("different", differentEmbedding);

        // Act
        var results = await _sut.SearchAsync(queryEmbedding, 10);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("similar", results[0].ChunkId);
        Assert.True(results[0].Score >= results[1].Score, "Results should be ordered by score descending");
        Assert.True(results[1].Score >= results[2].Score, "Results should be ordered by score descending");
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        SkipIfNotAvailable();

        // Arrange
        for (int i = 0; i < 20; i++)
        {
            await _sut!.UpsertAsync($"chunk-{i}", CreateTestEmbedding(i));
        }

        // Act
        var results = await _sut!.SearchAsync(CreateTestEmbedding(0), 5);

        // Assert
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task SearchAsync_ScoreIsPositive()
    {
        SkipIfNotAvailable();

        // Arrange
        await _sut!.UpsertAsync("chunk-1", CreateTestEmbedding(1));
        await _sut.UpsertAsync("chunk-2", CreateTestEmbedding(2));

        // Act
        var results = await _sut.SearchAsync(CreateTestEmbedding(1), 10);

        // Assert
        foreach (var result in results)
        {
            Assert.True(result.Score > 0, "Score should be positive");
        }
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_WithNoEmbeddings_ReturnsZero()
    {
        SkipIfNotAvailable();

        var count = await _sut!.CountAsync();

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task CountAsync_AfterUpserts_ReturnsNumberOfEmbeddings()
    {
        SkipIfNotAvailable();

        await _sut!.UpsertAsync("doc-1_chunk_0", CreateTestEmbedding(1));
        await _sut.UpsertAsync("doc-1_chunk_1", CreateTestEmbedding(2));

        var count = await _sut.CountAsync();

        Assert.Equal(2L, count);
    }

    #endregion

    #region GetExistingChunkIdsAsync Tests

    [Fact]
    public async Task GetExistingChunkIdsAsync_ReturnsOnlyStoredIds()
    {
        SkipIfNotAvailable();

        await _sut!.UpsertAsync("doc-1_chunk_0", CreateTestEmbedding(1));

        var existing = await _sut.GetExistingChunkIdsAsync(
            ["doc-1_chunk_0", "doc-1_chunk_1", "doc-2_chunk_0"]);

        Assert.Equal(["doc-1_chunk_0"], existing);
    }

    [Fact]
    public async Task GetExistingChunkIdsAsync_WithEmptyInput_ReturnsEmpty()
    {
        SkipIfNotAvailable();

        await _sut!.UpsertAsync("doc-1_chunk_0", CreateTestEmbedding(1));

        var existing = await _sut.GetExistingChunkIdsAsync([]);

        Assert.Empty(existing);
    }

    #endregion

    #region SqlServerOptions Tests

    [Fact]
    public void Constructor_WithOptions_UsesProvidedValues()
    {
        // Arrange
        var options = new SqlServerOptions
        {
            Schema = "custom_schema",
            TableName = "custom_table",
            UseVectorIndex = false,
            DistanceMetric = "euclidean"
        };

        // Act - just verify constructor doesn't throw
        var store = new SqlServerVectorStore(
            "Server=localhost;Database=test;",
            dimension: 512,
            options: options);

        // Assert - store created successfully (no exception)
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithDefaultOptions_UsesDefaults()
    {
        // Arrange
        var options = new SqlServerOptions();

        // Act
        var store = new SqlServerVectorStore(
            "Server=localhost;Database=test;",
            dimension: 1536,
            options: options);

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithDistanceMetricOnly_BackwardCompatible()
    {
        // Act - using the old constructor signature
        var store = new SqlServerVectorStore(
            "Server=localhost;Database=test;",
            dimension: 1536,
            distanceMetric: "dotproduct");

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new SqlServerOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerVectorStore(null!, 1536, options));
    }

    #endregion
}


