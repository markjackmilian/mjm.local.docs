namespace Mjm.LocalDocs.Server.Dtos;

public sealed record McpConfigResponse(
    string ServerUrl,
    bool RequireAuthentication,
    string ClaudeCliCommand,
    string ClaudeJsonConfig,
    string OpenCodeJsonConfig);
