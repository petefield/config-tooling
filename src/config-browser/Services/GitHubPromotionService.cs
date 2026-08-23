using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using config_browser.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace config_browser.Services;

internal sealed class GitHubPromotionService
{
    private const string AuthSessionStorageKey = "config-browser.github-auth-session";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly string? _promoteApiBaseUrl;

    public GitHubPromotionService(HttpClient httpClient, IJSRuntime jsRuntime, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _promoteApiBaseUrl = configuration["PromoteApiBaseUrl"];
    }

    public async Task<bool> HasAuthenticatedSessionAsync() =>
        !string.IsNullOrWhiteSpace(await GetAuthSessionAsync());

    public async Task SignInAsync(string appOrigin)
    {
        var apiBaseUrl = GetPromoteApiBaseUrl();
        var signInUrl = $"{apiBaseUrl}/api/auth/github/start?appOrigin={Uri.EscapeDataString(appOrigin)}";
        var expectedOrigin = new Uri(apiBaseUrl).GetLeftPart(UriPartial.Authority);

        var authSession = await _jsRuntime.InvokeAsync<string>(
            "githubAuth.signIn",
            signInUrl,
            expectedOrigin);

        await SetAuthSessionAsync(authSession);
    }

    public async Task SignOutAsync() =>
        await ClearAuthSessionAsync();

    public async Task<PromotionResult> PromoteAsync(
        GitHubRepository repository,
        ConfigCatalogEntry entry,
        string fileContents)
    {
        var authSession = await GetAuthSessionAsync()
            ?? throw new InvalidOperationException("Sign in with GitHub before promoting this config.");

        var request = new PromoteConfigRequest
        {
            AuthSession = authSession,
            Repository = repository,
            Entry = entry,
            FileContents = fileContents
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"{GetPromoteApiBaseUrl()}/api/promote",
            request,
            JsonOptions);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await ClearAuthSessionAsync();
            throw new InvalidOperationException("Your GitHub sign-in session expired or was rejected. Sign in again and retry the promotion.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response));
        }

        var result = await response.Content.ReadFromJsonAsync<PromotionResult>(JsonOptions)
            ?? throw new InvalidOperationException("The promote API returned an empty response.");

        if (!string.IsNullOrWhiteSpace(result.AuthSession))
        {
            await SetAuthSessionAsync(result.AuthSession);
        }

        return result;
    }

    private string GetPromoteApiBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(_promoteApiBaseUrl))
        {
            throw new InvalidOperationException(
                "The browser app is missing PromoteApiBaseUrl. Set src/config-browser/wwwroot/appsettings.json to the Azure Functions app URL before using Promote.");
        }

        return _promoteApiBaseUrl.TrimEnd('/');
    }

    private async Task<string?> GetAuthSessionAsync()
    {
        var value = await _jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", AuthSessionStorageKey);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private Task SetAuthSessionAsync(string authSession) =>
        _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", AuthSessionStorageKey, authSession.Trim()).AsTask();

    private Task ClearAuthSessionAsync() =>
        _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", AuthSessionStorageKey).AsTask();

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var error = await response.Content.ReadFromJsonAsync<PromoteApiError>(JsonOptions);

        if (!string.IsNullOrWhiteSpace(error?.Message))
        {
            return error.Message.Trim();
        }

        return $"Promote API returned {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    private sealed record PromoteConfigRequest
    {
        public required string AuthSession { get; init; }

        public required GitHubRepository Repository { get; init; }

        public required ConfigCatalogEntry Entry { get; init; }

        public required string FileContents { get; init; }
    }

    internal sealed record PromotionResult
    {
        public required string BranchName { get; init; }

        public required int PullRequestNumber { get; init; }

        public required string PullRequestUrl { get; init; }

        public string? AuthSession { get; init; }
    }

    private sealed record PromoteApiError
    {
        public string? Message { get; init; }
    }
}
