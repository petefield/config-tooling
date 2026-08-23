using Microsoft.Azure.Functions.Worker.Http;

namespace config_promote_api.Services;

internal sealed class CorsService
{
    private readonly IReadOnlyList<string> _allowedOrigins;

    public CorsService(GitHubAppOptions options)
    {
        _allowedOrigins = options.AllowedOrigins;
    }

    public void Apply(HttpRequestData request, HttpResponseData response)
    {
        var origin = GetAllowedOrigin(request);

        if (origin is null)
        {
            return;
        }

        response.Headers.Add("Access-Control-Allow-Origin", origin);
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");
        response.Headers.Add("Vary", "Origin");
    }

    private string? GetAllowedOrigin(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("Origin", out var values))
        {
            return _allowedOrigins.Count == 0 ? "*" : null;
        }

        var origin = values.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(origin))
        {
            return _allowedOrigins.Count == 0 ? "*" : null;
        }

        if (_allowedOrigins.Count == 0 || _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return origin;
        }

        return null;
    }
}
