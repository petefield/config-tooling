using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using config_browser.Models;

namespace config_browser.Services;

internal sealed class ConfigDataService
{
    private readonly Dictionary<string, ConfigDocument> _configCache = new(StringComparer.OrdinalIgnoreCase);
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

        _catalog = new ConfigCatalog
        {
            GeneratedAtUtc = historyIndex.GeneratedAtUtc,
            Entries = historyIndex.Files
                .Select(MapToCatalogEntry)
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

        using var response = await _httpClient.GetAsync(BuildDataPath(outputFile));
        response.EnsureSuccessStatusCode();

        var config = await ReadJsonAsync<ConfigDocument>(
                response,
                $"The generated config '{outputFile}' is missing or not valid JSON. Rebuild or restart the browser app after generating src/config-tooling/root.")
            ?? throw new InvalidOperationException($"The generated config '{outputFile}' could not be read.");

        _configCache[outputFile] = config;
        return config;
    }

    private static string BuildDataPath(string outputFile) =>
        $"data/{string.Join("/", outputFile.Split('/').Select(Uri.EscapeDataString))}";

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
