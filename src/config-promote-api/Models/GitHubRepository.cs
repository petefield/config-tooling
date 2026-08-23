namespace config_promote_api.Models;

internal sealed record GitHubRepository
{
    public required string Owner { get; init; }

    public required string Name { get; init; }

    public required string BaseBranch { get; init; }
}
