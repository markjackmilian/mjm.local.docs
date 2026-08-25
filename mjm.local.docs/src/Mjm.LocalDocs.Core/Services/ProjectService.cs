using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;

namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Project lifecycle operations that span more than one store.
/// </summary>
/// <remarks>
/// Exists because deleting a project is not a single-store operation and was being treated as
/// one. Removing the project row cascades through EF to its documents and chunks, but embeddings
/// live in a store EF knows nothing about, and externally-stored files live outside the database
/// entirely. Both were left behind at all three call sites. Owning the whole sequence here means
/// no caller has to remember the parts a cascade cannot reach.
/// </remarks>
public sealed class ProjectService
{
    private readonly IProjectRepository _projects;
    private readonly IDocumentRepository _documents;
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentLockRegistry _locks;
    private readonly IDocumentFileStorage? _fileStorage;

    /// <summary>
    /// Creates a new <see cref="ProjectService"/>.
    /// </summary>
    /// <param name="projects">Project repository.</param>
    /// <param name="documents">Document repository.</param>
    /// <param name="vectorStore">Vector store holding the embeddings.</param>
    /// <param name="locks">Per-document lock registry, so the sweep cannot race per-document work.</param>
    /// <param name="fileStorage">
    /// External file storage, or null when file content is held in the database.
    /// </param>
    public ProjectService(
        IProjectRepository projects,
        IDocumentRepository documents,
        IVectorStore vectorStore,
        IDocumentLockRegistry locks,
        IDocumentFileStorage? fileStorage = null)
    {
        _projects = projects;
        _documents = documents;
        _vectorStore = vectorStore;
        _locks = locks;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Deletes a project together with the embeddings and externally-stored files that no
    /// database cascade reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sweeps the project's documents twice, stripping each under its own lock: once immediately,
    /// and again right before the project row is deleted, so a document created between the two
    /// reads is not missed. A document created after the second sweep can still be missed — that
    /// residual window, from the second read to the delete, covers the second strip loop and is
    /// not closed by anything short of a transaction spanning the document, vector, and file
    /// stores, which do not share one.
    /// </para>
    /// <para>
    /// A failure part-way through leaves the project row and every document row intact — the
    /// project is deleted last — so a retry is idempotent and finishes the job. But a document
    /// already swept before the failure has permanently lost its original file even though its
    /// row survives: the delete already happened, nothing records that it did, and no later
    /// cleanup has a way to notice.
    /// </para>
    /// <para>
    /// A document already swept can have its embeddings written again by a concurrent reindex that
    /// acquires its lock after this sweep and before the project row is deleted. Because the swept
    /// set has already marked the document, the second sweep will not remove those newly-written
    /// embeddings, leaving orphans with no document row.
    /// </para>
    /// </remarks>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// True if the project existed and was deleted, false if it was not found. A failure during
    /// the sweep does not produce a return value: the exception propagates before the project row
    /// is deleted, which is what makes a retry meaningful.
    /// </returns>
    public async Task<bool> DeleteProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        // Read the ids first: deleting the project cascades the document rows away, and with them
        // the only record of which embeddings and files were supposed to go too.
        var swept = new HashSet<string>(StringComparer.Ordinal);

        await StripDocumentsAsync(
            await _documents.GetFileLocationsByProjectAsync(projectId, cancellationToken),
            swept,
            cancellationToken);

        // Read again immediately before the delete. A document created between the first read and
        // here would otherwise lose its row to the cascade with its file and embeddings untouched —
        // a permanent leak produced by the method meant to prevent one. This narrows that window to
        // the gap from this read to the delete, covering the second sweep; it does not close it, and
        // nothing short of a transaction spanning three stores would.
        await StripDocumentsAsync(
            await _documents.GetFileLocationsByProjectAsync(projectId, cancellationToken),
            swept,
            cancellationToken);

        return await _projects.DeleteAsync(projectId, cancellationToken);
    }

    private async Task StripDocumentsAsync(
        IReadOnlyList<DocumentFileLocation> locations,
        HashSet<string> swept,
        CancellationToken cancellationToken)
    {
        foreach (var location in locations)
        {
            if (!swept.Add(location.DocumentId))
                continue;

            // Under the document's own lock: serializes the sweep with a reindex ancestor walk so
            // they cannot interleave a half-written index during the strip. This does not exclude
            // a reindex that acquires the lock after this iteration: it can write embeddings before
            // the project row is deleted, leaving orphans with no document row for cleanup to key off.
            await using var documentLock = await _locks.AcquireAsync(location.DocumentId, cancellationToken);

            if (_fileStorage is not null && !string.IsNullOrEmpty(location.StorageLocation))
            {
                await _fileStorage.DeleteFileAsync(
                    location.DocumentId,
                    location.StorageLocation,
                    cancellationToken);
            }

            await _vectorStore.DeleteByDocumentIdAsync(location.DocumentId, cancellationToken);
        }
    }
}
