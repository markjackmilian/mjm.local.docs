namespace Mjm.LocalDocs.Server.Dtos;

public sealed record LoginRequest(string Username, string Password, bool RememberMe = false);
