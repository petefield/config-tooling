using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using config_promote_api.Models;

namespace config_promote_api.Services;

internal sealed class GitHubRepositoryPromotionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GitHubRepositoryPromotionService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<PromotionResult> PromoteAsync(
        string accessToken,
        GitHubRepository repository,
        ConfigCatalogEntry entry,
        string fileContents)
    {
        var targetEnvironment = GetPromotionTargetEnvironment(entry);
        var destinationPath = BuildDestinationSourcePath(entry.SourceFile, targetEnvironment);
        var branchName = BuildBranchName(entry, targetEnvironment);
        var title = $"Promote {entry.FileName} for {entry.Tenant} from {entry.Environment.ToUpperInvariant()} to {targetEnvironment.ToUpperInvariant()}";

        using var getBaseBranchRequest = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/git/ref/heads/{repository.BaseBranch}",
            accessToken);
        var baseBranch = await SendAsync<GitHubReferenceResponse>(getBaseBranchRequest);

        using var createBranchRequest = CreateGitHubRequest(
            HttpMethod.Post,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/git/refs",
            accessToken,
            new
            {
                @ref = $"refs/heads/{branchName}",
                sha = baseBranch.Object.Sha
            });
        await SendAsync<object>(createBranchRequest);

        var destinationSha = await GetExistingContentShaAsync(accessToken, repository, repository.BaseBranch, destinationPath);

        using var updateContentRequest = CreateGitHubRequest(
            HttpMethod.Put,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/contents/{BuildApiPath(destinationPath)}",
            accessToken,
            new GitHubCreateOrUpdateContentRequest
            {
                Message = title,
                Branch = branchName,
                Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContents)),
                Sha = destinationSha
            });
        await SendAsync<object>(updateContentRequest);

        using var createPullRequestRequest = CreateGitHubRequest(
            HttpMethod.Post,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/pulls",
            accessToken,
            new GitHubCreatePullRequestRequest
            {
                Title = title,
                Head = branchName,
                Base = repository.BaseBranch,
                Body = BuildPullRequestBody(entry, targetEnvironment, destinationPath)
            });
        var pullRequest = await SendAsync<GitHubPullRequestResponse>(createPullRequestRequest);

        return new PromotionResult
        {
            BranchName = branchName,
            PullRequestNumber = pullRequest.Number,
            PullRequestUrl = pullRequest.HtmlUrl
        };
    }

    private async Task<string?> GetExistingContentShaAsync(string accessToken, GitHubRepository repository, string branchName, string path)
    {
        using var request = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/contents/{BuildApiPath(path)}?ref={Uri.EscapeDataString(branchName)}",
            accessToken);

        using var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadGitHubErrorAsync(response));
        }

        var content = await response.Content.ReadFromJsonAsync<GitHubContentResponse>(JsonOptions);
        return content?.Sha;
    }

    private HttpRequestMessage CreateGitHubRequest(HttpMethod method, string uri, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("config-promote-api", "1.0"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadGitHubErrorAsync(response));
        }

        if (typeof(T) == typeof(object))
        {
            return (T)(object)new object();
        }

        var content = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return content ?? throw new InvalidOperationException("GitHub returned an unexpected empty response.");
    }

    private static async Task<string> ReadGitHubErrorAsync(HttpResponseMessage response)
    {
        var error = await response.Content.ReadFromJsonAsync<GitHubErrorResponse>(JsonOptions);
        var details = error?.Errors is { Count: > 0 }
            ? $" {string.Join(" ", error.Errors.Where(static item => !string.IsNullOrWhiteSpace(item.Message)).Select(static item => item.Message?.Trim() ?? string.Empty))}"
            : string.Empty;

        return string.IsNullOrWhiteSpace(error?.Message)
            ? $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}."
            : $"{error.Message.Trim()}{details}";
    }

    private static string BuildDestinationSourcePath(string sourceFile, string targetEnvironment)
    {
        var segments = sourceFile
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 4 || !string.Equals(segments[0], "configs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The source file path '{sourceFile}' is not in the expected configs/<tenant>/<environment>/... format.");
        }

        segments[2] = targetEnvironment;
        return string.Join('/', segments);
    }

    private static string BuildBranchName(ConfigCatalogEntry entry, string targetEnvironment)
    {
        var slug = new string(entry.FileName
            .ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');

        return $"promote/{entry.Tenant.ToLowerInvariant()}-{slug}-{entry.Environment.ToLowerInvariant()}-to-{targetEnvironment}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static string BuildApiPath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string BuildPullRequestBody(ConfigCatalogEntry entry, string targetEnvironment, string destinationPath) =>
        $"""
        Promote `{entry.FileName}` for tenant `{entry.Tenant}` from `{entry.Environment}` to `{targetEnvironment}`.

        - Source file: `{entry.SourceFile.Replace('\\', '/')}`
        - Target file: `{destinationPath}`
        - Contact type: `{entry.ContactType}`
        - Channel: `{entry.Channel}`
        """;

    private static string GetPromotionTargetEnvironment(ConfigCatalogEntry entry) =>
        string.Equals(entry.Environment, "dev", StringComparison.OrdinalIgnoreCase) ? "uat" : "prd";

    internal sealed record PromotionResult
    {
        public required string BranchName { get; init; }

        public required int PullRequestNumber { get; init; }

        public required string PullRequestUrl { get; init; }
    }

    private sealed record GitHubReferenceResponse
    {
        public required GitHubReferenceObject Object { get; init; }
    }

    private sealed record GitHubReferenceObject
    {
        public required string Sha { get; init; }
    }

    private sealed record GitHubContentResponse
    {
        public required string Sha { get; init; }
    }

    private sealed record GitHubCreateOrUpdateContentRequest
    {
        public required string Message { get; init; }

        public required string Content { get; init; }

        public required string Branch { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sha { get; init; }
    }

    private sealed record GitHubCreatePullRequestRequest
    {
        public required string Title { get; init; }

        public required string Head { get; init; }

        public required string Base { get; init; }

        public required string Body { get; init; }
    }

    private sealed record GitHubPullRequestResponse
    {
        public required int Number { get; init; }

        [JsonPropertyName("html_url")]
        public required string HtmlUrl { get; init; }
    }

    private sealed record GitHubErrorResponse
    {
        public string? Message { get; init; }

        public List<GitHubErrorItem> Errors { get; init; } = [];
    }

    private sealed record GitHubErrorItem
    {
        public string? Message { get; init; }
    }
}
