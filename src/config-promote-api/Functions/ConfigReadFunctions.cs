using System.Net;
using System.Text.Json;
using config_promote_api.Models;
using config_promote_api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace config_promote_api.Functions;

internal sealed class ConfigReadFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CorsService _corsService;
    private readonly GitHubRepositoryReadService _readService;
    private readonly GitHubUserTokenService _tokenService;

    public ConfigReadFunctions(CorsService corsService, GitHubRepositoryReadService readService, GitHubUserTokenService tokenService)
    {
        _corsService = corsService;
        _readService = readService;
        _tokenService = tokenService;
    }

    [Function("ReadConfigCatalog")]
    public async Task<HttpResponseData> ReadCatalogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "configs/catalog")] HttpRequestData request)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return CreateEmpty(request);
        }

        try
        {
            var authSession = await GetAuthenticatedSessionAsync(request);
            var repository = ReadRepository(request);
            var result = await _readService.GetCatalogAsync(repository, authSession.AccessToken);
            return await CreateJsonAsync(request, HttpStatusCode.OK, result, authSession.AuthSession);
        }
        catch (AuthenticationRequiredException exception)
        {
            return await CreateJsonAsync(request, HttpStatusCode.Unauthorized, new ApiErrorResponse { Message = exception.Message });
        }
        catch (Exception exception)
        {
            return await CreateJsonAsync(request, HttpStatusCode.BadRequest, new ApiErrorResponse { Message = exception.Message });
        }
    }

    [Function("ReadConfigFile")]
    public async Task<HttpResponseData> ReadFileAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "configs/file")] HttpRequestData request)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return CreateEmpty(request);
        }

        try
        {
            var authSession = await GetAuthenticatedSessionAsync(request);
            var repository = ReadRepository(request);
            var outputFile = GetRequiredQueryValue(request, "path");
            var rawContent = await _readService.GetRawConfigByOutputFileAsync(repository, outputFile, authSession.AccessToken);

            var response = request.CreateResponse(HttpStatusCode.OK);
            _corsService.Apply(request, response);
            response.Headers.Add("X-Config-Auth-Session", authSession.AuthSession);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(rawContent);
            return response;
        }
        catch (AuthenticationRequiredException exception)
        {
            return await CreateJsonAsync(request, HttpStatusCode.Unauthorized, new ApiErrorResponse { Message = exception.Message });
        }
        catch (Exception exception)
        {
            return await CreateJsonAsync(request, HttpStatusCode.BadRequest, new ApiErrorResponse { Message = exception.Message });
        }
    }

    [Function("ReadConfigHistory")]
    public async Task<HttpResponseData> ReadHistoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "configs/history")] HttpRequestData request)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return CreateEmpty(request);
        }

        try
        {
            var authSession = await GetAuthenticatedSessionAsync(request);
            var repository = ReadRepository(request);
            var sourceFile = GetRequiredQueryValue(request, "path");
            var history = await _readService.GetHistoryAsync(repository, sourceFile, authSession.AccessToken);
            return await CreateJsonAsync(request, HttpStatusCode.OK, history, authSession.AuthSession);
        }
        catch (AuthenticationRequiredException exception)
        {
            return await CreateJsonAsync(request, HttpStatusCode.Unauthorized, new ApiErrorResponse { Message = exception.Message });
        }
        catch (Exception exception)
        {
            return await CreateJsonAsync(request, HttpStatusCode.BadRequest, new ApiErrorResponse { Message = exception.Message });
        }
    }

    private static GitHubRepository ReadRepository(HttpRequestData request) =>
        new()
        {
            Owner = GetRequiredQueryValue(request, "owner"),
            Name = GetRequiredQueryValue(request, "repo"),
            BaseBranch = GetRequiredQueryValue(request, "branch")
        };

    private static string GetRequiredQueryValue(HttpRequestData request, string key)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        var value = query[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required query value '{key}'.");
        }

        return value;
    }

    private async Task<GitHubUserTokenService.AuthenticatedGitHubSession> GetAuthenticatedSessionAsync(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("Authorization", out var values))
        {
            throw new AuthenticationRequiredException("Sign in with GitHub before browsing configs.");
        }

        var authorization = values.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthenticationRequiredException("Sign in with GitHub before browsing configs.");
        }

        var authSessionToken = authorization["Bearer ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(authSessionToken))
        {
            throw new AuthenticationRequiredException("Sign in with GitHub before browsing configs.");
        }

        return await _tokenService.GetAuthenticatedSessionAsync(authSessionToken);
    }

    private HttpResponseData CreateEmpty(HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.NoContent);
        _corsService.Apply(request, response);
        return response;
    }

    private async Task<HttpResponseData> CreateJsonAsync(HttpRequestData request, HttpStatusCode statusCode, object body, string? authSession = null)
    {
        var response = request.CreateResponse(statusCode);
        _corsService.Apply(request, response);

        if (!string.IsNullOrWhiteSpace(authSession))
        {
            response.Headers.Add("X-Config-Auth-Session", authSession);
        }

        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }
}
