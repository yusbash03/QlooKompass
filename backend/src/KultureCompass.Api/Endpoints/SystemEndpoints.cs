using System.Text.Json;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KultureCompass.Api.Endpoints;

public static class SystemEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapGet("/health", () => Results.Ok(new HealthDto("Healthy", [])))
            .WithName("getHealth")
            .AllowAnonymous();

        group.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse,
        }).AllowAnonymous();

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
            .WithName("getMeta")
            .AllowAnonymous()
            .RequireRateLimiting(Auth.AuthServiceCollectionExtensions.PublicRateLimitPolicy);
    }

    private static async Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var status = report.Status switch
        {
            HealthStatus.Healthy => "Healthy",
            HealthStatus.Degraded => "Degraded",
            _ => "Unhealthy",
        };
        var checks = report.Entries.Select(e => new HealthCheckDto(e.Key, e.Value.Status.ToString())).ToArray();
        context.Response.StatusCode = report.Status == HealthStatus.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new HealthDto(status, checks), JsonOptions));
    }
}

public sealed record HealthDto(string Status, HealthCheckDto[] Checks);

public sealed record HealthCheckDto(string Name, string Status);

public sealed record MetaDto(string AppName, string Version, string QlooMode, string LlmProvider, bool IsMockMode, FeatureFlagsDto Features);

public sealed record FeatureFlagsDto(bool EvaluationMode, bool SamplePlan, bool ExperimentTracking);
