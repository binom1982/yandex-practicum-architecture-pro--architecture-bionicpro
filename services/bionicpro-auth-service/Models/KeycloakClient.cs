using BionicproAuthService.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BionicproAuthService.Models;

// ─────────────────────────────────────────────────────────────
// Опции конфигурации
// ─────────────────────────────────────────────────────────────
public record KeycloakOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public string Realm { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────
// Модели
// ─────────────────────────────────────────────────────────────
public record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_expires_in")] int RefreshExpiresIn,
    [property: JsonPropertyName("token_type")] string TokenType = "Bearer",
    [property: JsonPropertyName("scope")] string? Scope = null);

public record UserInfo(
    string Sub,
    string? PreferredUsername,
    string? Email,
    string? Name,
    string? GivenName,
    string? FamilyName);

// ─────────────────────────────────────────────────────────────
// Интерфейс
// ─────────────────────────────────────────────────────────────
public interface IKeycloakClient
{
    Task<TokenResponse?> AuthenticateAsync(string username, string password);
    Task<TokenResponse?> RefreshTokenAsync(string refreshToken);
    Task<UserInfo?> GetUserInfoAsync(string accessToken);
    Task<bool> LogoutAsync(string refreshToken);
}

// ─────────────────────────────────────────────────────────────
// Реализация
// ─────────────────────────────────────────────────────────────
public class KeycloakClient : IKeycloakClient
{
    private readonly HttpClient _httpClient;
    private readonly KeycloakOptions _options;

    public KeycloakClient(IOptions<KeycloakOptions> options, HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
    }

    public async Task<TokenResponse?> AuthenticateAsync(string username, string password)
    {
        var endpoint = $"/realms/{_options.Realm}/protocol/openid-connect/token";

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password),
            new KeyValuePair<string, string>("scope", "openid profile email")
        });

        var response = await _httpClient.PostAsync(endpoint, content);
        var rawContent = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"[Keycloak] Raw response: {rawContent}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Keycloak] Error: {response.StatusCode}");
            return null;
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();

        Console.WriteLine($"[Keycloak] Parsed AccessToken: {(tokens?.AccessToken?.Substring(0, 20) ?? "null")}...");

        return tokens;
    }

    public async Task<TokenResponse?> RefreshTokenAsync(string refreshToken)
    {
        var endpoint = $"/realms/{_options.Realm}/protocol/openid-connect/token";

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        var response = await _httpClient.PostAsync(endpoint, content);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<TokenResponse>();
    }

    public async Task<UserInfo?> GetUserInfoAsync(string accessToken)
    {
        var endpoint = $"/realms/{_options.Realm}/protocol/openid-connect/userinfo";

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var response = await _httpClient.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadFromJsonAsync<UserInfo>();
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<bool> LogoutAsync(string refreshToken)
    {
        var endpoint = $"/realms/{_options.Realm}/protocol/openid-connect/logout";

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        var response = await _httpClient.PostAsync(endpoint, content);
        return response.IsSuccessStatusCode;
    }
}