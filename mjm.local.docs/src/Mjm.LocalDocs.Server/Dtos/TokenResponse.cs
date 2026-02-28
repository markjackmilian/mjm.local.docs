namespace Mjm.LocalDocs.Server.Dtos;

public sealed record TokenResponse(
    string Id,
    string Name,
    string? TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsRevoked,
    bool IsValid);
