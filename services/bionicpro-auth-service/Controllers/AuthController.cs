using BionicproAuthService.Models;
using BionicproAuthService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BionicproAuthService.Controllers;



[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IKeycloakClient _keycloak;
    private readonly IAuthSessionService _sessionService;
    private readonly AuthSessionOptions _sessionOptions;

    public AuthController(
        IKeycloakClient keycloak,
        IAuthSessionService sessionService,
        IOptions<AuthSessionOptions> options)
    {
        _keycloak = keycloak;
        _sessionService = sessionService;
        _sessionOptions = options.Value;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        var tokens = await _keycloak.AuthenticateAsync(request.Username, request.Password);
        if (tokens == null)
            return Unauthorized(new { error = "invalid_credentials" });

        var userInfo = await _keycloak.GetUserInfoAsync(tokens.AccessToken);
        if (userInfo?.Sub == null)
            return Unauthorized(new { error = "user_info_failed" });

        var session = await _sessionService.CreateSessionAsync(tokens, userInfo.Sub);
        if (session == null)
            return StatusCode(500, new { error = "session_creation_failed" });

        Response.Cookies.Append(
            _sessionOptions.CookieName,
            session.SessionId,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = session.ExpiresAt
            });

        return Ok(new { success = true });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue(_sessionOptions.CookieName, out var sessionId))
        {
            await _sessionService.DeleteSessionAsync(sessionId);
            Response.Cookies.Delete(_sessionOptions.CookieName);
        }
        return Ok(new { success = true });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = HttpContext.Items["UserId"] as string;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        return Ok(new { userId });
    }
}