namespace Mjm.LocalDocs.Server.Dtos;

public sealed record ProjectResponse(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
