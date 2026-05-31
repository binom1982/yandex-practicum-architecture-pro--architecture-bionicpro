using BionicproAuthService.Models;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
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

    // 🔹 Если шифруете в стандартный Base64:
    private string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = _aes.Encrypt(bytes, _key, _iv);
        return Convert.ToBase64String(encrypted);  // ← стандартный Base64
    }

    private string Decrypt(string encrypted)
    {
        try
        {
            var bytes = Convert.FromBase64String(encrypted);  // ← должно соответствовать Encrypt
            var decrypted = _aes.Decrypt(bytes, _key, _iv);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Failed to decrypt: invalid Base64 input='{Input}'",
                encrypted?.Substring(0, Math.Min(20, encrypted?.Length ?? 0)));
            return null;
        }
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