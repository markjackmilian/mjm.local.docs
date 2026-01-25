using Microsoft.Data.Sqlite;
using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of vector store using sqlite-vec extension.
/// Uses vec0 virtual table for efficient vector similarity search with indexing.
/// </summary>
public sealed class SqliteVecVectorStore : IVectorStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly int _embeddingDimension;
    private bool _initialized;
    private bool _disposed;

    public SqliteVecVectorStore(string connectionString, int embeddingDimension = 1536)
    {
        _embeddingDimension = embeddingDimension;
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        
        _connection.EnableExtensions();
        _connection.LoadVector();
    }

    /// <summary>
    /// Ensures the vec0 virtual table exists.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        var sql = $"""
            CREATE VIRTUAL TABLE IF NOT EXISTS vec_chunks USING vec0(
                chunk_id TEXT PRIMARY KEY,
                embedding float[{_embeddingDimension}]
            );
            """;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _initialized = true;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        string chunkId,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO vec_chunks(chunk_id, embedding)
            VALUES (@chunkId, @embedding)
            """;
        cmd.Parameters.AddWithValue("@chunkId", chunkId);
        cmd.Parameters.AddWithValue("@embedding", EmbeddingToArray(embedding));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertBatchAsync(
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>> embeddings,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var (chunkId, embedding) in embeddings)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO vec_chunks(chunk_id, embedding)
                    VALUES (@chunkId, @embedding)
                    """;
                cmd.Parameters.AddWithValue("@chunkId", chunkId);
                cmd.Parameters.AddWithValue("@embedding", EmbeddingToArray(embedding));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM vec_chunks WHERE chunk_id = @chunkId";
        cmd.Parameters.AddWithValue("@chunkId", chunkId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM vec_chunks WHERE chunk_id LIKE @pattern";
        cmd.Parameters.AddWithValue("@pattern", $"{documentId}_chunk_%");

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var results = new List<VectorSearchResult>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT chunk_id, distance
            FROM vec_chunks
            WHERE embedding MATCH @queryEmbedding
            ORDER BY distance
            LIMIT @limit * 2
            """;
        cmd.Parameters.AddWithValue("@queryEmbedding", EmbeddingToArray(queryEmbedding));
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var chunkId = reader.GetString(0);
            var distance = reader.GetDouble(1);

            results.Add(new VectorSearchResult
            {
                ChunkId = chunkId,
                Score = 1.0 / (1.0 + distance)
            });
        }

        return results.Take(limit).ToList();
    }

    /// <summary>
    /// Converts a ReadOnlyMemory<float> embedding to an array for sqlite-vec.
    /// </summary>
    private static float[] EmbeddingToArray(ReadOnlyMemory<float> embedding)
    {
        return embedding.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _connection.Dispose();
        _disposed = true;
    }
}
