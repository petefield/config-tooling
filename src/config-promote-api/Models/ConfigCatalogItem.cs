namespace config_promote_api.Models;

internal sealed record ConfigCatalogItem
{
    public required string OutputFile { get; init; }

    public required string SourceFile { get; init; }

    public required string ContactType { get; init; }

    public required string Channel { get; init; }
}
