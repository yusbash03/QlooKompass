using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Recommendations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using static KultureCompass.Application.Tests.Generation.GenerationTestData;

namespace KultureCompass.Application.Tests.Generation;

public class GrowthPlanPipelineTests
{
    private readonly ScriptedLlmClient _llm = new();
    private readonly FakeResearchService _research = new();
    private readonly InMemoryGrowthPlanStore _store = new(GrowthPlan.CreateQueued(
        "owner-1", Guid.CreateVersion7(), "idem-1", "hash-1", Snapshot(), GenerationOptions.Default, RetrievedAt));

    private GrowthPlan Plan => _store.Plan;

    private Task RunAsync(int timeoutSeconds = 30, CancellationToken ct = default) =>
        new GrowthPlanPipeline(
            _store, _llm, new BriefInterpreter(_llm), new ResearchPlanner(), _research, new RecommendationSynthesizer(_llm),
            Options.Create(new GrowthPlanPipelineSettings { MaxRepairAttempts = 2, TimeoutSeconds = timeoutSeconds }),
            TimeProvider.System, NullLogger<GrowthPlanPipeline>.Instance).RunAsync(Plan.Id, ct);

    private static JsonObject DraftWithUnknownKeyIn(string insightId, int insights = 3)
    {
        var draft = ValidDraft(insights);
        foreach (var insight in draft["insights"]!.AsArray())
        {
            if (insight!["id"]!.GetValue<string>() == insightId)
                insight["evidenceKeys"] = new JsonArray("ev_099");
        }
        return draft;
    }

    [Fact]
    public async Task Happy_path_completes_with_recommendations_experiments_and_two_llm_calls()
    {
        _llm.ThenReturn(ValidDraft());

        await RunAsync();

        Plan.Status.ShouldBe(GenerationStatus.CompletedWithLimitations); // interpretation fell back (not scripted)
        Plan.Limitations.ShouldContain(BriefInterpreter.FallbackLimitation);
        Plan.Recommendations.Count(r => r.Kind == RecommendationKind.Insight).ShouldBe(3);
        Plan.Recommendations.Single(r => r.Kind == RecommendationKind.Collaboration).Title.ShouldBe("Synthetic Lagos Streetwear Co");
        Plan.Experiments.ShouldHaveSingleItem().LinkedRecommendationId.ShouldBe("cam_1");
        Plan.Evidence.Count.ShouldBe(6);
        Plan.LlmCallCount.ShouldBe(2);
        Plan.QlooCallCount.ShouldBe(4);
        Plan.PromptVersion.ShouldBe(PromptLibrary.PromptVersion);
        Plan.LlmProvider.ShouldBe(LlmProvider.Gemini);
        Plan.LlmUsageJson.ShouldNotBeNull().ShouldContain("\"synthesize\"");
        Plan.Stage.ShouldBe(GenerationStage.Done);
        Plan.StageLog.ShouldAllBe(s => s.State == StageState.Done);
    }

    [Fact]
    public async Task Successful_interpretation_and_clean_evidence_completes_without_limitations()
    {
        _llm.Interpretation = LlmResult.Ok("""
            {"category":"Streetwear","productThemes":["graphic tees"],"aestheticKeywords":["alte"],"targetCustomerSummary":"Lagos youth",
             "objective":"Sales","constraints":[],"requestedOutputs":["campaigns"],"tagSearchTerms":["streetwear","afrobeats"],
             "suggestedSeedNames":[],"country":"Nigeria","city":"Lagos"}
            """, new LlmUsage(10, 10));
        _llm.ThenReturn(ValidDraft());

        await RunAsync();

        Plan.Status.ShouldBe(GenerationStatus.Completed);
        Plan.Limitations.ShouldBeEmpty();
        Plan.InterpretationJson.ShouldNotBeNull().ShouldContain("afrobeats");
    }

    [Fact]
    public async Task Stage_log_is_persisted_after_every_stage_in_order()
    {
        _llm.ThenReturn(ValidDraft());

        await RunAsync();

        var savedStages = _store.Saves.Select(s => s.Stage).Distinct().ToList();
        savedStages.ShouldBe([
            GenerationStage.InterpretingBrief, GenerationStage.PlanningResearch, GenerationStage.RetrievingEvidence,
            GenerationStage.EvaluatingEvidence, GenerationStage.GeneratingRecommendations, GenerationStage.ValidatingOutput,
            GenerationStage.DesigningExperiments, GenerationStage.Done,
        ]);
        Plan.StageLog.Single(s => s.Stage == GenerationStage.RetrievingEvidence).Detail.ShouldBe("2 of 2 Qloo requests complete");
    }

