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
    private readonly ILogger<KeycloakClient> _logger;  // ← Добавьте поле

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public KeycloakClient(
        HttpClient http,
        IOptions<KeycloakOptions> options,
        ILogger<KeycloakClient> logger)  // ← Добавьте параметр в конструктор
    {
        _http = http;
        _options = options.Value;
        _logger = logger;  // ← Сохраните логгер
    }

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge, string codeChallengeMethod) =>
        $"{_options.PublicUrl}/realms/{_options.Realm}/protocol/openid-connect/auth" +
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
            new KeyValuePair<string, string>("redirect_uri", $"{_options.CallbackUrl}/auth/callback"),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
        });

        _logger.LogInformation("Exchanging code for tokens. Code: {CodePrefix}..., Redirect: {RedirectUri}",
            code.Substring(0, Math.Min(8, code.Length)),
            $"{_options.PublicUrl}/auth/callback");

        var resp = await _http.PostAsync(
            $"{_options.InternalUrl}/realms/{_options.Realm}/protocol/openid-connect/token", content);

        var responseBody = await resp.Content.ReadAsStringAsync();

        // 🔹 Логирование ответа — именно здесь!
        _logger.LogWarning("Keycloak token response: {StatusCode} - {ResponseBody}",
            resp.StatusCode, responseBody);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Token exchange failed with status {StatusCode}", resp.StatusCode);
            return null;
        }

        return JsonSerializer.Deserialize<TokenResponse>(responseBody, _jsonOptions);
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

        var responseBody = await resp.Content.ReadAsStringAsync();
        _logger.LogDebug("Refresh token response: {StatusCode} - {ResponseBody}", resp.StatusCode, responseBody);

        if (!resp.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<TokenResponse>(responseBody, _jsonOptions);
    }

    public async Task RevokeTokenAsync(string token)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", token),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
        });

        var resp = await _http.PostAsync(
            $"{_options.InternalUrl}/realms/{_options.Realm}/protocol/openid-connect/revoke", content);

        _logger.LogDebug("Token revocation response: {StatusCode}", resp.StatusCode);
    }
}