namespace config_promote_api.Models;

internal sealed record ApiErrorResponse
{
    public required string Message { get; init; }
}
