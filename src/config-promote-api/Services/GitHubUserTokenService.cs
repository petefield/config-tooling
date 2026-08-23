using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using config_promote_api.Models;

namespace config_promote_api.Services;

internal sealed class GitHubUserTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly GitHubAppOptions _options;
    private readonly TokenProtector _tokenProtector;

    public GitHubUserTokenService(IHttpClientFactory httpClientFactory, GitHubAppOptions options, TokenProtector tokenProtector)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options;
        _tokenProtector = tokenProtector;
    }

    public string BuildAuthorizationUrl(string appOrigin)
    {
        ValidateOrigin(appOrigin);

        var state = _tokenProtector.Protect(new GitHubAuthState
        {
            AppOrigin = appOrigin,
            IssuedAtUtc = DateTimeOffset.UtcNow
        });

        return $"https://github.com/login/oauth/authorize?client_id={Uri.EscapeDataString(_options.ClientId)}&redirect_uri={Uri.EscapeDataString(_options.CallbackUrl)}&state={Uri.EscapeDataString(state)}";
    }

    public async Task<AuthCompletion> ExchangeCodeAsync(string code, string stateToken)
    {
        var state = ReadState(stateToken);

        if (DateTimeOffset.UtcNow - state.IssuedAtUtc > TimeSpan.FromMinutes(10))
        {
            throw new AuthenticationRequiredException("The GitHub sign-in request expired. Start the sign-in flow again.");
        }

        var tokenResponse = await ExchangeTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = _options.CallbackUrl
        });

        return new AuthCompletion
        {
            AppOrigin = state.AppOrigin,
            AuthSession = CreateAuthSessionToken(tokenResponse)
        };
    }

    public async Task<AuthenticatedGitHubSession> GetAuthenticatedSessionAsync(string authSessionToken)
    {
        var session = _tokenProtector.Unprotect<GitHubAuthSession>(authSessionToken);

        if (session.AccessTokenExpiresAtUtc is null || session.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return new AuthenticatedGitHubSession
            {
                AccessToken = session.AccessToken,
                AuthSession = authSessionToken
            };
        }

        if (string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            throw new AuthenticationRequiredException("Your GitHub sign-in session expired. Sign in again and retry the promotion.");
        }

        if (session.RefreshTokenExpiresAtUtc is not null && session.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new AuthenticationRequiredException("Your GitHub refresh token expired. Sign in again and retry the promotion.");
        }

        var tokenResponse = await ExchangeTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.RefreshToken
        });

        return new AuthenticatedGitHubSession
        {
            AccessToken = tokenResponse.AccessToken!,
            AuthSession = CreateAuthSessionToken(tokenResponse)
        };
    }

    public string TryGetAppOrigin(string stateToken)
    {
        try
        {
            return ReadState(stateToken).AppOrigin;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<GitHubTokenResponse> ExchangeTokenAsync(IReadOnlyDictionary<string, string> formValues)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("config-promote-api", "1.0"));

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new AuthenticationRequiredException($"GitHub sign-in failed with {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(JsonOptions)
            ?? throw new AuthenticationRequiredException("GitHub returned an empty token response.");

        if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
        {
            throw new AuthenticationRequiredException(
                string.IsNullOrWhiteSpace(tokenResponse.ErrorDescription)
                    ? $"GitHub sign-in failed: {tokenResponse.Error}."
                    : $"GitHub sign-in failed: {tokenResponse.ErrorDescription}.");
        }

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new AuthenticationRequiredException("GitHub did not return a usable user access token.");
        }

        return tokenResponse;
    }

    private string CreateAuthSessionToken(GitHubTokenResponse tokenResponse)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new GitHubAuthSession
        {
            AccessToken = tokenResponse.AccessToken!,
            AccessTokenExpiresAtUtc = tokenResponse.ExpiresIn is > 0 ? now.AddSeconds(tokenResponse.ExpiresIn.Value) : null,
            RefreshToken = tokenResponse.RefreshToken,
            RefreshTokenExpiresAtUtc = tokenResponse.RefreshTokenExpiresIn is > 0 ? now.AddSeconds(tokenResponse.RefreshTokenExpiresIn.Value) : null
        };

        return _tokenProtector.Protect(session);
    }

    private GitHubAuthState ReadState(string stateToken) =>
        _tokenProtector.Unprotect<GitHubAuthState>(stateToken);

    private static void ValidateOrigin(string appOrigin)
    {
        if (!Uri.TryCreate(appOrigin, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Scheme) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("The browser app origin is not a valid absolute URL.");
        }
    }

    internal sealed record AuthCompletion
    {
        public required string AppOrigin { get; init; }

        public required string AuthSession { get; init; }
    }

    internal sealed record AuthenticatedGitHubSession
    {
        public required string AccessToken { get; init; }

        public required string AuthSession { get; init; }
    }

    private sealed record GitHubTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }

        [JsonPropertyName("refresh_token_expires_in")]
        public int? RefreshTokenExpiresIn { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
    }
}
