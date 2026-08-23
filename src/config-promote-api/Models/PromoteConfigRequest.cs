namespace config_promote_api.Models;

internal sealed record PromoteConfigRequest
{
    public required string AuthSession { get; init; }

    public required GitHubRepository Repository { get; init; }

    public required ConfigCatalogEntry Entry { get; init; }

    public required string FileContents { get; init; }
}
