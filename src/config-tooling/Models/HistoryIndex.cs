internal sealed record HistoryIndex
{
    public DateTimeOffset GeneratedAtUtc { get; init; }

    public required GitHubRepositoryInfo Repository { get; init; }

    public List<FileHistory> Files { get; init; } = [];
}
