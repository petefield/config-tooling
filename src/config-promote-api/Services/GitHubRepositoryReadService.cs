using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using config_promote_api.Models;

namespace config_promote_api.Services;

internal sealed class GitHubRepositoryReadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GitHubRepositoryReadService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<ConfigCatalogResponse> GetCatalogAsync(GitHubRepository repository, string accessToken)
    {
        var tree = await GetRepositoryTreeAsync(repository, accessToken);
        var sourceFiles = tree.Tree
            .Where(static item => string.Equals(item.Type, "blob", StringComparison.OrdinalIgnoreCase))
            .Where(item => IsConfigFile(item.Path))
            .OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var entries = new List<ConfigCatalogItem>(sourceFiles.Length);

        foreach (var sourceFile in sourceFiles)
        {
            var rawContent = await GetRawConfigAsync(repository, sourceFile.Path, accessToken);
            var metadata = ParseConfigMetadata(rawContent);

            entries.Add(new ConfigCatalogItem
            {
                OutputFile = BuildOutputFilePath(sourceFile.Path),
                SourceFile = sourceFile.Path,
                ContactType = string.IsNullOrWhiteSpace(metadata.Trigger) ? "—" : metadata.Trigger,
                Channel = string.IsNullOrWhiteSpace(metadata.Channel) ? "—" : metadata.Channel
            });
        }

        return new ConfigCatalogResponse
        {
            GeneratedAtUtc = await GetLatestCommitDateAsync(repository, accessToken),
            Entries = entries
        };
    }

    public async Task<string> GetRawConfigByOutputFileAsync(GitHubRepository repository, string outputFile, string accessToken)
    {
        var sourceFile = BuildSourceFilePath(outputFile);
        return await GetRawConfigAsync(repository, sourceFile, accessToken);
    }

    public async Task<IReadOnlyList<GitModification>> GetHistoryAsync(GitHubRepository repository, string sourceFile, string accessToken)
    {
        using var request = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/commits?sha={Uri.EscapeDataString(repository.BaseBranch)}&path={Uri.EscapeDataString(sourceFile)}&per_page=5",
            accessToken);
        var commits = await SendAsync<List<GitHubCommitResponse>>(request);

        return commits
            .Select(MapToGitModification)
            .ToArray();
    }

    private async Task<GitHubTreeResponse> GetRepositoryTreeAsync(GitHubRepository repository, string accessToken)
    {
        using var request = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/git/trees/{Uri.EscapeDataString(repository.BaseBranch)}?recursive=1",
            accessToken);
        return await SendAsync<GitHubTreeResponse>(request);
    }

    private async Task<DateTimeOffset> GetLatestCommitDateAsync(GitHubRepository repository, string accessToken)
    {
        using var request = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/commits/{Uri.EscapeDataString(repository.BaseBranch)}",
            accessToken);
        var commit = await SendAsync<GitHubCommitResponse>(request);
        return commit.Commit.Author.Date;
    }

    private async Task<string> GetRawConfigAsync(GitHubRepository repository, string sourceFile, string accessToken)
    {
        using var request = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/contents/{string.Join("/", sourceFile.Split('/').Select(Uri.EscapeDataString))}?ref={Uri.EscapeDataString(repository.BaseBranch)}",
            accessToken);
        var content = await SendAsync<GitHubContentResponse>(request);

        if (!string.Equals(content.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"GitHub returned an unsupported encoding for '{sourceFile}'.");
        }

        var rawContent = Encoding.UTF8.GetString(Convert.FromBase64String(content.Content.Replace("\n", string.Empty, StringComparison.Ordinal)));

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new InvalidOperationException($"GitHub returned empty content for '{sourceFile}'.");
        }

        return rawContent;
    }

    private HttpRequestMessage CreateGitHubRequest(HttpMethod method, string uri, string accessToken, bool acceptGitHubJson = true)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("config-promote-api", "1.0"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (acceptGitHubJson)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return content ?? throw new InvalidOperationException("GitHub returned an unexpected empty response.");
    }

    private static ConfigMetadata ParseConfigMetadata(string rawContent)
    {
        try
        {
            return JsonSerializer.Deserialize<ConfigMetadata>(rawContent, JsonOptions)
                ?? new ConfigMetadata();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("A config file in GitHub could not be parsed.", exception);
        }
    }

    private static bool IsConfigFile(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 4 &&
               string.Equals(segments[0], "configs", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOutputFilePath(string sourceFile) =>
        string.Join('/', sourceFile.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1));

    private static string BuildSourceFilePath(string outputFile) =>
        $"configs/{outputFile}";

    private static GitModification MapToGitModification(GitHubCommitResponse commit)
    {
        var subject = commit.Commit.Message
            .Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? commit.Commit.Message;
        var bodyLines = commit.Commit.Message
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .ToArray();

        if (subject.StartsWith("Merge pull request", StringComparison.OrdinalIgnoreCase) && bodyLines.Length > 0)
        {
            subject = bodyLines[0];
        }

        return new GitModification
        {
            Commit = commit.Sha,
            AuthorName = commit.Commit.Author.Name,
            AuthorEmail = commit.Commit.Author.Email,
            AuthorDate = commit.Commit.Author.Date,
            Message = subject
        };
    }

    private sealed record ConfigMetadata
    {
        [JsonPropertyName("trigger")]
        public string? Trigger { get; init; }

        [JsonPropertyName("channel")]
        public string? Channel { get; init; }
    }

    private sealed record GitHubTreeResponse
    {
        public List<GitHubTreeEntry> Tree { get; init; } = [];
    }

    private sealed record GitHubTreeEntry
    {
        public required string Path { get; init; }

        public required string Type { get; init; }
    }

    private sealed record GitHubCommitResponse
    {
        public required string Sha { get; init; }

        public required GitHubCommitDetail Commit { get; init; }
    }

    private sealed record GitHubCommitDetail
    {
        public required GitHubCommitAuthor Author { get; init; }

        public required string Message { get; init; }
    }

    private sealed record GitHubCommitAuthor
    {
        public required string Name { get; init; }

        public required string Email { get; init; }

        public DateTimeOffset Date { get; init; }
    }

    private sealed record GitHubContentResponse
    {
        public required string Content { get; init; }

        public required string Encoding { get; init; }
    }
}
