using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Recommendations;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Llm;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.Extensions.Options;
using Shouldly;
using QlooMode = KultureCompass.Domain.Plans.QlooMode;

namespace KultureCompass.Infrastructure.Tests.Generation;

/// <summary>Full pipeline in mock mode (synthetic Qloo fixtures + deterministic mock LLM), persisted to real Postgres
/// and reloaded — proves the plan, its owned evidence, polymorphic payloads, experiments and stage_log round-trip.</summary>
[Collection(PostgresCollection.Name)]
public class MockPipelinePersistenceTests(PostgresFixture fixture)
{
    private static MockLlmClient MockLlm(MockLlmScenario scenario = MockLlmScenario.Valid) =>
        new(Options.Create(new LlmOptions { MockScenario = scenario.ToString() }));

    [Fact]
    public async Task Mock_mode_plan_completes_and_round_trips_through_postgres()
    {
        var planId = await PipelineTestHost.CreateQueuedPlanAsync(fixture, PipelineTestHost.LagosStreetwearBrief());

        await PipelineTestHost.RunAsync(fixture, planId, new MockQlooClient(), MockLlm(), QlooMode.Mock);

        var plan = await PipelineTestHost.ReloadAsync(fixture, planId);
        plan.Status.ShouldBeOneOf(GenerationStatus.Completed, GenerationStatus.CompletedWithLimitations);
        plan.IsMock.ShouldBeTrue();
        plan.LlmProvider.ShouldBe(LlmProvider.Mock);
        plan.Evidence.Count.ShouldBeGreaterThanOrEqualTo(5);
        plan.Evidence.ShouldAllBe(e => e.Name.StartsWith("Synthetic "));
        plan.Recommendations.Count(r => r.Kind == RecommendationKind.Insight).ShouldBe(3);
        plan.Recommendations.ShouldContain(r => r.Kind == RecommendationKind.Campaign && r.Payload is CampaignPayload);
        var evidenceKeys = plan.Evidence.Select(e => e.Key).ToHashSet();
        plan.Recommendations.SelectMany(r => r.EvidenceKeys).ShouldAllBe(k => evidenceKeys.Contains(k));
        plan.Experiments.ShouldNotBeEmpty();
        plan.Summary!.Headline.ShouldStartWith("[MOCK]");
        plan.StageLog.Select(s => s.Stage).ShouldContain(GenerationStage.ValidatingOutput);
        plan.StageLog.ShouldAllBe(s => s.State == StageState.Done);
        plan.LlmCallCount.ShouldBe(2);
        plan.QlooCallCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Mock_quota_scenario_persists_a_failed_plan_with_llm_quota_exceeded()
    {
        var planId = await PipelineTestHost.CreateQueuedPlanAsync(fixture, PipelineTestHost.LagosStreetwearBrief());

        await PipelineTestHost.RunAsync(fixture, planId, new MockQlooClient(), MockLlm(MockLlmScenario.QuotaExceeded), QlooMode.Mock);

        var plan = await PipelineTestHost.ReloadAsync(fixture, planId);
        plan.Status.ShouldBe(GenerationStatus.Failed);
        plan.Error!.Code.ShouldBe(PlanErrorCode.LlmQuotaExceeded);
        plan.Evidence.ShouldNotBeEmpty(); // retrieved evidence is kept for inspection even when synthesis fails
    }
}
