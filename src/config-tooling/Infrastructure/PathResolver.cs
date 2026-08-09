internal static class PathResolver
{
    public static string FindDefaultSourceDirectory(string currentDirectory)
    {
        for (var directory = new DirectoryInfo(currentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "configs");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(currentDirectory, "configs");
    }

    public static string? FindGitRepositoryRoot(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
