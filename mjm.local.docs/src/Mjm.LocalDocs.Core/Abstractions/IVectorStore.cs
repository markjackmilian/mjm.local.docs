namespace Mjm.LocalDocs.Core.Abstractions;

/// <summary>
/// Store for document chunk embeddings and vector search operations.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Initializes the vector store (e.g., creates tables, indexes).
    /// Should be called once at application startup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or updates an embedding for a chunk.
    /// </summary>
    /// <param name="chunkId">The chunk identifier.</param>
    /// <param name="embedding">The embedding vector.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(
        string chunkId,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or updates multiple embeddings.
    /// </summary>
    /// <param name="embeddings">Dictionary of chunk ID to embedding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertBatchAsync(
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>> embeddings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an embedding for a chunk.
    /// </summary>
    /// <param name="chunkId">The chunk identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all embeddings for chunks belonging to a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total number of embeddings currently stored.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of stored embeddings.</returns>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of the supplied chunk identifiers that have an embedding stored.
    /// </summary>
    /// <remarks>
    /// Callers are responsible for batching: implementations may build one database parameter
    /// per identifier, and SQL Server caps a command at 2100 parameters.
    /// Pass at most a few hundred identifiers per call.
    /// </remarks>
    /// <param name="chunkIds">The chunk identifiers to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifiers that have an embedding, in no guaranteed order.</returns>
    Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for similar embeddings using vector similarity.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of chunk IDs with similarity scores, ordered by relevance.</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int limit = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a vector search result.
/// </summary>
public sealed class VectorSearchResult
{
    /// <summary>
    /// The chunk identifier.
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// The similarity score (higher is more similar).
    /// </summary>
    public required double Score { get; init; }
}
