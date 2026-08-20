using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Configuration;

namespace Mjm.LocalDocs.Infrastructure.Persistence;

/// <summary>
/// SQL Server/Azure SQL vector store implementation using raw SQL.
/// Stores embeddings in a separate chunk_embeddings table with native VECTOR type.
/// Uses string-formatted vectors for inserts (e.g., '[0.1, 0.2, 0.3]').
/// </summary>
public sealed class SqlServerVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly int _dimension;
    private readonly string _schema;
    private readonly string _tableName;
    private readonly bool _useVectorIndex;
    private readonly string _distanceMetric;

    /// <summary>
    /// Gets the fully qualified table name with schema.
    /// </summary>
    private string FullTableName => $"[{_schema}].[{_tableName}]";

    /// <summary>
    /// Gets the vector index name.
    /// </summary>
    private string IndexName => $"vec_idx_{_tableName}";

    /// <summary>
    /// Initializes a new instance of <see cref="SqlServerVectorStore"/> with explicit parameters.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="dimension">Dimension of embedding vectors (default 1536).</param>
    /// <param name="distanceMetric">Distance metric: cosine, euclidean, dotproduct (default cosine).</param>
    public SqlServerVectorStore(
        string connectionString,
        int dimension = 1536,
        string distanceMetric = "cosine")
        : this(connectionString, dimension, new SqlServerOptions { DistanceMetric = distanceMetric })
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SqlServerVectorStore"/> with options.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="dimension">Dimension of embedding vectors.</param>
    /// <param name="options">SQL Server-specific options.</param>
    public SqlServerVectorStore(
        string connectionString,
        int dimension,
        SqlServerOptions options)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _dimension = dimension;
        _schema = options.Schema;
        _tableName = options.TableName;
        _useVectorIndex = options.UseVectorIndex;
        _distanceMetric = options.DistanceMetric;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Create schema if not exists (only if not dbo)
        if (!string.Equals(_schema, "dbo", StringComparison.OrdinalIgnoreCase))
        {
            var createSchemaSql = $"""
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{_schema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{_schema}]');
                END
                """;

            await using var createSchemaCmd = new SqlCommand(createSchemaSql, connection);
            await createSchemaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create table if not exists
        var createTableSql = $"""
            IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = '{_tableName}' AND s.name = '{_schema}')
            BEGIN
                CREATE TABLE {FullTableName} (
                    chunk_id NVARCHAR(255) PRIMARY KEY,
                    embedding VECTOR({_dimension}) NOT NULL
                );
            END
            """;

        await using var createTableCmd = new SqlCommand(createTableSql, connection);
        await createTableCmd.ExecuteNonQueryAsync(cancellationToken);

        // Create vector index if not exists (for approximate nearest neighbor search)
        if (_useVectorIndex)
        {
            var createIndexSql = $"""
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = '{IndexName}')
                BEGIN
                    CREATE VECTOR INDEX {IndexName} 
                    ON {FullTableName}(embedding)
                    WITH (metric = '{_distanceMetric}');
                END
                """;

            try
            {
                await using var createIndexCmd = new SqlCommand(createIndexSql, connection);
                await createIndexCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException)
            {
                // Vector index creation may fail on some SQL Server versions
                // The store will still work with exact k-NN search
            }
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        string chunkId,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        var vectorString = EmbeddingToString(embedding.Span);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Use MERGE for upsert
        var sql = $"""
            MERGE INTO {FullTableName} AS target
            USING (SELECT @chunkId AS chunk_id) AS source
            ON target.chunk_id = source.chunk_id
            WHEN MATCHED THEN
                UPDATE SET embedding = CAST(@embedding AS VECTOR({_dimension}))
            WHEN NOT MATCHED THEN
                INSERT (chunk_id, embedding)
                VALUES (@chunkId, CAST(@embedding AS VECTOR({_dimension})));
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkId", chunkId);
        command.Parameters.AddWithValue("@embedding", vectorString);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertBatchAsync(
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>> embeddings,
        CancellationToken cancellationToken = default)
    {
        var embeddingsList = embeddings.ToList();
        if (embeddingsList.Count == 0) return;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Process in batches to avoid command size limits
        const int batchSize = 100;
        foreach (var batch in embeddingsList.Chunk(batchSize))
        {
            await UpsertBatchInternalAsync(connection, batch, cancellationToken);
        }
    }

    private async Task UpsertBatchInternalAsync(
        SqlConnection connection,
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>> batch,
        CancellationToken cancellationToken)
    {
        var batchList = batch.ToList();
        if (batchList.Count == 0) return;

        // Build a multi-row MERGE statement
        var sb = new StringBuilder();
        sb.AppendLine($"MERGE INTO {FullTableName} AS target");
        sb.AppendLine("USING (VALUES");

        var parameters = new List<SqlParameter>();
        for (var i = 0; i < batchList.Count; i++)
        {
            var (chunkId, embedding) = batchList[i];
            var vectorString = EmbeddingToString(embedding.Span);

            if (i > 0) sb.AppendLine(",");
            sb.Append($"    (@chunkId{i}, CAST(@embedding{i} AS VECTOR({_dimension})))");

            parameters.Add(new SqlParameter($"@chunkId{i}", chunkId));
            parameters.Add(new SqlParameter($"@embedding{i}", vectorString));
        }

        sb.AppendLine();
        sb.AppendLine(") AS source (chunk_id, embedding)");
        sb.AppendLine("ON target.chunk_id = source.chunk_id");
        sb.AppendLine("WHEN MATCHED THEN");
        sb.AppendLine("    UPDATE SET embedding = source.embedding");
        sb.AppendLine("WHEN NOT MATCHED THEN");
        sb.AppendLine("    INSERT (chunk_id, embedding)");
        sb.AppendLine("    VALUES (source.chunk_id, source.embedding);");

        await using var command = new SqlCommand(sb.ToString(), connection);
        command.Parameters.AddRange(parameters.ToArray());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {FullTableName} WHERE chunk_id = @chunkId";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkId", chunkId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Chunk IDs follow pattern: {documentId}_chunk_{index}
        var sql = $"DELETE FROM {FullTableName} WHERE chunk_id LIKE @pattern";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@pattern", $"{documentId}_chunk_%");

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // COUNT_BIG returns bigint, so the scalar maps straight onto long.
        var sql = $"SELECT COUNT_BIG(*) FROM {FullTableName}";

        await using var command = new SqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : 0L;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var ids = chunkIds.ToList();
        if (ids.Count == 0)
            return [];

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var found = new List<string>();

        // 500 per batch keeps well clear of the 2100 parameter ceiling.
        foreach (var batch in ids.Chunk(500))
        {
            var parameterNames = new string[batch.Length];

            await using var command = new SqlCommand { Connection = connection };
            for (var i = 0; i < batch.Length; i++)
            {
                parameterNames[i] = $"@id{i}";
                command.Parameters.AddWithValue($"@id{i}", batch[i]);
            }

            command.CommandText =
                $"SELECT chunk_id FROM {FullTableName} WHERE chunk_id IN ({string.Join(", ", parameterNames)})";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add(reader.GetString(0));
            }
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var vectorString = EmbeddingToString(queryEmbedding.Span);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Use VECTOR_DISTANCE for similarity search
        var sql = $"""
            SELECT TOP (@limit) 
                chunk_id,
                VECTOR_DISTANCE('{_distanceMetric}', embedding, CAST(@queryEmbedding AS VECTOR({_dimension}))) AS distance
            FROM {FullTableName}
            ORDER BY distance ASC
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@queryEmbedding", vectorString);

        var results = new List<VectorSearchResult>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var chunkId = reader.GetString(0);
            var distance = reader.GetDouble(1);

            results.Add(new VectorSearchResult
            {
                ChunkId = chunkId,
                Score = DistanceToSimilarity(distance)
            });
        }

        return results;
    }

    /// <summary>
    /// Converts an embedding to a SQL Server vector string format: '[0.1, 0.2, 0.3, ...]'
    /// </summary>
    private static string EmbeddingToString(ReadOnlySpan<float> embedding)
    {
        var sb = new StringBuilder(embedding.Length * 12); // Estimate size
        sb.Append('[');

        for (var i = 0; i < embedding.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(embedding[i].ToString("G9", CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Converts distance to similarity score (higher = more similar).
    /// </summary>
    private static double DistanceToSimilarity(double distance)
    {
        // For cosine distance: similarity = 1 - distance
        // For euclidean: similarity = 1 / (1 + distance)
        return 1.0 / (1.0 + distance);
    }
}
