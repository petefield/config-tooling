using System.Net;
using config_promote_api.Models;
using config_promote_api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace config_promote_api.Functions;

internal sealed class ConfigReadFunctions
{
    private readonly CorsService _corsService;
    private readonly GitHubRepositoryReadService _readService;

    public ConfigReadFunctions(CorsService corsService, GitHubRepositoryReadService readService)
    {
        _corsService = corsService;
        _readService = readService;
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
            var repository = ReadRepository(request);
            var result = await _readService.GetCatalogAsync(repository);
            return await CreateJsonAsync(request, HttpStatusCode.OK, result);
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
            var repository = ReadRepository(request);
            var outputFile = GetRequiredQueryValue(request, "path");
            var rawContent = await _readService.GetRawConfigByOutputFileAsync(repository, outputFile);

            var response = request.CreateResponse(HttpStatusCode.OK);
            _corsService.Apply(request, response);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(rawContent);
            return response;
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
            var repository = ReadRepository(request);
            var sourceFile = GetRequiredQueryValue(request, "path");
            var history = await _readService.GetHistoryAsync(repository, sourceFile);
            return await CreateJsonAsync(request, HttpStatusCode.OK, history);
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

    private HttpResponseData CreateEmpty(HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.NoContent);
        _corsService.Apply(request, response);
        return response;
    }

    private async Task<HttpResponseData> CreateJsonAsync(HttpRequestData request, HttpStatusCode statusCode, object body)
    {
        var response = request.CreateResponse(statusCode);
        _corsService.Apply(request, response);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteAsJsonAsync(body);
        return response;
    }
}
