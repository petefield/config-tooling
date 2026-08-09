using System.Diagnostics;
using System.Globalization;

internal sealed class GitHistoryService
{
    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';
    private const int HistoryLimit = 5;

    private readonly Dictionary<string, IReadOnlyList<GitModification>> _historyBySourceFile =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _repositoryRoot;

    public GitHistoryService(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    public async Task<IReadOnlyList<GitModification>> GetFileHistoryAsync(string relativeFilePath)
    {
        if (_historyBySourceFile.TryGetValue(relativeFilePath, out var existingHistory))
        {
            return existingHistory;
        }

        var history = await ReadFileHistoryAsync(relativeFilePath);
        _historyBySourceFile[relativeFilePath] = history;
        return history;
    }

    private async Task<IReadOnlyList<GitModification>> ReadFileHistoryAsync(string relativeFilePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--no-pager");
        startInfo.ArgumentList.Add("log");
        startInfo.ArgumentList.Add("--first-parent");
        startInfo.ArgumentList.Add("--diff-filter=AMR");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add(HistoryLimit.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--date=iso-strict");
        startInfo.ArgumentList.Add($"--format=%H%x1f%an%x1f%ae%x1f%ad%x1f%s%x1f%b%x1e");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(relativeFilePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git process.");

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to read git history for '{relativeFilePath}'. {standardError.Trim()}");
        }

        return ParseHistoryOutput(standardOutput);
    }

    private static IReadOnlyList<GitModification> ParseHistoryOutput(string standardOutput)
    {
        var modifications = new List<GitModification>();

        foreach (var record in standardOutput.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = record.Split(FieldSeparator);

            if (parts.Length != 6)
            {
                throw new InvalidOperationException($"Unexpected git log output: '{record}'.");
            }

            var subject = parts[4].Trim();
            var body = parts[5].Trim();

            modifications.Add(new GitModification
            {
                Commit = parts[0],
                AuthorName = parts[1],
                AuthorEmail = parts[2],
                AuthorDate = DateTimeOffset.Parse(parts[3], CultureInfo.InvariantCulture),
                Message = SelectDisplayMessage(subject, body)
            });
        }

        return modifications;
    }

    private static string SelectDisplayMessage(string subject, string body)
    {
        if (!subject.StartsWith("Merge pull request", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }

        var summary = body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(summary) ? subject : summary;
    }
}
