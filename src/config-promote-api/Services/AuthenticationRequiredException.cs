namespace config_promote_api.Services;

internal sealed class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException(string message)
        : base(message)
    {
    }

    public AuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
