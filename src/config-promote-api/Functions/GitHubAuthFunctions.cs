using System.Net;
using System.Text;
using config_promote_api.Models;
using config_promote_api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace config_promote_api.Functions;

internal sealed class GitHubAuthFunctions
{
    private readonly GitHubUserTokenService _tokenService;

    public GitHubAuthFunctions(GitHubUserTokenService tokenService)
    {
        _tokenService = tokenService;
    }

    [Function("StartGitHubSignIn")]
    public Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/github/start")] HttpRequestData request)
    {
        var appOrigin = GetRequiredQueryValue(request, "appOrigin");
        var redirectUrl = _tokenService.BuildAuthorizationUrl(appOrigin);

        var response = request.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", redirectUrl);
        return Task.FromResult(response);
    }

    [Function("CompleteGitHubSignIn")]
    public async Task<HttpResponseData> CallbackAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/github/callback")] HttpRequestData request)
    {
        var error = GetOptionalQueryValue(request, "error");
        var stateToken = GetRequiredQueryValue(request, "state");

        if (!string.IsNullOrWhiteSpace(error))
        {
            return CreateHtmlResponse(
                request,
                BuildCompletionHtml(
                    _tokenService.TryGetAppOrigin(stateToken),
                    authSession: null,
                    errorMessage: "GitHub sign-in was cancelled or denied."));
        }

        var code = GetRequiredQueryValue(request, "code");

        try
        {
            var completion = await _tokenService.ExchangeCodeAsync(code, stateToken);
            return CreateHtmlResponse(
                request,
                BuildCompletionHtml(completion.AppOrigin, authSession: completion.AuthSession, errorMessage: null));
        }
        catch (Exception exception)
        {
            var appOrigin = _tokenService.TryGetAppOrigin(stateToken);
            return CreateHtmlResponse(
                request,
                BuildCompletionHtml(appOrigin, authSession: null, errorMessage: exception.Message));
        }
    }

    private static HttpResponseData CreateHtmlResponse(HttpRequestData request, string html)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        response.WriteString(html, Encoding.UTF8);
        return response;
    }

    private static string GetRequiredQueryValue(HttpRequestData request, string key)
    {
        var value = GetOptionalQueryValue(request, key);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required query value '{key}'.");
        }

        return value;
    }

    private static string? GetOptionalQueryValue(HttpRequestData request, string key)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        return query[key];
    }

    private static string BuildCompletionHtml(string appOrigin, string? authSession, string? errorMessage)
    {
        var targetOrigin = string.IsNullOrWhiteSpace(appOrigin) ? "*" : JavaScriptEncode(appOrigin);
        var payload = string.IsNullOrWhiteSpace(errorMessage)
            ? $"{{ type: 'github-app-auth-complete', authSession: '{JavaScriptEncode(authSession ?? string.Empty)}' }}"
            : $"{{ type: 'github-app-auth-complete', error: '{JavaScriptEncode(errorMessage)}' }}";

        return string.Join(
            '\n',
            "<!DOCTYPE html>",
            "<html lang=\"en\">",
            "<head>",
            "    <meta charset=\"utf-8\" />",
            "    <title>GitHub sign-in complete</title>",
            "</head>",
            "<body>",
            "    <script>",
            "        if (window.opener) {",
            $"            window.opener.postMessage({payload}, '{targetOrigin}');",
            "        }",
            "        window.close();",
            "    </script>",
            "</body>",
            "</html>");
    }

    private static string JavaScriptEncode(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
