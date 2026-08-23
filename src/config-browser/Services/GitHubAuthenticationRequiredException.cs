namespace config_browser.Services;

internal sealed class GitHubAuthenticationRequiredException : InvalidOperationException
{
    public GitHubAuthenticationRequiredException(string message)
        : base(message)
    {
    }
}
