internal sealed class FeatureFlag
{
    public required string Tenant { get; init; }

    public bool Enabled { get; init; }

    public List<string> Environments { get; init; } = [];
}
