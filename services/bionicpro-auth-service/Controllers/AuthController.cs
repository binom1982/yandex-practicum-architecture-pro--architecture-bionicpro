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
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        var tokens = await _keycloak.AuthenticateAsync(request.Username, request.Password);
        if (tokens == null)
            return Unauthorized(new { error = "invalid_credentials" });

        // ✅ Вместо вызова к /userinfo:
        var userInfo = ParseUserInfoFromToken(tokens.AccessToken);
        if (userInfo?.Sub == null)
            return Unauthorized(new { error = "invalid_token" });
        /*var userInfo = await _keycloak.GetUserInfoAsync(tokens.AccessToken);
        if (userInfo?.Sub == null)
            return Unauthorized(new { error = "user_info_failed" });*/

        var session = await _sessionService.CreateSessionAsync(tokens, userInfo.Sub);
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

        // После создания сессии, перед возвратом:
        var roles = ParseRolesFromToken(tokens.AccessToken); // ← добавить

        return Ok(new
        {
            success = true,
            roles = roles  // ← добавить
        });

        
    }

    private List<string> ParseRolesFromToken(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        // Keycloak кладёт роли в claim "realm_access.roles"
        var rolesClaim = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access.roles");
        if (rolesClaim?.Value == null) return new List<string>();

        return rolesClaim.Value.Split(',').Distinct().ToList();
    }

    public UserInfo? ParseUserInfoFromToken(string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("[JWT] Error: accessToken is null or empty");
            return null;
        }

        try
        {
            Console.WriteLine($"[JWT] Parsing token (length: {accessToken.Length})");

            var parts = accessToken.Split('.');
            if (parts.Length != 3)
            {
                Console.WriteLine($"[JWT] Error: Invalid JWT format, parts count: {parts.Length}");
                return null;
            }

            // Декодируем payload с корректным паддингом
            var payload = parts[1];
            if (string.IsNullOrEmpty(payload))
            {
                Console.WriteLine("[JWT] Error: payload is empty");
                return null;
            }

            // Base64Url → Base64
            payload = payload.Replace('-', '+').Replace('_', '/');
            var padding = new string('=', (4 - payload.Length % 4) % 4);
            payload += padding;

            byte[] jsonBytes;
            try
            {
                jsonBytes = Convert.FromBase64String(payload);
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"[JWT] Error: Base64 decode failed: {ex.Message}");
                return null;
            }

            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            Console.WriteLine($"[JWT] Payload JSON: {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Безопасное извлечение свойств
            var sub = root.TryGetProperty("sub", out var s) ? s.GetString() : null;
            var username = root.TryGetProperty("preferred_username", out var u) ? u.GetString() : null;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;

            Console.WriteLine($"[JWT] Parsed: sub={sub ?? "null"}, username={username ?? "null"}, email={email ?? "null"}");

            return string.IsNullOrEmpty(sub) ? null : new UserInfo(sub, username, email);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JWT] Exception: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[JWT] Stack: {ex.StackTrace}");
            return null;
        }
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

public record UserInfo(string Sub, string? PreferredUsername, string? Email);