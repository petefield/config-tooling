using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using config_browser.Models;

namespace config_browser.Services;

internal sealed class ConfigDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, ConfigDocument> _configCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _rawConfigCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;

    private ConfigCatalog? _catalog;

    public ConfigDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ConfigCatalog> GetCatalogAsync()
    {
        if (_catalog is not null)
        {
            return _catalog;
        }

        using var response = await _httpClient.GetAsync("data/history-index.json");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "No generated config data was found. Run the config-tooling console app first so src/config-tooling/root contains fresh output.");
        }

        response.EnsureSuccessStatusCode();

        var historyIndex = await ReadJsonAsync<ConfigHistoryIndex>(
                response,
                "The generated history index is missing or not valid JSON. Rebuild or restart the browser app after generating src/config-tooling/root.")
            ?? throw new InvalidOperationException("The generated history index could not be read.");

        var entries = historyIndex.Files
            .Select(MapToCatalogEntry)
            .ToArray();

        await ApplyMatchingPrdFlagsAsync(entries);

        _catalog = new ConfigCatalog
        {
            GeneratedAtUtc = historyIndex.GeneratedAtUtc,
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

    public async Task<ConfigDocument> GetConfigAsync(string outputFile)
    {
        if (_configCache.TryGetValue(outputFile, out var cachedDocument))
        {
            return cachedDocument;
        }

        var rawConfig = await GetRawConfigAsync(outputFile);
        var config = DeserializeJson<ConfigDocument>(
                rawConfig,
                $"The generated config '{outputFile}' is missing or not valid JSON. Rebuild or restart the browser app after generating src/config-tooling/root.")
            ?? throw new InvalidOperationException($"The generated config '{outputFile}' could not be read.");

        _configCache[outputFile] = config;
        return config;
    }

    private async Task ApplyMatchingPrdFlagsAsync(ConfigCatalogEntry[] entries)
    {
        var updatedEntries = new List<ConfigCatalogEntry>(entries.Length);
        var entriesByKey = entries.ToDictionary(
            static entry => BuildTenantFileKey(entry.Tenant, entry.FileName, entry.Environment),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!string.Equals(entry.Environment, "uat", StringComparison.OrdinalIgnoreCase))
            {
                updatedEntries.Add(entry);
                continue;
            }

            var prdKey = BuildTenantFileKey(entry.Tenant, entry.FileName, "prd");
            var hasMatchingPrdVersion = false;

            if (entriesByKey.TryGetValue(prdKey, out var prdEntry))
            {
                var uatConfig = await GetRawConfigAsync(entry.OutputFile);
                var prdConfig = await GetRawConfigAsync(prdEntry.OutputFile);
                hasMatchingPrdVersion = string.Equals(uatConfig, prdConfig, StringComparison.Ordinal);
            }

            updatedEntries.Add(entry with { HasMatchingPrdVersion = hasMatchingPrdVersion });
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

        using var response = await _httpClient.GetAsync(BuildDataPath(outputFile));
        response.EnsureSuccessStatusCode();

        var rawConfig = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(rawConfig))
        {
            throw new InvalidOperationException(
                $"The generated config '{outputFile}' is missing or not valid JSON. Rebuild or restart the browser app after generating src/config-tooling/root.");
        }

        _rawConfigCache[outputFile] = rawConfig;
        return rawConfig;
    }

    private static string BuildDataPath(string outputFile) =>
        $"data/{string.Join("/", outputFile.Split('/').Select(Uri.EscapeDataString))}";

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

    private static ConfigCatalogEntry MapToCatalogEntry(ConfigHistoryEntry entry)
    {
        var segments = entry.OutputFile.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 3)
        {
            throw new InvalidOperationException(
                $"Unexpected generated output path '{entry.OutputFile}' in history-index.json.");
        }

        return new ConfigCatalogEntry
        {
            OutputFile = entry.OutputFile,
            SourceFile = entry.SourceFile,
            Tenant = segments[0],
            Environment = segments[1],
            FileName = segments[^1],
            Modifications = entry.Modifications.ToArray()
        };
    }
}
