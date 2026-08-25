namespace Mjm.LocalDocs.Core.Models.Dashboard;

/// <summary>
/// Granularity of the know-how growth chart.
/// </summary>
public enum GrowthGranularity
{
    /// <summary>Calendar weeks, starting Monday.</summary>
    Weekly,

    /// <summary>Calendar months.</summary>
    Monthly
}

/// <summary>
/// A single contribution event: one document version being created.
/// </summary>
/// <param name="CreatedAt">When the document version was created.</param>
/// <param name="IsNewDocument">
/// True when this is a brand-new document (no parent), false when it is a new
/// version of an existing one.
/// </param>
public sealed record DocumentContribution(DateTimeOffset CreatedAt, bool IsNewDocument);

/// <summary>
/// How many chunks an active document currently has.
/// </summary>
/// <param name="DocumentId">The document identifier.</param>
/// <param name="ProjectId">The owning project identifier.</param>
/// <param name="FileName">The document file name, for display.</param>
/// <param name="ChunkCount">Number of chunks persisted for the document.</param>
public sealed record DocumentChunkTally(
    string DocumentId,
    string ProjectId,
    string FileName,
    int ChunkCount);

/// <summary>
/// An update that never completed: a document that is still active even though a newer version
/// of it is also active. Both answer the same searches until the update is closed.
/// </summary>
/// <param name="DocumentId">The newer version, still active.</param>
/// <param name="FileName">The newer version's file name, for display.</param>
/// <param name="ParentDocumentId">The older version, which should have been retired.</param>
/// <param name="ParentFileName">The older version's file name, for display.</param>
public sealed record InterruptedUpdate(
    string DocumentId,
    string FileName,
    string ParentDocumentId,
    string ParentFileName);

/// <summary>
/// Which document a chunk belongs to.
/// </summary>
/// <param name="DocumentId">The owning document identifier.</param>
/// <param name="ChunkId">The chunk identifier.</param>
public sealed record ChunkOwnership(string DocumentId, string ChunkId);

/// <summary>
/// One bar of the growth chart.
/// </summary>
/// <param name="Start">Inclusive start of the bucket.</param>
/// <param name="Label">Display label for the axis.</param>
/// <param name="NewDocuments">Brand-new documents created in the bucket.</param>
/// <param name="NewVersions">New versions of existing documents created in the bucket.</param>
/// <param name="IsPartial">
/// True for the bucket still in progress, so a naturally low final bar is not read as a collapse.
/// </param>
public sealed record GrowthBucket(
    DateTimeOffset Start,
    string Label,
    int NewDocuments,
    int NewVersions,
    bool IsPartial);

/// <summary>
/// Searchability of the active document set.
/// </summary>
/// <param name="ActiveDocuments">Number of documents not superseded.</param>
/// <param name="FullyIndexed">Documents with at least one chunk and no missing embeddings.</param>
/// <param name="Broken">
/// Documents that are not fully searchable: zero chunks, or at least one chunk without an embedding.
/// </param>
/// <param name="InterruptedUpdates">
/// Updates that never completed. Distinct from <paramref name="Broken"/>: these documents are
/// perfectly searchable, which is the problem — so is the older version they were meant to
/// replace, and both answer the same query.
/// </param>
/// <param name="MissingFiles">
/// Active documents whose row points at an external file that no longer exists. Distinct from
/// <paramref name="Broken"/> in the direction that matters: these documents are perfectly
/// searchable and cannot be repaired by reindexing, because the text survived and the file did
/// not. Populated only by a forced reconciliation — see the remarks on
/// <c>DashboardMetricsService.GetIndexHealthAsync</c>.
/// </param>
public sealed record IndexHealth(
    int ActiveDocuments,
    int FullyIndexed,
    IReadOnlyList<DocumentChunkTally> Broken,
    IReadOnlyList<InterruptedUpdate> InterruptedUpdates,
    IReadOnlyList<MissingFile> MissingFiles);

/// <summary>
/// Everything the dashboard renders in one payload.
/// </summary>
/// <param name="ActiveDocumentCount">Documents not superseded.</param>
/// <param name="NewInPeriod">Brand-new documents in the current rolling window.</param>
/// <param name="NewInPreviousPeriod">Brand-new documents in the preceding window of equal length.</param>
/// <param name="LastContributionAt">Most recent creation across all documents, or null when empty.</param>
/// <param name="Health">Index health of the active document set.</param>
/// <param name="Growth">Twelve buckets, oldest first.</param>
public sealed record DashboardMetrics(
    int ActiveDocumentCount,
    int NewInPeriod,
    int NewInPreviousPeriod,
    DateTimeOffset? LastContributionAt,
    IndexHealth Health,
    IReadOnlyList<GrowthBucket> Growth);

/// <summary>
/// Where a document's original file lives, if it lives outside the database.
/// </summary>
/// <param name="DocumentId">The document identifier.</param>
/// <param name="StorageLocation">
/// The external storage path, or null when the file content is held in the database row.
/// </param>
public sealed record DocumentFileLocation(string DocumentId, string? StorageLocation);

/// <summary>
/// The owning document's identity and state for one chunk, without its content.
/// </summary>
/// <param name="ChunkId">The chunk identifier.</param>
/// <param name="DocumentId">The owning document identifier.</param>
/// <param name="ProjectId">The owning project identifier, for filtering a search by project.</param>
/// <param name="IsSuperseded">Whether the owning document has been replaced by a newer version.</param>
public sealed record ChunkDocumentContext(
    string ChunkId,
    string DocumentId,
    string ProjectId,
    bool IsSuperseded);

/// <summary>
/// An active document whose original file is stored outside the database.
/// </summary>
/// <param name="DocumentId">The document identifier.</param>
/// <param name="FileName">The document's file name, for display.</param>
/// <param name="StorageLocation">The external storage path the row points at.</param>
public sealed record MissingFile(string DocumentId, string FileName, string StorageLocation);
