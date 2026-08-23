namespace config_promote_api.Models;

internal sealed record GitHubAuthState
{
    public required string AppOrigin { get; init; }

    public DateTimeOffset IssuedAtUtc { get; init; }
}
