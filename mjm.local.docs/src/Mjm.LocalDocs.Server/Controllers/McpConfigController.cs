using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Server.Dtos;

namespace Mjm.LocalDocs.Server.Controllers;

[ApiController]
[Route("api/mcp-config")]
[Authorize]
public sealed class McpConfigController : ControllerBase
{
    private readonly McpOptions _mcpOptions;

    public McpConfigController(IOptions<McpOptions> mcpOptions)
    {
        _mcpOptions = mcpOptions.Value;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var request = HttpContext.Request;
        var serverUrl = $"{request.Scheme}://{request.Host}";
        var mcpUrl = $"{serverUrl}/mcp";

        var claudeCliCommand = _mcpOptions.RequireAuthentication
            ? $"claude mcp add local-docs --transport http {mcpUrl} -- --header \"Authorization: Bearer YOUR_TOKEN\""
            : $"claude mcp add local-docs --transport http {mcpUrl}";

        var claudeJsonConfig = _mcpOptions.RequireAuthentication
            ? $$"""
              {
                "mcpServers": {
                  "local-docs": {
                    "type": "http",
                    "url": "{{mcpUrl}}",
                    "headers": {
                      "Authorization": "Bearer YOUR_TOKEN"
                    }
                  }
                }
              }
              """
            : $$"""
              {
                "mcpServers": {
                  "local-docs": {
                    "type": "http",
                    "url": "{{mcpUrl}}"
                  }
                }
              }
              """;

        var openCodeJsonConfig = _mcpOptions.RequireAuthentication
            ? $$"""
              {
                "mcpServers": {
                  "local-docs": {
                    "type": "http",
                    "url": "{{mcpUrl}}",
                    "headers": {
                      "Authorization": "Bearer YOUR_TOKEN"
                    }
                  }
                }
              }
              """
            : $$"""
              {
                "mcpServers": {
                  "local-docs": {
                    "type": "http",
                    "url": "{{mcpUrl}}"
                  }
                }
              }
              """;

        return Ok(new McpConfigResponse(
            serverUrl,
            _mcpOptions.RequireAuthentication,
            claudeCliCommand,
            claudeJsonConfig,
            openCodeJsonConfig));
    }
}
