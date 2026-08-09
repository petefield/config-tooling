using System.Text.Json;

var currentDirectory = Directory.GetCurrentDirectory();
var sourceDirectory = args.Length > 0
    ? Path.GetFullPath(args[0], currentDirectory)
    : Path.Combine(currentDirectory, "configs");
var destinationRoot = args.Length > 1
    ? Path.GetFullPath(args[1], currentDirectory)
    : Path.Combine(currentDirectory, "root");

if (!Directory.Exists(sourceDirectory))
{
    Console.Error.WriteLine($"Config directory not found: {sourceDirectory}");
    return 1;
}

var jsonFiles = Directory
    .EnumerateFiles(sourceDirectory, "*.json", SearchOption.TopDirectoryOnly)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (jsonFiles.Length == 0)
{
    Console.WriteLine($"No config files found in {sourceDirectory}");
    return 0;
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

var filesCopied = 0;

foreach (var filePath in jsonFiles)
{
    var fileContents = await File.ReadAllTextAsync(filePath);
    var config = JsonSerializer.Deserialize<ConfigFile>(fileContents, jsonOptions)
        ?? throw new InvalidOperationException($"Unable to deserialize config file '{filePath}'.");

    foreach (var featureFlag in config.FeatureFlags.Where(flag => flag.Enabled))
    {
        foreach (var environment in ResolveEnvironments(featureFlag.Environments))
        {
            var targetDirectory = Path.Combine(destinationRoot, featureFlag.Tenant, environment);
            Directory.CreateDirectory(targetDirectory);

            var targetFilePath = Path.Combine(targetDirectory, Path.GetFileName(filePath));
            await File.WriteAllTextAsync(targetFilePath, fileContents);
            filesCopied++;
        }
    }
}

Console.WriteLine($"Copied {filesCopied} file(s) into {destinationRoot}");
return 0;

static IReadOnlyCollection<string> ResolveEnvironments(IEnumerable<string> environments)
{
    var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var environment in environments)
    {
        if (string.Equals(environment, "all", StringComparison.OrdinalIgnoreCase))
        {
            resolved.Add("dev");
            resolved.Add("uat");
            resolved.Add("prd");
            continue;
        }

        resolved.Add(environment.ToLowerInvariant());
    }

    return resolved;
}
