internal sealed record GitHubRepositoryInfo
{
    public required string Owner { get; init; }

    public required string Name { get; init; }

    public required string BaseBranch { get; init; }
}
