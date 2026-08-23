using System.Diagnostics;

internal sealed class GitRepositoryMetadataService
{
    private readonly string _repositoryRoot;

    public GitRepositoryMetadataService(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    public async Task<GitHubRepositoryInfo> GetRepositoryInfoAsync()
    {
        var remoteUrl = await RunGitAsync("remote", "get-url", "origin");
        var baseBranch = await RunGitAsync("rev-parse", "--abbrev-ref", "HEAD");
        var (owner, name) = ParseGitHubRepository(remoteUrl);

        return new GitHubRepositoryInfo
        {
            Owner = owner,
            Name = name,
            BaseBranch = baseBranch
        };
    }

    private async Task<string> RunGitAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git process.");

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Unable to read git repository metadata. {standardError.Trim()}");
        }

        return standardOutput.Trim();
    }

    private static (string Owner, string Name) ParseGitHubRepository(string remoteUrl)
    {
        const string httpsPrefix = "https://github.com/";
        const string sshPrefix = "git@github.com:";

        var repositoryPath = remoteUrl.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase)
            ? remoteUrl[httpsPrefix.Length..]
            : remoteUrl.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase)
                ? remoteUrl[sshPrefix.Length..]
                : throw new InvalidOperationException($"Unsupported git remote URL '{remoteUrl}'.");

        if (repositoryPath.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repositoryPath = repositoryPath[..^4];
        }

        var segments = repositoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length != 2)
        {
            throw new InvalidOperationException($"Unable to parse GitHub repository details from '{remoteUrl}'.");
        }

        return (segments[0], segments[1]);
    }
}
