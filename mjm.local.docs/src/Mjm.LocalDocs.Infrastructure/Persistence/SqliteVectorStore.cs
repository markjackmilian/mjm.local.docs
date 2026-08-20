using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of vector store.
/// Uses a simple table with binary embedding storage and brute-force cosine similarity.
/// For production use with large datasets, consider using sqlite-vec extension or a dedicated vector database.
/// </summary>
public sealed class SqliteVectorStore : IVectorStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly int _embeddingDimension;
    private bool _initialized;
    private bool _disposed;

    public SqliteVectorStore(string connectionString, int embeddingDimension = 1536)
    {
        _embeddingDimension = embeddingDimension;
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures the embeddings table exists.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        var sql = """
            CREATE TABLE IF NOT EXISTS chunk_embeddings (
                chunk_id TEXT PRIMARY KEY,
                embedding BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_chunk_embeddings_chunk_id ON chunk_embeddings(chunk_id);
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
            INSERT OR REPLACE INTO chunk_embeddings(chunk_id, embedding)
            VALUES (@chunkId, @embedding)
            """;
        cmd.Parameters.AddWithValue("@chunkId", chunkId);
        cmd.Parameters.AddWithValue("@embedding", EmbeddingToBlob(embedding));

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
                    INSERT OR REPLACE INTO chunk_embeddings(chunk_id, embedding)
                    VALUES (@chunkId, @embedding)
                    """;
                cmd.Parameters.AddWithValue("@chunkId", chunkId);
                cmd.Parameters.AddWithValue("@embedding", EmbeddingToBlob(embedding));
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
        cmd.CommandText = "DELETE FROM chunk_embeddings WHERE chunk_id = @chunkId";
        cmd.Parameters.AddWithValue("@chunkId", chunkId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // Chunk IDs follow pattern: {documentId}_chunk_{index}
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM chunk_embeddings WHERE chunk_id LIKE @pattern";
        cmd.Parameters.AddWithValue("@pattern", $"{documentId}_chunk_%");

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chunk_embeddings";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : 0L;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var ids = chunkIds.ToList();
        if (ids.Count == 0)
            return [];

        var found = new List<string>();

        // Batch so a large probe cannot exceed the provider's parameter limit.
        foreach (var batch in ids.Chunk(500))
        {
            await using var cmd = _connection.CreateCommand();

            var parameterNames = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                parameterNames[i] = $"@id{i}";
                cmd.Parameters.AddWithValue($"@id{i}", batch[i]);
            }

            cmd.CommandText =
                $"SELECT chunk_id FROM chunk_embeddings WHERE chunk_id IN ({string.Join(", ", parameterNames)})";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
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
        await EnsureInitializedAsync(cancellationToken);

        var results = new List<VectorSearchResult>();

        // Load all embeddings and compute similarity (brute force)
        // For large datasets, consider using sqlite-vec extension
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT chunk_id, embedding FROM chunk_embeddings";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var chunkId = reader.GetString(0);
            var embeddingBlob = (byte[])reader[1];
            var embedding = BlobToEmbedding(embeddingBlob);

            var score = CosineSimilarity(queryEmbedding.Span, embedding.Span);

            results.Add(new VectorSearchResult
            {
                ChunkId = chunkId,
                Score = score
            });
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Converts a float embedding to a blob.
    /// </summary>
    private static byte[] EmbeddingToBlob(ReadOnlyMemory<float> embedding)
    {
        var floatArray = embedding.ToArray();
        var bytes = new byte[floatArray.Length * sizeof(float)];
        Buffer.BlockCopy(floatArray, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Converts a blob to a float embedding.
    /// </summary>
    private static ReadOnlyMemory<float> BlobToEmbedding(byte[] blob)
    {
        var floatArray = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, floatArray, 0, blob.Length);
        return floatArray;
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// </summary>
    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            return 0;

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var magnitude = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);

        return magnitude == 0 ? 0 : dotProduct / magnitude;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _connection.Dispose();
        _disposed = true;
    }
}
