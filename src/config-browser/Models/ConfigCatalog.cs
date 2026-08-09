namespace config_browser.Models;

internal sealed record ConfigCatalog
{
    public DateTimeOffset GeneratedAtUtc { get; init; }

    public IReadOnlyList<ConfigCatalogEntry> Entries { get; init; } = [];
}

internal sealed record ConfigCatalogEntry
{
    public required string OutputFile { get; init; }

    public required string SourceFile { get; init; }

    public required string Tenant { get; init; }

    public required string Environment { get; init; }

    public required string FileName { get; init; }

    public IReadOnlyList<GitModification> Modifications { get; init; } = [];
}

internal sealed record ConfigHistoryIndex
{
    public DateTimeOffset GeneratedAtUtc { get; init; }

    public List<ConfigHistoryEntry> Files { get; init; } = [];
}

internal sealed record ConfigHistoryEntry
{
    public required string OutputFile { get; init; }

    public required string SourceFile { get; init; }

    public List<GitModification> Modifications { get; init; } = [];
}

internal sealed record GitModification
{
    public required string Commit { get; init; }

    public required string AuthorName { get; init; }

    public required string AuthorEmail { get; init; }

    public DateTimeOffset AuthorDate { get; init; }

    public required string Message { get; init; }
}
