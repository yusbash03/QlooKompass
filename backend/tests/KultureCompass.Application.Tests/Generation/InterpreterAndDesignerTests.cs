using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Domain.Profiles;
using Shouldly;
using static KultureCompass.Application.Tests.Generation.GenerationTestData;

namespace KultureCompass.Application.Tests.Generation;

public class BriefInterpreterTests
{
    private static async Task<InterpretationOutcome> InterpretAsync(LlmResult result)
    {
        var llm = new ScriptedLlmClient { Interpretation = result };
        return await new BriefInterpreter(llm).InterpretAsync(Snapshot(), CancellationToken.None);
    }

    [Fact]
    public async Task Valid_output_is_used_but_user_stated_category_and_objective_always_win()
    {
        var outcome = await InterpretAsync(LlmResult.Ok("""
            {"category":"Beauty","productThemes":["graphic tees"],"aestheticKeywords":["alte","see https://x.example"],
             "targetCustomerSummary":"Young Lagos creatives","objective":"Awareness","constraints":["budget under 500k"],
             "requestedOutputs":["campaigns"],"tagSearchTerms":["Streetwear","Afrobeats","www.spam.example","skate"],
             "suggestedSeedNames":[{"name":"Synthetic Afrobeat Star","kindHint":"Artist"},{"name":"Synthetic Seed Label","kindHint":"Brand"}],
             "country":"Ghana","city":"Accra"}
            """, null));

        outcome.Limitation.ShouldBeNull();
        var i = outcome.Interpretation;
        i.Category.ShouldBe(BusinessCategory.Streetwear);
        i.Objective.ShouldBe(MarketingObjective.Sales);
        i.Country.ShouldBe("Nigeria"); // from the user's "Lagos, Nigeria", not the model
        i.City.ShouldBe("Lagos");
        i.TagSearchTerms.ShouldBe(["streetwear", "afrobeats", "skate"]); // URL term dropped, ≤ 3
        i.AestheticKeywords.ShouldBe(["alte"]);
        i.SuggestedSeedNames.ShouldHaveSingleItem().Name.ShouldBe("Synthetic Seed Label"); // user's own reference not repeated
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Llm_failure_or_garbage_falls_back_deterministically_with_a_limitation(bool providerFailure)
    {
        var outcome = await InterpretAsync(providerFailure
            ? LlmResult.Fail(LlmErrorKind.QuotaExceeded, "quota")
            : LlmResult.Ok("not json", null));

        outcome.Limitation.ShouldBe(BriefInterpreter.FallbackLimitation);
        outcome.Interpretation.SuggestedSeedNames.ShouldBeEmpty();
        outcome.Interpretation.TagSearchTerms.ShouldBe(["streetwear", "sales"]);
        outcome.Call.Succeeded.ShouldBeFalse();
    }
}

public class ExperimentDesignerTests
{
    private static GrowthPlanDraft Draft(JsonObject json) => Validate(json).Draft!;

    [Fact]
    public void Keeps_draft_experiments_and_always_marks_thresholds_as_proposed_defaults()
    {
        var experiment = ExperimentDesigner.Design(Draft(ValidDraft()), Snapshot()).ShouldHaveSingleItem();

        experiment.Id.ShouldBe("exp_1");
        experiment.LinkedRecommendationId.ShouldBe("cam_1");
        experiment.ThresholdIsProposedDefault.ShouldBeTrue();
    }

    [Fact]
    public void Campaigns_without_an_experiment_get_a_deterministic_template()
    {
        var json = ValidDraft();
        json["experiments"] = new JsonArray();

        var experiment = ExperimentDesigner.Design(Draft(json), Snapshot()).ShouldHaveSingleItem();

        experiment.LinkedRecommendationId.ShouldBe("cam_1");
        experiment.Hypothesis.ShouldBe("If we run Behind-the-scenes music culture for Lagos 18-30, Link clicks will improve.");
        experiment.PrimaryMetric.ShouldBe("Link clicks");
        experiment.MeasurementPeriodDays.ShouldBe(ExperimentDesigner.DefaultPeriodDays);
        experiment.SuccessThreshold.ShouldBe(ExperimentDesigner.DefaultThreshold);
        experiment.ThresholdIsProposedDefault.ShouldBeTrue();
    }
}
