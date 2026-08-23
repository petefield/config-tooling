namespace config_promote_api.Models;

internal sealed record ConfigCatalogResponse
{
    public DateTimeOffset GeneratedAtUtc { get; init; }

    public List<ConfigCatalogItem> Entries { get; init; } = [];
}
