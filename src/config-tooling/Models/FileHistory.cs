internal sealed record FileHistory
{
    public required string OutputFile { get; init; }

    public required string SourceFile { get; init; }

    public List<GitModification> Modifications { get; init; } = [];

    public required string ContactType {get;set; }

    public required string Channel {get; set; }
}
