using BionicproAuthService.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BionicproAuthService.Services;

public interface IKeycloakClient
{
    string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge, string codeChallengeMethod);
    Task<TokenResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier);
    Task<TokenResponse?> RefreshAccessTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string token);
}

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
        $"{_options.PublicUrl}/realms/{_options.Realm}/protocol/openid-connect/auth" +  // ← PublicUrl для браузера
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
            new KeyValuePair<string, string>("redirect_uri", $"{_options.PublicUrl}/auth/callback"), // ← Public для редиректа
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
        });

        // ← InternalUrl для сервер-сервер вызова
        var resp = await _http.PostAsync(
            $"{_options.InternalUrl}/realms/{_options.Realm}/protocol/openid-connect/token", content);

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
            $"{_options.InternalUrl}/realms/{_options.Realm}/protocol/openid-connect/token", content);

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
        await _http.PostAsync($"{_options.InternalUrl}/realms/{_options.Realm}/protocol/openid-connect/revoke", content);
    }
}