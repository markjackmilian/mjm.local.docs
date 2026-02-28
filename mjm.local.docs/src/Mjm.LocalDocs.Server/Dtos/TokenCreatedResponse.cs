namespace Mjm.LocalDocs.Server.Dtos;

public sealed record TokenCreatedResponse(TokenResponse Token, string PlainTextToken);
