using BionicproAuthService.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BionicproAuthService.Services;

public record AuthRequest(string Username, string Password);

public class AuthSessionOptions
{
    public string CookieName { get; init; } = "bionicpro_session";
    public int SessionLifetimeMinutes { get; init; } = 60;
    public int AccessTokenLifetimeMinutes { get; init; } = 2;
}

public record AuthSessionData(
    string SessionId,
    string AccessToken,
    string RefreshToken,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string UserId);

public interface IAuthSessionService
{
    Task<AuthSessionData?> CreateSessionAsync(TokenResponse tokens, string userId);
    Task<AuthSessionData?> GetSessionAsync(string sessionId);
    Task<bool> RotateSessionAsync(string sessionId);
    Task<bool> DeleteSessionAsync(string sessionId);
    Task<bool> RefreshAccessTokenAsync(string sessionId);
}

public class AuthSessionService : IAuthSessionService
{
    private readonly IDistributedCache _cache;
    private readonly IKeycloakClient _keycloakClient;
    private readonly AuthSessionOptions _options;
    private readonly TimeSpan _sessionTtl;
    private readonly TimeSpan _accessTtl;

    public AuthSessionService(
        IDistributedCache cache,
        IKeycloakClient keycloakClient,
        IOptions<AuthSessionOptions> options)
    {
        _cache = cache;
        _keycloakClient = keycloakClient;
        _options = options.Value;
        _sessionTtl = TimeSpan.FromMinutes(_options.SessionLifetimeMinutes);
        _accessTtl = TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes);
    }

    public async Task<AuthSessionData?> CreateSessionAsync(TokenResponse tokens, string userId)
    {
        try
        {
            Console.WriteLine($"[Session] Creating session for userId: {userId}");

            var sessionId = GenerateSecureId();
            Console.WriteLine($"[Session] Generated sessionId: {sessionId}");

            var session = new AuthSessionData(
                SessionId: sessionId,
                UserId: userId,
                AccessToken: Encrypt(tokens.AccessToken),      // ← Проверьте Encrypt()
                RefreshToken: Encrypt(tokens.RefreshToken),    // ← Проверьте Encrypt()
                CreatedAt: DateTime.UtcNow,
                ExpiresAt: DateTime.UtcNow.AddMinutes(_options.SessionLifetimeMinutes)
            );

            Console.WriteLine($"[Session] Serialized session, AccessToken length: {tokens.AccessToken?.Length}");

            await SaveSessionAsync(session);

            // 🔍 Проверка: действительно ли сохранилось?
            var key = $"session:{sessionId}";
            var verify = await _cache.GetAsync(key);
            Console.WriteLine($"[Session] Verify save for key '{key}': {(verify != null ? "OK" : "FAILED")}");

            return session;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Session] ERROR in CreateSessionAsync: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[Session] Stack: {ex.StackTrace}");
            throw; // ← Не глотать исключение!
        }
    }

    public async Task<AuthSessionData?> GetSessionAsync(string sessionId)
    {
        var data = await _cache.GetAsync($"session:{sessionId}");
        if (data == null || data.Length == 0) return null;

        var session = JsonSerializer.Deserialize<AuthSessionData>(data);
        if (session?.ExpiresAt < DateTime.UtcNow)
        {
            await DeleteSessionAsync(sessionId);
            return null;
        }
        return session;
    }

    public async Task<bool> RotateSessionAsync(string sessionId)
    {
        var session = await GetSessionAsync(sessionId);
        if (session == null) return false;

        await DeleteSessionAsync(sessionId);

        var newSession = session with
        {
            SessionId = GenerateSecureId(),
            ExpiresAt = DateTime.UtcNow.Add(_sessionTtl)
        };

        await SaveSessionAsync(newSession);
        return true;
    }

    public async Task<bool> RefreshAccessTokenAsync(string sessionId)
    {
        var session = await GetSessionAsync(sessionId);
        if (session == null) return false;

        var refreshToken = Decrypt(session.RefreshToken);
        var newTokens = await _keycloakClient.RefreshTokenAsync(refreshToken);
        if (newTokens == null) return false;

        var updatedSession = session with
        {
            AccessToken = newTokens.AccessToken,
            RefreshToken = Encrypt(newTokens.RefreshToken)
        };

        await SaveSessionAsync(updatedSession);
        return true;
    }

    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        await _cache.RemoveAsync($"session:{sessionId}");
        return true;
    }

    private async Task SaveSessionAsync(AuthSessionData session)
    {
        try
        {
            var key = $"session:{session.SessionId}";
            Console.WriteLine($"[Redis] Saving to key: {key}");

            var data = JsonSerializer.SerializeToUtf8Bytes(session);
            Console.WriteLine($"[Redis] Serialized data length: {data.Length}");

            await _cache.SetAsync(key, data, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.SessionLifetimeMinutes)
            });

            Console.WriteLine($"[Redis] SetAsync completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"[Redis] Inner: {ex.InnerException.Message}");
            throw;
        }
    }

    private string GenerateSecureId()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        try
        {
            // Ваша логика шифрования

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Encrypt] Error: {ex.Message}");
            throw;
        }
    }

    private string Decrypt(string encrypted)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
    }
}