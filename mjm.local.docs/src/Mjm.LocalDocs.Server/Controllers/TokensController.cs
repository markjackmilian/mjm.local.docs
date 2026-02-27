using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Services;
using Mjm.LocalDocs.Server.Dtos;

namespace Mjm.LocalDocs.Server.Controllers;

[ApiController]
[Route("api/tokens")]
[Authorize]
public sealed class TokensController : ControllerBase
{
    private readonly ApiTokenService _tokenService;
    private readonly McpOptions _mcpOptions;

    public TokensController(
        ApiTokenService tokenService,
        IOptions<McpOptions> mcpOptions)
    {
        _tokenService = tokenService;
        _mcpOptions = mcpOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tokens = await _tokenService.GetAllTokensAsync(ct);
        return Ok(new
        {
            tokens = tokens.Select(MapToken).ToList(),
            mcpAuthRequired = _mcpOptions.RequireAuthentication
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var token = await _tokenService.GetTokenByIdAsync(id, ct);
        if (token is null)
            return NotFound();

        return Ok(MapToken(token));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTokenRequest request, CancellationToken ct)
    {
        var (token, plainTextToken) = await _tokenService.CreateTokenAsync(
            request.Name,
            request.ExpiresAt,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = token.Id },
            new TokenCreatedResponse(MapToken(token), plainTextToken));
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        var revoked = await _tokenService.RevokeTokenAsync(id, ct);
        if (!revoked)
            return NotFound();

        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await _tokenService.DeleteTokenAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    private static TokenResponse MapToken(ApiToken t) =>
        new(t.Id, t.Name, t.TokenPrefix, t.CreatedAt, t.ExpiresAt,
            t.LastUsedAt, t.IsRevoked, t.IsValid);
}
