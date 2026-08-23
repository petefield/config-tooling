namespace config_promote_api.Models;

internal sealed record ConfigCatalogEntry
{
    public required string OutputFile { get; init; }

    public required string SourceFile { get; init; }

    public required string Tenant { get; init; }

    public required string ContactType { get; init; }

    public required string Channel { get; init; }

    public required string Environment { get; init; }

    public required string FileName { get; init; }
}
