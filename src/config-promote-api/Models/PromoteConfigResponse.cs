namespace config_promote_api.Models;

internal sealed record PromoteConfigResponse
{
    public required string BranchName { get; init; }

    public required int PullRequestNumber { get; init; }

    public required string PullRequestUrl { get; init; }

    public required string AuthSession { get; init; }
}
