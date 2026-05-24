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
        // 1. Запрос токена в Keycloak
        var tokens = await _keycloak.AuthenticateAsync(request.Username, request.Password);
        if (tokens == null)
            return Unauthorized(new { error = "invalid_credentials" });

        // 2. Парсим JWT для получения sub
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

        // 3. Логируем ВСЕ claims для отладки
        Console.WriteLine("=== JWT Claims ===");
        foreach (var claim in jwt.Claims)
        {
            Console.WriteLine($"  {claim.Type}: {claim.Value}");
        }
        Console.WriteLine("==================");

        // 4. Пытаемся извлечь роли с обработкой ошибок
        object rolesResult;
        try
        {
            var roles = ExtractRoles(jwt);
            rolesResult = roles.Any() ? roles : "no_roles_in_token";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse roles: {ex.Message}");
            rolesResult = new
            {
                error = "role_parse_error",
                message = ex.Message,
                claimTypes = jwt.Claims.Select(c => c.Type).Distinct().ToList()
            };
        }

        // 5. Создаём сессию
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

        return Ok(new
        {
            success = true,
            roles = rolesResult,  // ← сюда попадёт список ролей или ошибка
            username = request.Username
        });
    }

    /// <summary>
    /// Извлечение ролей из JWT с поддержкой разных форматов Keycloak
    /// </summary>
    private List<string> ExtractRoles(JwtSecurityToken jwt)
    {
        var roles = new List<string>();

        // Вариант 1: realm_access.roles (наш основной mapper)
        var realmAccessClaim = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access");
        if (realmAccessClaim != null)
        {
            // Парсим JSON вида {"roles":["user","prothetic_user"]}
            try
            {
                var doc = JsonDocument.Parse(realmAccessClaim.Value);
                if (doc.RootElement.TryGetProperty("roles", out var rolesElement))
                {
                    foreach (var role in rolesElement.EnumerateArray())
                    {
                        roles.Add(role.GetString());
                    }
                }
            }
            catch { /* не JSON, пробуем дальше */ }
        }

        // Вариант 2: несколько отдельных claims "realm_access.roles"
        roles.AddRange(jwt.Claims
            .Where(c => c.Type == "realm_access.roles")
            .SelectMany(c => c.Value.Split(','))
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r)));

        // Вариант 3: client roles в resource_access
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
                        {
                            roles.Add(role.GetString());
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        // Вариант 4: просто claims с именем "roles" или "role"
        roles.AddRange(jwt.Claims
            .Where(c => c.Type is "roles" or "role")
            .SelectMany(c => c.Value.Split(','))
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r)));

        return roles.Distinct().ToList();
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