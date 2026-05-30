using BionicproAuthService.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BionicproAuthService.Models;


// ─────────────────────────────────────────────────────────────
// Модели
// ─────────────────────────────────────────────────────────────
//public record TokenResponse(
//    [property: JsonPropertyName("access_token")] string AccessToken,
//    [property: JsonPropertyName("refresh_token")] string RefreshToken,
//    [property: JsonPropertyName("id_token")] string? IdToken,
//    [property: JsonPropertyName("expires_in")] int ExpiresIn,
//    [property: JsonPropertyName("refresh_expires_in")] int RefreshExpiresIn,
//    [property: JsonPropertyName("token_type")] string TokenType = "Bearer",
//    [property: JsonPropertyName("scope")] string? Scope = null);

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

// ─────────────────────────────────────────────────────────────
// Интерфейс
// ─────────────────────────────────────────────────────────────
public interface IKeycloakClient
{
    string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge, string codeChallengeMethod);
    Task<TokenResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier);
    Task<TokenResponse?> RefreshAccessTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string token);
}

// ─────────────────────────────────────────────────────────────
// Реализация
// ─────────────────────────────────────────────────────────────
public class KeycloakClient : IKeycloakClient
{
    private readonly HttpClient _http;
    private readonly KeycloakOptions _options;

    public KeycloakClient(HttpClient http, IOptions<KeycloakOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge, string codeChallengeMethod) =>
        $"{_options.AuthUrl}/realms/{_options.Realm}/protocol/openid-connect/auth" +
        $"?client_id={_options.ClientId}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&response_type=code" +
        $"&scope=openid" +
        $"&state={state}" +
        $"&code_challenge={codeChallenge}" +
        $"&code_challenge_method={codeChallengeMethod}";

    public async Task<TokenResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", $"{_options.BaseUrl}/auth/callback"),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
        });

        var resp = await _http.PostAsync(
            $"{_options.AuthUrl}/realms/{_options.Realm}/protocol/openid-connect/token", content);

        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TokenResponse>(json);
    }

    public async Task<TokenResponse?> RefreshAccessTokenAsync(string refreshToken)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
        });

        var resp = await _http.PostAsync(
            $"{_options.AuthUrl}/realms/{_options.Realm}/protocol/openid-connect/token", content);

        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TokenResponse>(json);
    }

    public async Task RevokeTokenAsync(string token)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", token),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
        });
        await _http.PostAsync($"{_options.AuthUrl}/realms/{_options.Realm}/protocol/openid-connect/revoke", content);
    }
}