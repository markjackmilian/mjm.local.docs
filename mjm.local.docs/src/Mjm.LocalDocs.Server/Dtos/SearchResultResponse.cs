namespace Mjm.LocalDocs.Server.Dtos;

public sealed record SearchResultResponse(
    string ChunkId,
    string Content,
    string DocumentId,
    string? FileName,
    double Score);
