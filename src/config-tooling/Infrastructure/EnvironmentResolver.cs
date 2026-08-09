internal static class EnvironmentResolver
{
    private static readonly string[] ExpandedEnvironments = ["dev", "uat", "prd"];

    public static IReadOnlyCollection<string> Resolve(IEnumerable<string> environments)
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var environment in environments)
        {
            if (string.Equals(environment, "all", StringComparison.OrdinalIgnoreCase))
            {
                resolved.UnionWith(ExpandedEnvironments);
                continue;
            }

            resolved.Add(environment.ToLowerInvariant());
        }

        return resolved;
    }
}
