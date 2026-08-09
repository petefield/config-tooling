internal sealed record AppOptions(string SourceDirectory, string DestinationRoot, string RepositoryRoot)
{
    public static AppOptions Create(string[] args, string currentDirectory)
    {
        var sourceDirectory = args.Length > 0
            ? Path.GetFullPath(args[0], currentDirectory)
            : PathResolver.FindDefaultSourceDirectory(currentDirectory);
        var destinationRoot = args.Length > 1
            ? Path.GetFullPath(args[1], currentDirectory)
            : Path.Combine(currentDirectory, "output");
        var repositoryRoot = PathResolver.FindGitRepositoryRoot(sourceDirectory)
            ?? throw new InvalidOperationException(
                $"Config directory '{sourceDirectory}' is not inside a git repository.");

        return new AppOptions(sourceDirectory, destinationRoot, repositoryRoot);
    }
}
