using Mjm.LocalDocs.Core.Abstractions;

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
    private readonly IDocumentFileStorage? _fileStorage;

    /// <summary>
    /// Creates a new <see cref="ProjectService"/>.
    /// </summary>
    /// <param name="projects">Project repository.</param>
    /// <param name="documents">Document repository.</param>
    /// <param name="vectorStore">Vector store holding the embeddings.</param>
    /// <param name="fileStorage">
    /// External file storage, or null when file content is held in the database.
    /// </param>
    public ProjectService(
        IProjectRepository projects,
        IDocumentRepository documents,
        IVectorStore vectorStore,
        IDocumentFileStorage? fileStorage = null)
    {
        _projects = projects;
        _documents = documents;
        _vectorStore = vectorStore;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Deletes a project along with everything belonging to it, including the parts no database
    /// cascade reaches.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the project existed and was deleted, false if it was not found.</returns>
    public async Task<bool> DeleteProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        // Read the ids first: deleting the project cascades the document rows away, and with them
        // the only record of which embeddings and files were supposed to go too.
        var locations = await _documents.GetFileLocationsByProjectAsync(projectId, cancellationToken);

        foreach (var location in locations)
        {
            if (_fileStorage is not null && !string.IsNullOrEmpty(location.StorageLocation))
            {
                await _fileStorage.DeleteFileAsync(
                    location.DocumentId,
                    location.StorageLocation,
                    cancellationToken);
            }

            await _vectorStore.DeleteByDocumentIdAsync(location.DocumentId, cancellationToken);
        }

        return await _projects.DeleteAsync(projectId, cancellationToken);
    }
}
