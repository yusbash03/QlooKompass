using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KultureCompass.Api.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapGet("/health", () => TypedResults.Ok(new HealthDto("Healthy", [])))
            .WithTags("System")
            .WithName("getHealth")
            .AllowAnonymous()
            .Produces<HealthDto>();

        // A normal endpoint (not MapHealthChecks) so it appears in the OpenAPI document like the contract says.
        group.MapGet("/health/ready", async (HealthCheckService health, CancellationToken ct) =>
            {
                var report = await health.CheckHealthAsync(ct);
                var dto = new HealthDto(
                    report.Status switch { HealthStatus.Healthy => "Healthy", HealthStatus.Degraded => "Degraded", _ => "Unhealthy" },
                    [.. report.Entries.Select(e => new HealthCheckDto(e.Key, e.Value.Status.ToString()))]);
                return report.Status == HealthStatus.Healthy
                    ? Results.Ok(dto)
                    : Results.Json(dto, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithTags("System")
            .WithName("getReadiness")
            .AllowAnonymous()
            .Produces<HealthDto>()
            .Produces<HealthDto>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/meta", (IOptions<QlooOptions> qloo, IOptions<LlmOptions> llm, IOptions<FeatureOptions> features) =>
        {
            var qlooIsMock = qloo.Value.Mode == QlooMode.Mock;
            var isMockMode = qlooIsMock || llm.Value.IsMock;
            var meta = new MetaDto(
                AppName: "Kulture Compass",
                Version: "1.0.0",
                QlooMode: qloo.Value.Mode.ToString(),
                LlmProvider: llm.Value.Provider,
                IsMockMode: isMockMode,
                Features: new FeatureFlagsDto(features.Value.EvaluationMode, features.Value.SamplePlan, features.Value.ExperimentTracking));
            return Results.Ok(meta);
        })
            .WithTags("System")
            .WithName("getMeta")
            .AllowAnonymous()
            .Produces<MetaDto>()
            .RequireRateLimiting(Auth.AuthServiceCollectionExtensions.PublicRateLimitPolicy);
    }
}

public sealed record HealthDto(string Status, HealthCheckDto[] Checks);

public sealed record HealthCheckDto(string Name, string Status);

public sealed record MetaDto(string AppName, string Version, string QlooMode, string LlmProvider, bool IsMockMode, FeatureFlagsDto Features);

public sealed record FeatureFlagsDto(bool EvaluationMode, bool SamplePlan, bool ExperimentTracking);
