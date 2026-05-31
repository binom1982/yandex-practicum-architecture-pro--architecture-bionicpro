using BionicproAuthService.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BionicproAuthService.Services;

public interface IAuthSessionService
{
    Task<AuthSession?> CreateSessionAsync(TokenResponse tokens, string userId);
    Task<AuthSession?> GetSessionAsync(string sessionId);
    Task<AuthSession?> RotateSessionAsync(AuthSession session);
    Task UpdateSessionAsync(AuthSession session);
    Task DeleteSessionAsync(string sessionId);
}

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

public class InMemorySessionService : IAuthSessionService
{
    private readonly ConcurrentDictionary<string, AuthSession> _sessions = new();
    private readonly Aes _aes;
    private readonly AuthSessionOptions _sessionOptions;
    // ✅ ИСПРАВЛЕНО: был ILogger<AuthController>, теперь правильный тип
    private readonly ILogger<InMemorySessionService> _logger;

    public InMemorySessionService(
        IOptions<SessionSecurityOptions> securityOpts,
        IOptions<AuthSessionOptions> sessionOpts,
        // ✅ ИСПРАВЛЕНО: тип логгера
        ILogger<InMemorySessionService> logger)
    {
        // 🔹 Настройка AES
        _aes = Aes.Create();
        _aes.Key = Encoding.UTF8.GetBytes(
            securityOpts.Value.EncryptionKey.PadRight(32).Substring(0, 32));
        _aes.IV = new byte[16]; // для dev-среды; в prod используйте случайный IV

        _sessionOptions = sessionOpts.Value;
        _logger = logger;

        // 🔹 НОВОЕ: Лог для проверки Singleton
        _logger.LogInformation("InMemorySessionService instance hash: {HashCode}", GetHashCode());
        var key = securityOpts.Value.EncryptionKey;
        _logger.LogInformation("Session service initialized. Encryption key length: {KeyLength}",
            key?.Length ?? 0);
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
            ExpiresAt = now.AddMinutes(_sessionOptions.SessionLifetimeMinutes)
        };

        _sessions[session.SessionId] = session;
        _logger.LogDebug("Session created: {SessionId}, User: {UserId}, Total sessions: {Count}",
            session.SessionId, userId, _sessions.Count);

        return Task.FromResult<AuthSession?>(session);
    }

    public async Task<AuthSession?> GetSessionAsync(string sessionId)
    {
        _logger.LogDebug("Looking up session: {SessionId}", sessionId);

        if (!_sessions.TryGetValue(sessionId, out var s))
        {
            // 🔹 НОВОЕ: Лог ключей для отладки (только в dev!)
            _logger.LogDebug("Available session keys: {Keys}",
                string.Join(", ", _sessions.Keys.Take(5))); // показываем первые 5

            _logger.LogWarning("Session NOT FOUND in dictionary. Available: {Count}", _sessions.Count);
            return null;
        }

        if (s.IsExpired)
        {
            _logger.LogWarning("Session expired: {SessionId}, ExpiredAt: {ExpiresAt}",
                sessionId, s.ExpiresAt);
            return null;
        }

        try
        {
            var decrypted = DecryptSession(s);
            _logger.LogDebug("Session decrypted successfully: {SessionId}", sessionId);
            return decrypted;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Failed to decrypt session {SessionId}. AccessToken prefix: {TokenPrefix}",
                sessionId, s.AccessToken?.Substring(0, Math.Min(20, s.AccessToken?.Length ?? 0)));
            return null;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic error decrypting session {SessionId}", sessionId);
            return null;
        }
    }

    public Task<AuthSession?> RotateSessionAsync(AuthSession session)
    {
        _sessions.TryRemove(session.SessionId, out _);

        var newSession = new AuthSession
        {
            UserId = session.UserId,
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            AccessTokenExpiresAt = session.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = session.RefreshTokenExpiresAt,
            ExpiresAt = session.ExpiresAt
        };
        _sessions[newSession.SessionId] = newSession;
        _logger.LogDebug("Session rotated: {OldId} → {NewId}", session.SessionId, newSession.SessionId);
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

    // 🔹 НОВОЕ: Метод для отладки (только для dev, не использовать в prod!)
    public int GetSessionCountForDebug() => _sessions.Count;

    // 🔹 Шифрование (стандартный Base64)
    private string Encrypt(string plain)
    {
        using var encryptor = _aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToBase64String(encrypted);
    }

    // 🔹 Дешифрование (стандартный Base64)
    private string Decrypt(string encrypted)
    {
        try
        {
            using var decryptor = _aes.CreateDecryptor();
            var bytes = Convert.FromBase64String(encrypted);
            var decrypted = decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Invalid Base64 in Decrypt(). Input prefix: {InputPrefix}",
                encrypted?.Substring(0, Math.Min(20, encrypted?.Length ?? 0)));
            throw;
        }
    }

    // 🔹 Возвращает сессию с расшифрованными токенами
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