using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using KultureCompass.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Tests.Generation;

/// <summary>Runs the real GrowthPlanPipeline against real EF Core stores, with whichever Qloo/LLM clients the test
/// supplies (mock for CI, live for Category=Live).</summary>
internal static class PipelineTestHost
{
    public static BriefSnapshot LagosStreetwearBrief(IReadOnlyList<ReferenceEntity>? references = null) => new(
        "Synthetic Alte Row", "Lagos-based streetwear label blending Afrofuturism with everyday essentials.",
        BusinessCategory.Streetwear, null, "Graphic tees, bucket hats, and utility jackets inspired by Lagos street style.",
        "Gen Z and young millennial creatives in Lagos", "Lagos, Nigeria", MarketingObjective.Sales,
        "Launching a new capsule collection next quarter", null,
        references ?? [ReferenceEntity.Create("Synthetic Afrobeat Star", ReferenceKindHint.Artist)], BudgetRange.From100kTo500k);

    public static async Task<Guid> CreateQueuedPlanAsync(PostgresFixture fixture, BriefSnapshot brief)
    {
        await using var db = fixture.CreateContext();
        var plan = GrowthPlan.CreateQueued(
            Guid.NewGuid().ToString(), Guid.CreateVersion7(), Guid.NewGuid().ToString(), "hash", brief, GenerationOptions.Default, DateTimeOffset.UtcNow);
        db.GrowthPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    public static async Task RunAsync(
        PostgresFixture fixture, Guid planId, IQlooClient qloo, ILlmClient llm, QlooMode qlooMode, int timeoutSeconds = 180)
    {
        await using var db = fixture.CreateContext();
        var planner = new ResearchPlanner(locationInsightsVerified: true, personInsightsVerified: true);
        var pipeline = new GrowthPlanPipeline(
            new GrowthPlanStore(db), llm, new BriefInterpreter(llm), planner, new CulturalIntelligenceService(qloo, planner),
            new RecommendationSynthesizer(llm),
            Options.Create(new GrowthPlanPipelineSettings { QlooMode = qlooMode, TimeoutSeconds = timeoutSeconds }),
            TimeProvider.System, NullLogger<GrowthPlanPipeline>.Instance);
        await pipeline.RunAsync(planId, CancellationToken.None);
    }

    public static async Task<GrowthPlan> ReloadAsync(PostgresFixture fixture, Guid planId)
    {
        await using var db = fixture.CreateContext();
        return (await new GrowthPlanStore(db).GetForProcessingAsync(planId, CancellationToken.None))!;
    }
}
