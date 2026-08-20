namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Thrown when a document was persisted but could not be indexed for search.
/// </summary>
/// <remarks>
/// This is deliberately distinct from a failed upload: the file is stored and the document
/// exists, it is simply not searchable until reindexed. Callers should say so rather than
/// reporting the upload as failed.
/// </remarks>
public sealed class DocumentIndexingException : Exception
{
    /// <summary>
    /// The document that was saved but not indexed.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// Creates a new <see cref="DocumentIndexingException"/>.
    /// </summary>
    /// <param name="documentId">The document that was saved but not indexed.</param>
    /// <param name="innerException">The failure that prevented indexing.</param>
    public DocumentIndexingException(string documentId, Exception innerException)
        : base(
            $"Document '{documentId}' was saved but could not be indexed. " +
            "It will not appear in search results until it is reindexed.",
            innerException)
    {
        DocumentId = documentId;
    }
}
