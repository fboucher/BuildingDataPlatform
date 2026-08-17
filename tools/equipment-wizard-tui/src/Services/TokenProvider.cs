using System.Net.Http.Headers;
using System.Text.Json;

namespace EquipmentWizardTui.Services;

public sealed class TokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly EcoStruxureApiOptions _options;
    private string? _cachedToken;
    private DateTime _tokenExpiry;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public TokenProvider(HttpClient httpClient, EcoStruxureApiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _tokenExpiry = DateTime.MinValue;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedToken;
            }

            ValidateOptions();

            using var requestContent = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("scope", _options.Scope),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            ]);

            var tokenUrl = $"https://login.microsoftonline.com/{_options.TenantId}/oauth2/v2.0/token";
            using var response = await _httpClient.PostAsync(tokenUrl, requestContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var tokenResponse = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Failed to parse AAD token response.");

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("AAD token response did not contain access_token.");
            }

            var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600;
            var safeTtl = Math.Max(60, expiresInSeconds - 60);
            _tokenExpiry = DateTime.UtcNow.AddSeconds(safeTtl);
            _cachedToken = tokenResponse.AccessToken;

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task AddBearerTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.TenantId) ||
            string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.Scope))
        {
            throw new InvalidOperationException(
                "Missing EcoStruxure API credentials. Check your .env file for TENANT_ID, CLIENT_ID, CLIENT_SECRET, and CREDS_SCOPES.");
        }
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
