namespace config_promote_api.Models;

internal sealed record GitHubAuthSession
{
    public required string AccessToken { get; init; }

    public DateTimeOffset? AccessTokenExpiresAtUtc { get; init; }

    public string? RefreshToken { get; init; }

    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; init; }
}
