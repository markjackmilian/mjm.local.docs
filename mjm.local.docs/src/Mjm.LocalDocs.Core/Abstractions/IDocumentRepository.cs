using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Models.Dashboard;

namespace Mjm.LocalDocs.Core.Abstractions;

/// <summary>
/// Repository for storing and retrieving documents and their chunks.
/// </summary>
public interface IDocumentRepository
{
    #region Document Operations

    /// <summary>
    /// Adds a new document.
    /// </summary>
    /// <param name="document">The document to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The added document.</returns>
    Task<Document> AddDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a document by its identifier.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, or null if not found.</returns>
    Task<Document?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the original file content for a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file content as byte array, or null if not found.</returns>
    Task<byte[]?> GetDocumentFileAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all documents for a project.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of documents in the project.</returns>
    Task<IReadOnlyList<Document>> GetDocumentsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document and all its chunks.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a document exists.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if exists, false otherwise.</returns>
    Task<bool> DocumentExistsAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a document with the same content hash exists in the project.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="contentHash">The SHA256 hash of the file content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing document if found, null otherwise.</returns>
    Task<Document?> GetDocumentByHashAsync(
        string projectId,
        string contentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a document as superseded by a newer version.
    /// Sets IsSuperseded to true and UpdatedAt to current time.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SupersedeDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all versions in the document chain, traversing ParentDocumentId links.
    /// Returns documents ordered by VersionNumber descending (newest first).
    /// </summary>
    /// <param name="documentId">Any document ID in the version chain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all versions ordered by version number descending.</returns>
    Task<IReadOnlyList<Document>> GetDocumentVersionsAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Chunk Operations

    /// <summary>
    /// Adds document chunks to the store.
    /// </summary>
    /// <param name="chunks">The chunks to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a chunk by its identifier.
    /// </summary>
    /// <param name="chunkId">The chunk identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chunk, or null if not found.</returns>
    Task<DocumentChunk?> GetChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all chunks for a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of chunks for the document.</returns>
    Task<IReadOnlyList<DocumentChunk>> GetChunksByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunks by their identifiers.
    /// </summary>
    /// <param name="chunkIds">The chunk identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of chunks found.</returns>
    Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks for a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteChunksByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Project Operations

    /// <summary>
    /// Gets all project IDs that have documents.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of project identifiers.</returns>
    Task<IReadOnlyList<string>> GetProjectsWithDocumentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all documents and chunks for a project.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteDocumentsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Dashboard Aggregates

    /// <summary>
    /// Gets one record per document version created at or after the given instant.
    /// Superseded documents are included: the series counts creation events, not present state.
    /// </summary>
    /// <remarks>
    /// Implementations must apply the window in memory over a scalar column projection: the
    /// SQLite provider cannot translate a relational comparison on a <see cref="DateTimeOffset"/>.
    /// </remarks>
    /// <param name="since">Inclusive lower bound on <see cref="Document.CreatedAt"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Contribution records, in no guaranteed order.</returns>
    Task<IReadOnlyList<DocumentContribution>> GetContributionsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts documents that are not superseded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of active documents.</returns>
    Task<int> CountActiveDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent <see cref="Document.CreatedAt"/> across all documents,
    /// superseded ones included.
    /// </summary>
    /// <remarks>
    /// Implementations must aggregate in memory over a scalar column projection: the SQLite
    /// provider cannot apply <c>Max</c> to a <see cref="DateTimeOffset"/> column.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest creation timestamp, or null when there are no documents.</returns>
    Task<DateTimeOffset?> GetLastContributionAtAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts active documents per project in a single query.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project identifier to active document count. Projects with none are absent.</returns>
    Task<IReadOnlyDictionary<string, int>> GetActiveDocumentCountsByProjectAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts all persisted chunks.
    /// </summary>
    /// <remarks>
    /// Compared against <see cref="IVectorStore.CountAsync"/> as a cheap pre-check for the health
    /// probe. The two are NOT guaranteed equal even on a healthy knowledge base: deleting a project
    /// cascades through EF to its documents and chunks, but the embedding store is not an EF table,
    /// so its rows are orphaned and the totals diverge permanently. A divergence therefore means
    /// "run the full probe", never "something is broken" — the probe is what decides. The
    /// consequence of orphans is a slower dashboard, never a wrong one.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total number of chunks.</returns>
    Task<long> CountChunksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the chunk count of every active document, including documents with zero chunks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One tally per active document.</returns>
    Task<IReadOnlyList<DocumentChunkTally>> GetActiveDocumentChunkTalliesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the chunks belonging to the given documents, each paired with its owner.
    /// </summary>
    /// <remarks>
    /// The owner is carried explicitly rather than parsed back out of the
    /// <c>{documentId}_chunk_{index}</c> identifier convention.
    /// </remarks>
    /// <param name="documentIds">The documents whose chunks are wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chunk-to-document pairs, in no guaranteed order.</returns>
    Task<IReadOnlyList<ChunkOwnership>> GetChunkOwnershipAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every update that never completed: an active document whose
    /// <see cref="Document.ParentDocumentId"/> names another document that is also still active.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, which is what allowed a failed update to leave the previous
    /// version searchable instead of retiring it blind. Both versions answer the same query until
    /// the update is closed, so this is a correctness signal, not a tidiness one.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One record per unclosed parent-child link, in no guaranteed order.</returns>
    Task<IReadOnlyList<InterruptedUpdate>> GetInterruptedUpdatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an active document names the given document as its parent.
    /// </summary>
    /// <param name="documentId">The candidate parent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when a newer version of this document is still active.</returns>
    Task<bool> HasActiveChildAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the identifier and external storage path of every document in a project,
    /// superseded versions included.
    /// </summary>
    /// <remarks>
    /// Two scalar columns, deliberately. Deleting a project has to visit each document to remove
    /// its embeddings and its externally-stored file, neither of which any database cascade
    /// reaches — and enumerating them through <see cref="GetDocumentsByProjectAsync"/> would
    /// materialise every <c>FileContent</c> blob in the project to do it.
    /// </remarks>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One record per document, in no guaranteed order.</returns>
    Task<IReadOnlyList<DocumentFileLocation>> GetFileLocationsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    #endregion
}
