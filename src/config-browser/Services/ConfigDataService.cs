using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using config_browser.Models;
using Microsoft.Extensions.Configuration;

namespace config_browser.Services;

internal sealed class ConfigDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ConfigPathPrefixes = ["configs"];

    private readonly Dictionary<string, ConfigDocument> _configCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<GitModification>> _gitHistoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _rawConfigCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly GitHubRepository _repository;

    private ConfigCatalog? _catalog;

    public ConfigDataService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _repository = configuration.GetSection("Repository").Get<GitHubRepository>()
            ?? throw new InvalidOperationException("The browser app is missing Repository settings in appsettings.json.");
    }

    public async Task<ConfigCatalog> GetCatalogAsync()
    {
        if (_catalog is not null)
        {
            return _catalog;
        }

        var repositoryTree = await GetRepositoryTreeAsync();
        var sourceFiles = repositoryTree.Tree
            .Where(static entry => string.Equals(entry.Type, "blob", StringComparison.OrdinalIgnoreCase))
            .Where(entry => IsConfigFile(entry.Path))
            .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var entries = await Task.WhenAll(sourceFiles.Select(MapToCatalogEntryAsync));

        await ApplyMatchingEnvironmentFlagsAsync(entries);
        var latestCommitDate = await GetLatestCommitDateAsync();

        _catalog = new ConfigCatalog
        {
            GeneratedAtUtc = latestCommitDate,
            Repository = _repository,
            Entries = entries
                .OrderBy(static entry => entry.Tenant, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Environment, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        return _catalog;
    }

    public async Task<ConfigCatalogEntry?> GetCatalogEntryAsync(string outputFile)
    {
        var catalog = await GetCatalogAsync();

        return catalog.Entries.FirstOrDefault(
            entry => string.Equals(entry.OutputFile, outputFile, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<GitModification>> GetGitHistoryAsync(string sourceFile)
    {
        if (_gitHistoryCache.TryGetValue(sourceFile, out var cachedHistory))
        {
            return cachedHistory;
        }

        using var response = await SendGitHubGetAsync(BuildCommitsApiPath(sourceFile));
        response.EnsureSuccessStatusCode();

        var commits = await ReadJsonAsync<List<GitHubCommitResponse>>(
                response,
                $"GitHub did not return valid history for '{sourceFile}'.")
            ?? [];

        var modifications = commits
            .Select(MapToGitModification)
            .ToArray();

        _gitHistoryCache[sourceFile] = modifications;
        return modifications;
    }

    public async Task<ConfigDocument> GetConfigAsync(string outputFile)
    {
        if (_configCache.TryGetValue(outputFile, out var cachedDocument))
        {
            return cachedDocument;
        }

        var rawConfig = await GetRawConfigAsync(outputFile);
        var config = DeserializeJson<ConfigDocument>(
                rawConfig,
                $"The GitHub config '{outputFile}' is missing or not valid JSON.")
            ?? throw new InvalidOperationException($"The GitHub config '{outputFile}' could not be read.");

        _configCache[outputFile] = config;
        return config;
    }

    public Task<string> GetConfigTextAsync(string outputFile) =>
        GetRawConfigAsync(outputFile);

    private async Task ApplyMatchingEnvironmentFlagsAsync(ConfigCatalogEntry[] entries)
    {
        var updatedEntries = new List<ConfigCatalogEntry>(entries.Length);
        var entriesByKey = entries.ToDictionary(
            static entry => BuildTenantFileKey(entry.Tenant, entry.FileName, entry.Environment),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var updatedEntry = entry;

            if (string.Equals(entry.Environment, "dev", StringComparison.OrdinalIgnoreCase))
            {
                var uatKey = BuildTenantFileKey(entry.Tenant, entry.FileName, "uat");
                var hasMatchingUatVersion = false;

                if (entriesByKey.TryGetValue(uatKey, out var uatEntry))
                {
                    var devConfig = await GetRawConfigAsync(entry.OutputFile);
                    var uatConfig = await GetRawConfigAsync(uatEntry.OutputFile);
                    hasMatchingUatVersion = string.Equals(devConfig, uatConfig, StringComparison.Ordinal);
                }

                updatedEntry = updatedEntry with { HasMatchingUatVersion = hasMatchingUatVersion };
            }

            if (string.Equals(entry.Environment, "uat", StringComparison.OrdinalIgnoreCase))
            {
                var prdKey = BuildTenantFileKey(entry.Tenant, entry.FileName, "prd");
                var hasMatchingPrdVersion = false;

                if (entriesByKey.TryGetValue(prdKey, out var prdEntry))
                {
                    var uatConfig = await GetRawConfigAsync(entry.OutputFile);
                    var prdConfig = await GetRawConfigAsync(prdEntry.OutputFile);
                    hasMatchingPrdVersion = string.Equals(uatConfig, prdConfig, StringComparison.Ordinal);
                }

                updatedEntry = updatedEntry with { HasMatchingPrdVersion = hasMatchingPrdVersion };
            }

            updatedEntries.Add(updatedEntry);
        }

        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = updatedEntries[index];
        }
    }

    private async Task<string> GetRawConfigAsync(string outputFile)
    {
        if (_rawConfigCache.TryGetValue(outputFile, out var cachedConfig))
        {
            return cachedConfig;
        }

        var sourceFile = BuildSourceFilePath(outputFile);
        using var response = await _httpClient.GetAsync(BuildRawContentUrl(sourceFile));
        response.EnsureSuccessStatusCode();

        var rawConfig = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(rawConfig))
        {
            throw new InvalidOperationException(
                $"The GitHub config '{outputFile}' is missing or not valid JSON.");
        }

        _rawConfigCache[outputFile] = rawConfig;
        return rawConfig;
    }

    private async Task<GitHubTreeResponse> GetRepositoryTreeAsync()
    {
        using var response = await SendGitHubGetAsync(
            $"https://api.github.com/repos/{_repository.Owner}/{_repository.Name}/git/trees/{Uri.EscapeDataString(_repository.BaseBranch)}?recursive=1");
        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync<GitHubTreeResponse>(
                   response,
                   "GitHub did not return a valid repository tree for the config browser.")
               ?? throw new InvalidOperationException("GitHub returned an empty repository tree for the config browser.");
    }

    private async Task<DateTimeOffset> GetLatestCommitDateAsync()
    {
        using var response = await SendGitHubGetAsync(
            $"https://api.github.com/repos/{_repository.Owner}/{_repository.Name}/commits/{Uri.EscapeDataString(_repository.BaseBranch)}");
        response.EnsureSuccessStatusCode();

        var commit = await ReadJsonAsync<GitHubCommitResponse>(
                         response,
                         "GitHub did not return a valid latest commit for the config browser.")
                     ?? throw new InvalidOperationException("GitHub returned an empty latest commit for the config browser.");

        return commit.Commit.Author.Date;
    }

    private async Task<ConfigCatalogEntry> MapToCatalogEntryAsync(GitHubTreeEntry entry)
    {
        var sourceFile = entry.Path;
        var outputFile = BuildOutputFilePath(sourceFile);
        var config = await GetConfigAsync(outputFile);

        var segments = outputFile.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 3)
        {
            throw new InvalidOperationException($"Unexpected config output path '{outputFile}' derived from '{sourceFile}'.");
        }

        return new ConfigCatalogEntry
        {
            OutputFile = outputFile,
            SourceFile = sourceFile,
            ContactType = config.Trigger ?? "—",
            Channel = config.Channel ?? "—",
            Tenant = segments[0],
            Environment = segments[1],
            FileName = segments[^1],
            Modifications = []
        };
    }

    private string BuildRawContentUrl(string sourceFile) =>
        $"https://raw.githubusercontent.com/{_repository.Owner}/{_repository.Name}/{Uri.EscapeDataString(_repository.BaseBranch)}/{string.Join("/", sourceFile.Split('/').Select(Uri.EscapeDataString))}";

    private string BuildCommitsApiPath(string sourceFile) =>
        $"https://api.github.com/repos/{_repository.Owner}/{_repository.Name}/commits?sha={Uri.EscapeDataString(_repository.BaseBranch)}&path={Uri.EscapeDataString(sourceFile)}&per_page=5";

    private async Task<HttpResponseMessage> SendGitHubGetAsync(string requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return await _httpClient.SendAsync(request);
    }

    private static string BuildOutputFilePath(string sourceFile)
    {
        var segments = sourceFile.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 4 || !ConfigPathPrefixes.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unexpected config source path '{sourceFile}'.");
        }

        return string.Join('/', segments.Skip(1));
    }

    private static string BuildSourceFilePath(string outputFile) =>
        $"configs/{outputFile}";

    private static bool IsConfigFile(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 4 &&
               ConfigPathPrefixes.Contains(segments[0], StringComparer.OrdinalIgnoreCase) &&
               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTenantFileKey(string tenant, string fileName, string environment) =>
        $"{tenant}/{environment}/{fileName}";

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, string invalidJsonMessage)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            throw new InvalidOperationException(invalidJsonMessage);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(invalidJsonMessage, exception);
        }
    }

    private static T? DeserializeJson<T>(string json, string invalidJsonMessage)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(invalidJsonMessage, exception);
        }
    }

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
}