    [Fact]
    public async Task Invalid_json_is_repaired_and_the_plan_completes()
    {
        _llm.ThenReturn(LlmResult.Ok("{\"schemaVersion\": \"1.0\", \"summ", null)).ThenReturn(ValidDraft());

        await RunAsync();

        Plan.Status.ShouldNotBe(GenerationStatus.Failed);
        _llm.SynthesisCallCount.ShouldBe(2);
        Plan.LlmCallCount.ShouldBe(3); // interpret + synthesize + 1 repair
        var repair = _llm.Requests.Last();
        repair.SystemPrompt.ShouldContain("REPAIR MODE");
        repair.UserContent.ShouldContain("<violations>");
        repair.UserContent.ShouldContain("<evidence>");
    }

    [Fact]
    public async Task Unknown_key_after_exhausted_repairs_drops_the_item_when_minimums_still_hold()
    {
        _llm.ThenReturn(DraftWithUnknownKeyIn("ins_4", insights: 4));

        await RunAsync();

        Plan.Status.ShouldBe(GenerationStatus.CompletedWithLimitations);
        _llm.SynthesisCallCount.ShouldBe(3); // 1 + MaxRepairAttempts(2), never more
        Plan.Recommendations.Count(r => r.Kind == RecommendationKind.Insight).ShouldBe(3);
        Plan.Recommendations.ShouldNotContain(r => r.Id == "ins_4");
        Plan.Limitations.ShouldContain(RecommendationSynthesizer.DroppedItemsLimitation(1));
    }

