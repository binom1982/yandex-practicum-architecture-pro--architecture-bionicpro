using System.Text.Json.Serialization;

namespace BionicproAuthService.Models;

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
    /// Внутренний URL Keycloak (для вызовов из контейнера в контейнер)
    /// </summary>
    public string InternalUrl { get; set; } = "http://keycloak:8080";

    /// <summary>
    /// Публичный URL Keycloak (виден браузеру пользователя)
    /// </summary>
    public string PublicUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Куда Keycloak редиректит после логина
    /// </summary>
    public string CallbackUrl { get; set; } = "http://localhost:3000";

    public string Realm { get; set; } = "reports-realm";
    public string ClientId { get; set; } = "reports-api";
    public string ClientSecret { get; set; } = string.Empty;
}

public record TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_expires_in")]
    public int RefreshExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";
}

public record UserInfo(
    string Sub,
    string? PreferredUsername,
    string? Email,
    string? Name,
    string? GivenName,
    string? FamilyName);
