using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Server.Dtos;
using LocalDocsAuthOptions = Mjm.LocalDocs.Core.Models.AuthenticationOptions;

namespace Mjm.LocalDocs.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly LocalDocsAuthOptions _authOptions;

    public AuthController(IOptions<LocalDocsAuthOptions> authOptions)
    {
        _authOptions = authOptions.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!string.Equals(request.Username, _authOptions.Username, StringComparison.OrdinalIgnoreCase) ||
            request.Password != _authOptions.Password)
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, _authOptions.Username),
            new(ClaimTypes.NameIdentifier, _authOptions.Username)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProperties);

        return Ok(new UserInfoResponse(_authOptions.Username, true));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var username = User.Identity?.Name ?? "";
        return Ok(new UserInfoResponse(username, User.Identity?.IsAuthenticated ?? false));
    }
}
