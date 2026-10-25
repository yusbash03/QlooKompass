using KultureCompass.Api.Auth;
using KultureCompass.Api.Http;
using KultureCompass.Application.Common;
using KultureCompass.Application.Plans;
using KultureCompass.Domain.Plans;
using Microsoft.AspNetCore.Mvc;

namespace KultureCompass.Api.Endpoints;

public static class GrowthPlanEndpoints
{
    public const string BasePath = "/api/v1/growth-plans";

    public static void MapGrowthPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath).WithTags("GrowthPlans");

        group.MapPost("", async ([FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, StartGrowthPlanRequest body,
                StartGrowthPlanHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new StartGrowthPlanCommand(idempotencyKey, body), ct)).ToHttpResult(started => started.IsTerminalReplay
                    ? TypedResults.Ok(started.Plan)
                    : TypedResults.Accepted($"{BasePath}/{started.Plan.Id}", started.Plan)))
            .WithName("startGrowthPlan")
            .RequireRateLimiting(AuthServiceCollectionExtensions.GenerationRateLimitPolicy)
            .Produces<GrowthPlanAcceptedDto>(StatusCodes.Status202Accepted)
            .Produces<GrowthPlanAcceptedDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("", async (ListGrowthPlansHandler handler, CancellationToken ct, int page = 1, int pageSize = 20,
                Guid? businessProfileId = null, GenerationStatus? status = null) =>
                (await handler.HandleAsync(new ListPlansQuery(page, pageSize, businessProfileId, status), ct)).ToHttpResult(TypedResults.Ok))
            .WithName("listGrowthPlans")
            .Produces<PagedResult<GrowthPlanListItem>>();

        group.MapGet("/{id:guid}", async (Guid id, GetGrowthPlanHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult(TypedResults.Ok))
            .WithName("getGrowthPlan")
            .Produces<GrowthPlanDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/status", async (Guid id, GetGrowthPlanStatusHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult(TypedResults.Ok))
            .WithName("getGrowthPlanStatus")
            .Produces<GrowthPlanStatusDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/me/quota", async (GetQuotaHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(TypedResults.Ok))
            .WithTags("System")
            .WithName("getMyQuota")
            .Produces<QuotaDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
