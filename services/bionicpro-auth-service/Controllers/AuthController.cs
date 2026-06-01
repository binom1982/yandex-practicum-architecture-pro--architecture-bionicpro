using BionicproAuthService.Models;
using BionicproAuthService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

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

        // 🔹 НОВОЕ: Лог для проверки, что сервис один экземпляр (Singleton)
        _logger.LogDebug("AuthController instance hash: {HashCode}", GetHashCode());
        _logger.LogDebug("AuthSession options: CookieName={CookieName}, Lifetime={Lifetime}min",
            _sessionOptions.CookieName, _sessionOptions.SessionLifetimeMinutes);
    }

    // 🔹 GET /auth/login — инициация PKCE flow
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult LoginInit([FromQuery] string? redirect)
    {
        _logger.LogInformation("Initiating PKCE login. Redirect: {Redirect}", redirect ?? "default");

        // 🔹 Определяем целевой redirect_uri для фронтенда
        var frontendRedirect = string.IsNullOrEmpty(redirect)
            ? "http://localhost:3000"  // дефолт для фронтенда
            : redirect;

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        var state = Guid.NewGuid().ToString("N");

        // 🔹 Сохраняем ОБА параметра в сессии
        HttpContext.Session.SetString($"pkce_{state}", codeVerifier);
        HttpContext.Session.SetString($"redirect_{state}", frontendRedirect);
        _logger.LogDebug("PKCE state created: {State}, Frontend redirect: {Redirect}", state, frontendRedirect);

        var keycloakUrl = _keycloak.BuildAuthorizationUrl(
            redirectUri: $"{Request.Scheme}://{Request.Host}/auth/callback",
            state: state,
            codeChallenge: codeChallenge,
            codeChallengeMethod: "S256");

        _logger.LogInformation("Redirecting to Keycloak: {KeycloakUrl}", keycloakUrl);
        return Redirect(keycloakUrl);
    }

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
        {
            _logger.LogWarning("Keycloak returned error: {Error}, Description: {ErrorDescription}",
                error, HttpContext.Request.Query["error_description"]);
            return BadRequest(new { error, error_description = HttpContext.Request.Query["error_description"] });
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            _logger.LogWarning("Callback missing required parameters. Code: {CodePresent}, State: {StatePresent}",
                !string.IsNullOrEmpty(code), !string.IsNullOrEmpty(state));
            return BadRequest(new { error = "missing_code_or_state" });
        }

        _logger.LogDebug("Processing callback. State: {State}, Code prefix: {CodePrefix}",
            state, code?.Substring(0, Math.Min(8, code?.Length ?? 0)));

        // 🔹 Восстанавливаем code_verifier
        var codeVerifier = HttpContext.Session.GetString($"pkce_{state}");
        if (string.IsNullOrEmpty(codeVerifier))
        {
            _logger.LogWarning("PKCE state not found or expired: {State}", state);
            return BadRequest(new { error = "invalid_state" });
        }

        // 🔹 Обмениваем code на токены
        var tokens = await _keycloak.ExchangeCodeForTokensAsync(code!, codeVerifier); // 🔹 НОВОЕ: ! для подавления предупреждения
        if (tokens == null)
        {
            _logger.LogError("Token exchange failed for state: {State}", state);
            return Unauthorized(new { error = "token_exchange_failed" });
        }

        _logger.LogDebug("Token exchange successful for state: {State}", state);

        // 🔹 Извлекаем sub из access_token
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokens.AccessToken);
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            _logger.LogError("Access token missing 'sub' claim");
            return Unauthorized(new { error = "invalid_token" });
        }

        // 🔹 Создаём сессию
        var session = await _sessionService.CreateSessionAsync(tokens, sub);
        if (session == null)
        {
            _logger.LogError("Failed to create session for user: {UserId}", sub);
            return StatusCode(500, new { error = "session_creation_failed" });
        }

        // 🔹 Очищаем временные данные из сессии
        HttpContext.Session.Remove($"pkce_{state}");

        // 🔹 Устанавливаем secure cookie
        Response.Cookies.Append(
            _sessionOptions.CookieName,
            session.SessionId,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = session.ExpiresAt,
                Path = "/"
            });

        _logger.LogInformation("Session created for user: {UserId}, SessionId: {SessionId}",
            sub, session.SessionId);

        // 🔹 ВОССТАНАВЛИВАЕМ redirect_uri из сессии (а не используем state!)
        var targetRedirect = HttpContext.Session.GetString($"redirect_{state}") ?? "http://localhost:3000";
        HttpContext.Session.Remove($"redirect_{state}"); // очищаем

        _logger.LogInformation("Redirecting back to frontend: {TargetRedirect}", targetRedirect);
        return Redirect(targetRedirect);
    }

    // 🔹 GET /auth/session — проверка сессии
    [HttpGet("session")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CheckSession()
    {
        // 🔹 НОВОЕ: Логируем ВСЕ куки с их значениями (только для dev!)
        _logger.LogDebug("All cookies received: {Cookies}",
            string.Join("; ", Request.Cookies.Select(c => $"{c.Key}={c.Value?.Substring(0, Math.Min(8, c.Value.Length))}...")));

        if (!Request.Cookies.TryGetValue(_sessionOptions.CookieName, out var sessionId))
        {
            _logger.LogWarning("Cookie '{CookieName}' NOT found. Available: {Available}",
                _sessionOptions.CookieName, string.Join(", ", Request.Cookies.Keys));
            return Unauthorized();
        }

        _logger.LogDebug("Cookie '{CookieName}' value: '{SessionId}'", _sessionOptions.CookieName, sessionId);

        // 🔹 НОВОЕ: Лог для отладки — сколько сессий в словаре
        if (_sessionService is InMemorySessionService inMemory)
        {
            _logger.LogDebug("Sessions in dictionary: {Count}", inMemory.GetSessionCountForDebug());
        }

        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session == null || session.IsExpired)
        {
            _logger.LogWarning("Session not found or expired: {SessionId}", sessionId);
            Response.Cookies.Delete(_sessionOptions.CookieName);
            return Unauthorized();
        }

        // 🔹 Ротация сессии
        var newSession = await _sessionService.RotateSessionAsync(session);
        if (newSession != null && newSession.SessionId != sessionId)
        {
            _logger.LogDebug("Session rotated: {OldId} → {NewId}", sessionId, newSession.SessionId);
            Response.Cookies.Append(
                _sessionOptions.CookieName,
                newSession.SessionId,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = false, // 🔹 НОВОЕ: отключено для dev, в prod вернуть Request.IsHttps
                    SameSite = SameSiteMode.Lax,
                    Expires = newSession.ExpiresAt,
                    Path = "/"
                });
        }

        _logger.LogDebug("Session valid for user: {UserId}", session.UserId);
        return Ok(new { authenticated = true, userId = session.UserId, expiresAt = session.ExpiresAt });
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
        {
            Response.Cookies.Delete(_sessionOptions.CookieName);
            return Unauthorized();
        }

        // 🔹 Обновление access_token при истечении
        if (session.IsAccessTokenExpired)
        {
            _logger.LogDebug("Access token expired, attempting refresh for user: {UserId}", session.UserId);
            var refreshed = await _keycloak.RefreshAccessTokenAsync(session.RefreshToken);
            if (refreshed != null)
            {
                session.AccessToken = refreshed.AccessToken;
                session.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn);
                await _sessionService.UpdateSessionAsync(session);
                _logger.LogDebug("Access token refreshed successfully");
            }
            else
            {
                _logger.LogWarning("Failed to refresh access token for user: {UserId}", session.UserId);
            }
        }

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
                try
                {
                    await _keycloak.RevokeTokenAsync(session.RefreshToken);
                    _logger.LogDebug("Refresh token revoked for user: {UserId}", session.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to revoke refresh token for user: {UserId}", session.UserId);
                }
                await _sessionService.DeleteSessionAsync(sessionId);
            }
            Response.Cookies.Delete(_sessionOptions.CookieName);
            _logger.LogInformation("User logged out, session deleted: {SessionId}", sessionId);
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
}

public record UserInfo(string Sub, string? PreferredUsername, string? Email);