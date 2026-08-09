internal sealed record HistoryIndex
{
    public DateTimeOffset GeneratedAtUtc { get; init; }

    public List<FileHistory> Files { get; init; } = [];
}
