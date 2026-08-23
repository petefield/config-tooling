using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using config_promote_api.Models;
using config_promote_api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace config_promote_api.Functions;

internal sealed class PromoteFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CorsService _corsService;
    private readonly GitHubUserTokenService _tokenService;
    private readonly GitHubRepositoryPromotionService _promotionService;

    public PromoteFunctions(
        CorsService corsService,
        GitHubUserTokenService tokenService,
        GitHubRepositoryPromotionService promotionService)
    {
        _corsService = corsService;
        _tokenService = tokenService;
        _promotionService = promotionService;
    }

    [Function("PromoteConfig")]
    public async Task<HttpResponseData> PromoteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "promote")] HttpRequestData request)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return CreateEmpty(request, HttpStatusCode.NoContent);
        }

        try
        {
            var promoteRequest = await JsonSerializer.DeserializeAsync<PromoteConfigRequest>(request.Body, JsonOptions)
                ?? throw new InvalidOperationException("The promote request body is missing or invalid.");

            var authSession = await _tokenService.GetAuthenticatedSessionAsync(promoteRequest.AuthSession);
            var result = await _promotionService.PromoteAsync(
                authSession.AccessToken,
                promoteRequest.Repository,
                promoteRequest.Entry,
                promoteRequest.FileContents);

            return await CreateJsonAsync(
                request,
                HttpStatusCode.OK,
                new PromoteConfigResponse
                {
                    BranchName = result.BranchName,
                    PullRequestNumber = result.PullRequestNumber,
                    PullRequestUrl = result.PullRequestUrl,
                    AuthSession = authSession.AuthSession
                });
        }
        catch (AuthenticationRequiredException exception)
        {
            return await CreateJsonAsync(
                request,
                HttpStatusCode.Unauthorized,
                new ApiErrorResponse { Message = exception.Message });
        }
        catch (Exception exception)
        {
            return await CreateJsonAsync(
                request,
                HttpStatusCode.BadRequest,
                new ApiErrorResponse { Message = exception.Message });
        }
    }

    private HttpResponseData CreateEmpty(HttpRequestData request, HttpStatusCode statusCode)
    {
        var response = request.CreateResponse(statusCode);
        _corsService.Apply(request, response);
        return response;
    }

    private async Task<HttpResponseData> CreateJsonAsync(HttpRequestData request, HttpStatusCode statusCode, object body)
    {
        var response = request.CreateResponse(statusCode);
        _corsService.Apply(request, response);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }
}
