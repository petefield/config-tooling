namespace config_promote_api.Services;

internal sealed record GitHubAppOptions
{
    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public required string CallbackUrl { get; init; }

    public required byte[] SessionKey { get; init; }

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    public static GitHubAppOptions FromEnvironment()
    {
        var clientId = ReadRequired("GitHubAppClientId");
        var clientSecret = ReadRequired("GitHubAppClientSecret");
        var callbackUrl = ReadRequired("GitHubAppCallbackUrl");
        var sessionKey = Convert.FromBase64String(ReadRequired("GitHubAppSessionKey"));
        var allowedOrigins = (Environment.GetEnvironmentVariable("GitHubAppAllowedOrigins") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (sessionKey.Length != 32)
        {
            throw new InvalidOperationException("GitHubAppSessionKey must be a base64-encoded 32-byte key.");
        }

        return new GitHubAppOptions
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            CallbackUrl = callbackUrl,
            SessionKey = sessionKey,
            AllowedOrigins = allowedOrigins
        };
    }

    private static string ReadRequired(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Missing required environment variable '{name}'.");
}