    [Fact]
    public async Task Unknown_key_after_exhausted_repairs_fails_when_dropping_would_break_minimums()
    {
        _llm.ThenReturn(DraftWithUnknownKeyIn("ins_2"));

        await RunAsync();

        Plan.Status.ShouldBe(GenerationStatus.Failed);
        Plan.Error!.Code.ShouldBe(PlanErrorCode.LlmOutputInvalid);
        _llm.SynthesisCallCount.ShouldBe(3);
        Plan.Recommendations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Llm_quota_on_synthesis_fails_with_llm_quota_exceeded_without_repairs()
    {
        _llm.ThenReturn(LlmResult.Fail(LlmErrorKind.QuotaExceeded, "quota"));

        await RunAsync();

        Plan.Status.ShouldBe(GenerationStatus.Failed);
        Plan.Error!.Code.ShouldBe(PlanErrorCode.LlmQuotaExceeded);
        _llm.SynthesisCallCount.ShouldBe(1);
        Plan.StageLog[^1].State.ShouldBe(StageState.Failed);
    }

    [Theory]
    [InlineData(LlmErrorKind.Unavailable, PlanErrorCode.LlmUnavailable)]
    [InlineData(LlmErrorKind.RateLimited, PlanErrorCode.LlmUnavailable)]
    [InlineData(LlmErrorKind.Timeout, PlanErrorCode.Timeout)]
    [InlineData(LlmErrorKind.Refused, PlanErrorCode.LlmOutputInvalid)]
    public async Task Synthesis_provider_failures_map_to_plan_error_codes(LlmErrorKind kind, PlanErrorCode expected)
    {
        _llm.ThenReturn(LlmResult.Fail(kind, "x"));

        await RunAsync();

        Plan.Error!.Code.ShouldBe(expected);
    }

    [Fact]
    public async Task Truncated_output_goes_through_repair()
    {
        _llm.ThenReturn(LlmResult.Fail(LlmErrorKind.Truncated, "cut off")).ThenReturn(ValidDraft());

        await RunAsync();

        Plan.Status.ShouldNotBe(GenerationStatus.Failed);
        _llm.Requests.Last().UserContent.ShouldContain("cut off before it finished");
    }

    [Fact]
    public async Task Too_little_evidence_fails_with_qloo_no_evidence_and_never_calls_synthesis()
    {
        _research.OnRetrieve = _ => Task.FromResult(EvidenceSet(count: 2));
        _llm.ThenReturn(ValidDraft());

        await RunAsync();

        Plan.Status.ShouldBe(GenerationStatus.Failed);
        Plan.Error!.Code.ShouldBe(PlanErrorCode.QlooNoEvidence);
        Plan.Error.Message.ShouldBe(EvidenceEvaluator.InsufficientEvidenceMessage);
        _llm.SynthesisCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Research_limitations_make_the_plan_CompletedWithLimitations()
    {
        _research.OnRetrieve = _ => Task.FromResult(EvidenceSet(6, "R7 local places failed: Qloo unavailable"));
        _llm.ThenReturn(ValidDraft());

        await RunAsync();

        Plan.Status.ShouldBe(GenerationStatus.CompletedWithLimitations);
        Plan.Limitations.ShouldContain("R7 local places failed: Qloo unavailable");
    }

    [Fact]
    public async Task Model_restatements_of_research_limitations_are_not_duplicated()
    {
        _research.OnRetrieve = _ => Task.FromResult(EvidenceSet(6, "Qloo call budget (12) reached; remaining research operations were skipped."));
        var draft = ValidDraft();
        draft["limitations"] = new JsonArray("Qloo call budget reached; remaining research operations were skipped.", "Evidence is taste data, not sales data.");
        _llm.ThenReturn(draft);

        await RunAsync();

        Plan.Limitations.ShouldContain("Qloo call budget (12) reached; remaining research operations were skipped.");
        Plan.Limitations.ShouldNotContain("Qloo call budget reached; remaining research operations were skipped.");
        Plan.Limitations.ShouldContain("Evidence is taste data, not sales data.");
    }

    [Fact]
    public async Task The_users_own_brand_and_references_are_never_labelled_AI_suggestions()
    {
        var draft = ValidDraft();
        draft["mentionedEntities"] = new JsonArray(
            new JsonObject { ["name"] = "Synthetic Eko Threads", ["evidenceKey"] = null },      // the brand itself
            new JsonObject { ["name"] = "synthetic afrobeat star", ["evidenceKey"] = null },    // user's reference (case differs)
            new JsonObject { ["name"] = "Synthetic Invented Festival", ["evidenceKey"] = null });
        _llm.ThenReturn(draft);

        await RunAsync();

        Plan.UnlinkedMentions.Select(m => m.Name).ShouldBe(["Synthetic Invented Festival"]);
    }

    [Fact]
    public async Task Qloo_auth_failure_fails_with_qloo_auth_failed()
    {
        _research.OnRetrieve = _ => throw new QlooAuthFailedException("bad key");

        await RunAsync();

        Plan.Error!.Code.ShouldBe(PlanErrorCode.QlooAuthFailed);
    }

    [Fact]
    public async Task Exceeding_the_overall_timeout_fails_with_timeout_and_is_persisted()
    {
        _research.OnRetrieve = async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return EvidenceSet();
        };

        await RunAsync(timeoutSeconds: 1);

        Plan.Status.ShouldBe(GenerationStatus.Failed);
        Plan.Error!.Code.ShouldBe(PlanErrorCode.Timeout);
        _store.Saves[^1].Status.ShouldBe(GenerationStatus.Failed);
    }

    [Fact]
    public async Task Unexpected_exception_fails_with_internal_error_and_a_generic_message()
    {
        _research.OnRetrieve = _ => throw new InvalidOperationException("secret internals");

        await RunAsync();

        Plan.Error!.Code.ShouldBe(PlanErrorCode.InternalError);
        Plan.Error.Message.ShouldNotContain("secret internals");
    }

    [Fact]
    public async Task A_plan_that_is_not_Queued_is_skipped()
    {
        _llm.ThenReturn(ValidDraft());
        await RunAsync();
        var saves = _store.Saves.Count;

        await RunAsync(); // already terminal

        _store.Saves.Count.ShouldBe(saves);
    }

    [Fact]
    public async Task Prompts_never_contain_the_owner_id_and_wrap_data_in_delimited_blocks()
    {
        _llm.ThenReturn(ValidDraft());

        await RunAsync();

        _llm.Requests.ShouldAllBe(r => !r.UserContent.Contains("owner-1"));
        var synthesis = _llm.Requests.First(r => r.SchemaName == PromptLibrary.GrowthPlanSchemaName);
        synthesis.UserContent.ShouldContain("<brief>");
        synthesis.UserContent.ShouldContain("<evidence>[");
        synthesis.UserContent.ShouldContain("\"key\":\"ev_001\"");
        synthesis.SystemPrompt.ShouldContain("Use ONLY entities that appear in <evidence>");
    }
}
