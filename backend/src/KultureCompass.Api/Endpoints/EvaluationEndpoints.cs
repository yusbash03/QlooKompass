using KultureCompass.Api.Auth;
using KultureCompass.Api.Http;
using KultureCompass.Application.Evaluation;
using KultureCompass.Application.Plans;
using KultureCompass.Application.Samples;
using Microsoft.AspNetCore.Mvc;

namespace KultureCompass.Api.Endpoints;

/// <summary>BE-8: experiment tracking (stretch), evaluation mode and the public sample plan — each behind its flag.</summary>
public static class EvaluationEndpoints
{
    public static void MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/v1/growth-plans/{id:guid}/experiments/{experimentId:regex(^exp_[1-9]$)}", async (Guid id, string experimentId,
                [FromHeader(Name = "If-Match")] string? ifMatch, UpdateExperimentRequest body, UpdateExperimentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateExperimentCommand(id, experimentId, ifMatch, body), ct))
                    .ToHttpResult(dto => new ETagResult(TypedResults.Ok(dto), dto.Version)))
            .WithTags("GrowthPlans")
            .WithName("updateExperiment")
            .RequireFeature(f => f.ExperimentTracking, "Experiment tracking")
            .Produces<ExperimentDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        app.MapGet("/api/v1/experiments", async (ListExperimentsHandler handler, CancellationToken ct, int page = 1, int pageSize = 20,
                Domain.Experiments.ExperimentStatus? status = null) =>
                (await handler.HandleAsync(new ListExperimentsQuery(page, pageSize, status), ct)).ToHttpResult(TypedResults.Ok))
            .WithTags("GrowthPlans")
            .WithName("listExperiments")
            .Produces<Application.Common.PagedResult<ExperimentSummaryDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        var evaluations = app.MapGroup("/api/v1/evaluations").WithTags("Evaluations")
            .RequireFeature(f => f.EvaluationMode, "Evaluation mode");

        evaluations.MapPost("", async ([FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, StartEvaluationRequest body,
                StartEvaluationHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(idempotencyKey, body, ct)).ToHttpResult(dto =>
                    TypedResults.Accepted($"/api/v1/evaluations/{dto.Id}", dto)))
            .WithName("startEvaluation")
            .RequireRateLimiting(AuthServiceCollectionExtensions.GenerationRateLimitPolicy)
            .Produces<EvaluationDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        evaluations.MapGet("/{id:guid}", async (Guid id, GetEvaluationHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult(TypedResults.Ok))
            .WithName("getEvaluation")
            .Produces<EvaluationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/api/v1/sample-plan", () => TypedResults.Text(SamplePlan.Json, "application/json"))
            .WithTags("GrowthPlans")
            .WithName("getSamplePlan")
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceCollectionExtensions.PublicRateLimitPolicy)
            .RequireFeature(f => f.SamplePlan, "The sample plan")
            .Produces<GrowthPlanDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
