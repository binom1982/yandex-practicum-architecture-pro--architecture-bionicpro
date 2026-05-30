using BionicproAuthService.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BionicproAuthService.Services;

public class SessionSecurityOptions
{
    /// <summary>
    /// Ключ шифрования для токенов (мин. 32 символа для AES-256)
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// Алгоритм шифрования (по умолчанию AES)
    /// </summary>
    public string EncryptionAlgorithm { get; set; } = "AES";

    /// <summary>
    /// Требовать HTTPS для cookie (в prod = true)
    /// </summary>
    public bool RequireSecureCookie { get; set; } = false;
}

public class AuthSessionOptions
{
    /// <summary>
    /// Имя cookie для сессии
    /// </summary>
    public string CookieName { get; set; } = "bionicpro_session";

    /// <summary>
    /// Время жизни сессии в минутах (по умолчанию 24 часа)
    /// </summary>
    public int SessionLifetimeMinutes { get; set; } = 1440;

    /// <summary>
    /// Флаг: продлевать сессию при каждом запросе
    /// </summary>
    public bool SlidingExpiration { get; set; } = true;
}

public class KeycloakOptions
{
    /// <summary>
    /// Базовый URL Keycloak (для внутренних вызовов)
    /// </summary>
    public string AuthUrl { get; set; } = "http://keycloak:8080";

    /// <summary>
    ///realm Keycloak
    /// </summary>
    public string Realm { get; set; } = "reports-realm";

    /// <summary>
    /// Client ID для этого сервиса
    /// </summary>
    public string ClientId { get; set; } = "reports-api";

    /// <summary>
    /// Client Secret для confidential client
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Публичный базовый URL для редиректов (браузер видит этот адрес)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";
}



public interface IAuthSessionService
{
    Task<AuthSession?> CreateSessionAsync(TokenResponse tokens, string userId);
    Task<AuthSession?> GetSessionAsync(string sessionId);
    Task<AuthSession?> RotateSessionAsync(AuthSession session);
    Task UpdateSessionAsync(AuthSession session);
    Task DeleteSessionAsync(string sessionId);
}

// Models/AuthSession.cs
public class AuthSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsAccessTokenExpired => DateTime.UtcNow > AccessTokenExpiresAt;
}

// Services/InMemorySessionService.cs
public class InMemorySessionService : IAuthSessionService
{
    private readonly ConcurrentDictionary<string, AuthSession> _sessions = new();
    private readonly Aes _aes;
    private readonly AuthSessionOptions _sessionOptions; // ← Добавьте это поле

    public InMemorySessionService(
        IOptions<SessionSecurityOptions> securityOpts,  // для шифрования
        IOptions<AuthSessionOptions> sessionOpts)       // ← Добавьте этот параметр
    {
        _aes = Aes.Create();
        _aes.Key = Encoding.UTF8.GetBytes(
            securityOpts.Value.EncryptionKey.PadRight(32).Substring(0, 32));
        _aes.IV = new byte[16];

        _sessionOptions = sessionOpts.Value; // ← Сохраните опции
    }

    public Task<AuthSession?> CreateSessionAsync(TokenResponse tokens, string userId)
    {
        var now = DateTime.UtcNow;
        var session = new AuthSession
        {
            UserId = userId,
            AccessToken = Encrypt(tokens.AccessToken),
            RefreshToken = Encrypt(tokens.RefreshToken),
            AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn),
            RefreshTokenExpiresAt = now.AddSeconds(tokens.RefreshExpiresIn),
            ExpiresAt = now.AddMinutes(_sessionOptions.SessionLifetimeMinutes) // ✅ теперь работает
        };
        _sessions[session.SessionId] = session;
        return Task.FromResult<AuthSession?>(session);
    }

    public Task<AuthSession?> GetSessionAsync(string sessionId) =>
        Task.FromResult(_sessions.TryGetValue(sessionId, out var s) && !s.IsExpired ? DecryptSession(s) : null);

    public Task<AuthSession?> RotateSessionAsync(AuthSession session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        var newSession = new AuthSession
        {
            UserId = session.UserId,
            AccessToken = session.AccessToken, // уже зашифрованы
            RefreshToken = session.RefreshToken,
            AccessTokenExpiresAt = session.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = session.RefreshTokenExpiresAt,
            ExpiresAt = session.ExpiresAt
        };
        _sessions[newSession.SessionId] = newSession;
        return Task.FromResult<AuthSession?>(newSession);
    }

    public Task UpdateSessionAsync(AuthSession session)
    {
        _sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private string Encrypt(string plain)
    {
        using var encryptor = _aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToBase64String(encrypted);
    }

    private string Decrypt(string encrypted)
    {
        using var decryptor = _aes.CreateDecryptor();
        var bytes = Convert.FromBase64String(encrypted);
        var decrypted = decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return Encoding.UTF8.GetString(decrypted);
    }

    private AuthSession DecryptSession(AuthSession s) => new()
    {
        SessionId = s.SessionId,
        UserId = s.UserId,
        AccessToken = Decrypt(s.AccessToken),
        RefreshToken = Decrypt(s.RefreshToken),
        AccessTokenExpiresAt = s.AccessTokenExpiresAt,
        RefreshTokenExpiresAt = s.RefreshTokenExpiresAt,
        ExpiresAt = s.ExpiresAt
    };
}