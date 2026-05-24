using BionicproAuthService.Models;
using BionicproAuthService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;

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
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        var tokens = await _keycloak.AuthenticateAsync(request.Username, request.Password);
        if (tokens == null)
            return Unauthorized(new { error = "invalid_credentials" });

        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(tokens.AccessToken);
        }
        catch (Exception ex)
        {
            return Unauthorized(new { error = "invalid_token", details = ex.Message });
        }

        var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub");
        if (subClaim == null)
            return Unauthorized(new { error = "invalid_token", details = "sub not found" });

        // Отладочный лог
        Console.WriteLine("=== JWT Claims ===");
        foreach (var claim in jwt.Claims)
            Console.WriteLine($"  {claim.Type}: {claim.Value}");
        Console.WriteLine("==================");

        // Извлекаем роли — всегда массив строк
        string[] roles;
        try
        {
            var rolesList = ExtractRoles(jwt);
            roles = rolesList.Any() ? rolesList.ToArray() : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse roles: {ex.Message}");
            roles = Array.Empty<string>(); // ← фиксированный тип для Swagger
        }

        var session = await _sessionService.CreateSessionAsync(tokens, subClaim.Value);
        if (session == null)
            return StatusCode(500, new { error = "session_creation_failed" });

        Response.Cookies.Append(
            _sessionOptions.CookieName,
            session.SessionId,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteMode.Strict,
                Expires = session.ExpiresAt
            });

        return Ok(new LoginResponse
        {
            Success = true,
            Roles = roles,
            Username = request.Username
        });
    }

    private List<string> ExtractRoles(JwtSecurityToken jwt)
    {
        var roles = new List<string>();

        var realmAccessClaim = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access");
        if (realmAccessClaim != null)
        {
            try
            {
                var doc = JsonDocument.Parse(realmAccessClaim.Value);
                if (doc.RootElement.TryGetProperty("roles", out var rolesElement))
                {
                    foreach (var role in rolesElement.EnumerateArray())
                        roles.Add(role.GetString());
                }
            }
            catch { }
        }

        roles.AddRange(jwt.Claims
            .Where(c => c.Type == "realm_access.roles")
            .SelectMany(c => c.Value.Split(','))
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r)));

        var resourceAccessClaim = jwt.Claims.FirstOrDefault(c => c.Type == "resource_access");
        if (resourceAccessClaim != null)
        {
            try
            {
                var doc = JsonDocument.Parse(resourceAccessClaim.Value);
                foreach (var client in doc.RootElement.EnumerateObject())
                {
                    if (client.Value.TryGetProperty("roles", out var clientRoles))
                    {
                        foreach (var role in clientRoles.EnumerateArray())
                            roles.Add(role.GetString());
                    }
                }
            }
            catch { }
        }

        roles.AddRange(jwt.Claims
            .Where(c => c.Type is "roles" or "role")
            .SelectMany(c => c.Value.Split(','))
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r)));

        return roles.Distinct().ToList();
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var userId = HttpContext.Items["UserId"] as string;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        return Ok(new { userId });
    }
}

public record UserInfo(string Sub, string? PreferredUsername, string? Email);

public class LoginResponse
{
    public bool Success { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
    public string Username { get; set; } = string.Empty;
    public string? Error { get; set; }
}