using System.Text.Json;
using System.Threading.Channels;
using System.Xml.Schema;

internal sealed class ConfigToolingApp
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly GitHistoryService _gitHistoryService;

    private ConfigToolingApp(GitHistoryService gitHistoryService)
    {
        _gitHistoryService = gitHistoryService;
    }

    public static Task<int> RunAsync(string[] args)
    {
        var options = AppOptions.Create(args, Directory.GetCurrentDirectory());
        var gitHistoryService = new GitHistoryService(options.RepositoryRoot);

        return new ConfigToolingApp(gitHistoryService).RunAsync(options);
    }

    private async Task<int> RunAsync(AppOptions options)
    {
        if (!Directory.Exists(options.SourceDirectory))
        {
            Console.Error.WriteLine($"Config directory not found: {options.SourceDirectory}");
            return 1;
        }

        var jsonFiles = Directory
            .EnumerateFiles(options.SourceDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (jsonFiles.Length == 0)
        {
            Console.WriteLine($"No config files found in {options.SourceDirectory}");
            return 0;
        }

        ResetDestinationRoot(options.DestinationRoot);

        var filesCopied = 0;
        var fileHistories = new List<FileHistory>();

        foreach (var filePath in jsonFiles)
        {
            Console.WriteLine($"Processing file {filePath}");
            filesCopied += await ProcessConfigFileAsync(filePath, options, fileHistories);
        }

        var historyIndexPath = await WriteHistoryIndexAsync(options.DestinationRoot, fileHistories);

        Console.WriteLine($"Copied {filesCopied} file(s) into {options.DestinationRoot}");
        Console.WriteLine($"Wrote history index to {historyIndexPath}");
        return 0;
    }

    private async Task<int> ProcessConfigFileAsync(
        string filePath,
        AppOptions options,
        ICollection<FileHistory> fileHistories)
    {
        var fileContents = await File.ReadAllTextAsync(filePath);
        var config = System.Text.Json.JsonSerializer.Deserialize<Config>(fileContents);
        var relativeSourceFile = Path.GetRelativePath(options.RepositoryRoot, filePath);
        var modifications = await _gitHistoryService.GetFileHistoryAsync(relativeSourceFile);
        var copiesCreated = 0;

        var pathtokens = Path.GetRelativePath(options.SourceDirectory, filePath).Split(System.IO.Path.DirectorySeparatorChar);
        var tenant = pathtokens[0];
        var environment = pathtokens[1];
    
        var outputFilePath = Path.Combine(
            options.DestinationRoot,
            tenant,
            environment,
            Path.GetFileName(filePath));

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
        Console.WriteLine($" - {outputFilePath}");
        await File.WriteAllTextAsync(outputFilePath, fileContents);

        fileHistories.Add(new FileHistory
        {
            OutputFile = Path.GetRelativePath(options.DestinationRoot, outputFilePath),
            SourceFile = relativeSourceFile,
            Modifications = modifications.ToList(),
            ContactType = config.trigger,
            Channel = config.channel
            
        });

        copiesCreated++;
            
        

        return copiesCreated;
    }

    private static void ResetDestinationRoot(string destinationRoot)
    {
        if (Directory.Exists(destinationRoot))
        {
            Directory.Delete(destinationRoot, recursive: true);
        }

        Directory.CreateDirectory(destinationRoot);
    }

    private static async Task<string> WriteHistoryIndexAsync(
        string destinationRoot,
        List<FileHistory> fileHistories)
    {
        var historyIndexPath = Path.Combine(destinationRoot, "history-index.json");
        var historyIndex = new HistoryIndex
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Files = fileHistories
        };

        await File.WriteAllTextAsync(
            historyIndexPath,
            JsonSerializer.Serialize(historyIndex, JsonOptions));

        return historyIndexPath;
    }
}
