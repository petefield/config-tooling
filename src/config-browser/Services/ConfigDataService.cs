using System.Net.Http.Json;
using System.Text.Json;
using config_browser.Models;
using Microsoft.Extensions.Configuration;

namespace config_browser.Services;

internal sealed class ConfigDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, ConfigDocument> _configCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<GitModification>> _gitHistoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _rawConfigCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly string _promoteApiBaseUrl;
    private readonly GitHubRepository _repository;

    private ConfigCatalog? _catalog;

    public ConfigDataService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _promoteApiBaseUrl = configuration["PromoteApiBaseUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("The browser app is missing PromoteApiBaseUrl in appsettings.json.");
        _repository = configuration.GetSection("Repository").Get<GitHubRepository>()
            ?? throw new InvalidOperationException("The browser app is missing Repository settings in appsettings.json.");
    }

    public async Task<ConfigCatalog> GetCatalogAsync()
    {
        if (_catalog is not null)
        {
            return _catalog;
        }

        using var response = await _httpClient.GetAsync(BuildCatalogApiUrl());
        response.EnsureSuccessStatusCode();

        var apiCatalog = await ReadJsonAsync<ConfigCatalogResponse>(
                response,
                "The promote API did not return a valid config catalog.")
            ?? throw new InvalidOperationException("The promote API returned an empty config catalog.");

        var entries = apiCatalog.Entries
            .Select(MapToCatalogEntry)
            .ToArray();

        await ApplyMatchingEnvironmentFlagsAsync(entries);

        _catalog = new ConfigCatalog
        {
            GeneratedAtUtc = apiCatalog.GeneratedAtUtc,
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

        using var response = await _httpClient.GetAsync(BuildHistoryApiUrl(sourceFile));
        response.EnsureSuccessStatusCode();

        var history = await ReadJsonAsync<List<GitModification>>(
                response,
                $"The promote API did not return valid git history for '{sourceFile}'.")
            ?? [];

        _gitHistoryCache[sourceFile] = history;
        return history;
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
                $"The promote API config '{outputFile}' is missing or not valid JSON.")
            ?? throw new InvalidOperationException($"The promote API config '{outputFile}' could not be read.");

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

        using var response = await _httpClient.GetAsync(BuildFileApiUrl(outputFile));
        response.EnsureSuccessStatusCode();

        var rawConfig = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(rawConfig))
        {
            throw new InvalidOperationException($"The promote API config '{outputFile}' is missing or empty.");
        }

        _rawConfigCache[outputFile] = rawConfig;
        return rawConfig;
    }

    private string BuildCatalogApiUrl() =>
        $"{_promoteApiBaseUrl}/api/configs/catalog?owner={Uri.EscapeDataString(_repository.Owner)}&repo={Uri.EscapeDataString(_repository.Name)}&branch={Uri.EscapeDataString(_repository.BaseBranch)}";

    private string BuildFileApiUrl(string outputFile) =>
        $"{_promoteApiBaseUrl}/api/configs/file?owner={Uri.EscapeDataString(_repository.Owner)}&repo={Uri.EscapeDataString(_repository.Name)}&branch={Uri.EscapeDataString(_repository.BaseBranch)}&path={Uri.EscapeDataString(outputFile)}";

    private string BuildHistoryApiUrl(string sourceFile) =>
        $"{_promoteApiBaseUrl}/api/configs/history?owner={Uri.EscapeDataString(_repository.Owner)}&repo={Uri.EscapeDataString(_repository.Name)}&branch={Uri.EscapeDataString(_repository.BaseBranch)}&path={Uri.EscapeDataString(sourceFile)}";

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

    private static ConfigCatalogEntry MapToCatalogEntry(ConfigCatalogItem entry)
    {
        var segments = entry.OutputFile.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 3)
        {
            throw new InvalidOperationException($"Unexpected config output path '{entry.OutputFile}'.");
        }

        return new ConfigCatalogEntry
        {
            OutputFile = entry.OutputFile,
            SourceFile = entry.SourceFile,
            ContactType = entry.ContactType,
            Channel = entry.Channel,
            Tenant = segments[0],
            Environment = segments[1],
            FileName = segments[^1],
            Modifications = []
        };
    }

    private sealed record ConfigCatalogResponse
    {
        public DateTimeOffset GeneratedAtUtc { get; init; }

        public List<ConfigCatalogItem> Entries { get; init; } = [];
    }

    private sealed record ConfigCatalogItem
    {
        public required string OutputFile { get; init; }

        public required string SourceFile { get; init; }

        public required string ContactType { get; init; }

        public required string Channel { get; init; }
    }
}
