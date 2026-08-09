internal sealed record ConfigFile
{
    public List<FeatureFlag> FeatureFlags { get; init; } = [];

    public IEnumerable<FeatureFlag> GetEnabledFeatureFlags() =>
        FeatureFlags.Where(static featureFlag => featureFlag.Enabled);
}
