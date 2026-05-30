using BionicproAuthService.Models;
using BionicproAuthService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BionicproAuthService.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IKeycloakClient _keycloak;
    private readonly IAuthSessionService _sessionService;
    private readonly AuthSessionOptions _sessionOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IKeycloakClient keycloak,
        IAuthSessionService sessionService,
        IOptions<AuthSessionOptions> options,
        ILogger<AuthController> logger)
    {
        _keycloak = keycloak;
        _sessionService = sessionService;
        _sessionOptions = options.Value;
        _logger = logger;
    }

    // 🔹 GET /auth/login — инициация PKCE flow (браузерный редирект)
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult LoginInit([FromQuery] string? redirect)
    {
        var redirectUri = string.IsNullOrEmpty(redirect)
            ? $"{Request.Scheme}://{Request.Host}"
            : redirect;

        // Генерируем PKCE параметры
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Сохраняем code_verifier во временном кеше (5 мин)
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString($"pkce_{state}", codeVerifier);

        var keycloakUrl = _keycloak.BuildAuthorizationUrl(
            redirectUri: $"{Request.Scheme}://{Request.Host}/auth/callback",
            state: state,
            codeChallenge: codeChallenge,
            codeChallengeMethod: "S256");

        return Redirect(keycloakUrl);
    }

    //// 🔹 POST /auth/login — legacy (для совместимости, если нужно)
    //[HttpPost("login")]
    //[ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    //[ProducesResponseType(StatusCodes.Status401Unauthorized)]
    //public async Task<IActionResult> Login([FromBody] AuthRequest request)
    //{
    //    // Можно оставить для внутренних сервисов или убрать
    //    return BadRequest(new { error = "use_get_login_for_browser" });
    //}

    // 🔹 GET /auth/callback — обработка возврата от Keycloak
    [HttpGet("callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
            return BadRequest(new { error, error_description = HttpContext.Request.Query["error_description"] });

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest(new { error = "missing_code_or_state" });

        // Восстанавливаем code_verifier
        var codeVerifier = HttpContext.Session.GetString($"pkce_{state}");
        if (string.IsNullOrEmpty(codeVerifier))
            return BadRequest(new { error = "invalid_state" });

        // Обмениваем code на токены
        var tokens = await _keycloak.ExchangeCodeForTokensAsync(code, codeVerifier);
        if (tokens == null)
            return Unauthorized(new { error = "token_exchange_failed" });

        // Извлекаем sub из access_token
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokens.AccessToken);
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (string.IsNullOrEmpty(sub))
            return Unauthorized(new { error = "invalid_token" });

        // Создаём сессию
        var session = await _sessionService.CreateSessionAsync(tokens, sub);
        if (session == null)
            return StatusCode(500, new { error = "session_creation_failed" });

        // Очищаем временный PKCE-стейт
        HttpContext.Session.Remove($"pkce_{state}");

        // Устанавливаем secure cookie
        Response.Cookies.Append(
            _sessionOptions.CookieName,
            session.SessionId,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps, // true в продакшене
                SameSite = SameSiteMode.Lax,
                Expires = session.ExpiresAt,
                Path = "/"
            });

        // Редирект обратно на фронтенд
        var targetRedirect = Uri.IsWellFormedUriString(state, UriKind.Absolute)
            ? state
            : $"{Request.Scheme}://{Request.Host}";
        return Redirect(targetRedirect);
    }

    // 🔹 GET /auth/session — проверка сессии (для фронтенда)
    [HttpGet("session")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CheckSession()
    {
        if (!Request.Cookies.TryGetValue(_sessionOptions.CookieName, out var sessionId))
            return Unauthorized();

        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session == null || session.IsExpired)
        {
            Response.Cookies.Delete(_sessionOptions.CookieName);
            return Unauthorized();
        }

        // 🔐 Ротация сессии: перепривязываем токены к новому session_id
        var newSession = await _sessionService.RotateSessionAsync(session);
        if (newSession != null)
        {
            Response.Cookies.Append(
                _sessionOptions.CookieName,
                newSession.SessionId,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Expires = newSession.ExpiresAt,
                    Path = "/"
                });
        }

        return Ok(new
        {
            authenticated = true,
            userId = session.UserId,
            expiresAt = session.ExpiresAt
        });
    }

    // 🔹 GET /auth/me — получение данных пользователя
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        if (!Request.Cookies.TryGetValue(_sessionOptions.CookieName, out var sessionId))
            return Unauthorized();

        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session == null || session.IsExpired)
            return Unauthorized();

        // Опционально: обновить access_token если он истёк
        if (session.IsAccessTokenExpired)
        {
            var refreshed = await _keycloak.RefreshAccessTokenAsync(session.RefreshToken);
            if (refreshed != null)
            {
                session.AccessToken = refreshed.AccessToken;
                await _sessionService.UpdateSessionAsync(session);
            }
        }

        // Извлекаем данные из access_token
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(session.AccessToken);

        return Ok(new UserInfo(
            Sub: session.UserId,
            PreferredUsername: jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value,
            Email: jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value
        ));
    }

    // 🔹 POST /auth/logout
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue(_sessionOptions.CookieName, out var sessionId))
        {
            var session = await _sessionService.GetSessionAsync(sessionId);
            if (session != null)
            {
                // Отзываем refresh_token в Keycloak (опционально)
                await _keycloak.RevokeTokenAsync(session.RefreshToken);
                await _sessionService.DeleteSessionAsync(sessionId);
            }
            Response.Cookies.Delete(_sessionOptions.CookieName);
        }
        return Ok(new { success = true });
    }

    // 🔹 PKCE утилиты
    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

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
}

public record UserInfo(string Sub, string? PreferredUsername, string? Email);

public class LoginResponse
{
    public bool Success { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
    public string Username { get; set; } = string.Empty;
    public string? Error { get; set; }
}