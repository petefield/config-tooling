internal sealed record GitModification
{
    public required string Commit { get; init; }

    public required string AuthorName { get; init; }

    public required string AuthorEmail { get; init; }

    public DateTimeOffset AuthorDate { get; init; }

    public required string Message { get; init; }
}
