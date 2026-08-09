using System.Text.Json;
using System.Text.Json.Serialization;

namespace config_browser.Models;

internal sealed record ConfigDocument
{
    public string? OperationUnit { get; init; }

    public string? Connector { get; init; }

    public string? Channel { get; init; }

    public string? Trigger { get; init; }

    public string? OutputType { get; init; }

    public List<ConfigField> InputData { get; init; } = [];

    public List<ConfigMapping> Mapping { get; init; } = [];

    public ConfigCondition? Condition { get; init; }

    public List<FeatureFlag> FeatureFlags { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; init; } = [];
}

internal sealed record ConfigField
{
    public string? Name { get; init; }

    public string? Type { get; init; }

    public string? Path { get; init; }

    public bool? Required { get; init; }
}

internal sealed record ConfigMapping
{
    public string? InputField { get; init; }

    public string? OutputField { get; init; }
}

internal sealed record ConfigCondition
{
    public string? Expression { get; init; }

    public string? Description { get; init; }
}

internal sealed record FeatureFlag
{
    public string? Tenant { get; init; }

    public bool Enabled { get; init; }

    public List<string> Environments { get; init; } = [];
}
