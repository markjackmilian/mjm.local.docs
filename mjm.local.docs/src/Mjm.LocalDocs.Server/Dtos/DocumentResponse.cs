namespace Mjm.LocalDocs.Server.Dtos;

public sealed record DocumentResponse(
    string Id,
    string ProjectId,
    string FileName,
    string FileExtension,
    long FileSizeBytes,
    int VersionNumber,
    string? ParentDocumentId,
    bool IsSuperseded,
    string? ContentHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
