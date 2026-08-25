namespace Mjm.LocalDocs.Core.Abstractions;

/// <summary>
/// Serialises operations that mutate one document's index, so two of them cannot interleave.
/// </summary>
/// <remarks>
/// Implementations must be registered as singletons. The services that use this are scoped, so a
/// lock held per service instance would be per-request and would protect nothing across browser
/// circuits or between the web UI and the MCP tools.
/// </remarks>
public interface IDocumentLockRegistry
{
    /// <summary>
    /// Waits for exclusive access to one document and returns a handle that releases it.
    /// </summary>
    /// <param name="documentId">The document to lock. Locks on different documents are independent.</param>
    /// <param name="cancellationToken">Cancellation token, observed while waiting.</param>
    /// <returns>A handle whose disposal releases the lock.</returns>
    Task<IAsyncDisposable> AcquireAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
