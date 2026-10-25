using System.Text.Json.Nodes;
using KultureCompass.Application.Generation;
using Shouldly;
using static KultureCompass.Application.Tests.Generation.GenerationTestData;

namespace KultureCompass.Application.Tests.Generation;

/// <summary>Every rule in BE-04 §1 step 6.</summary>
public class OutputValidatorTests
{
    private static JsonObject Item(JsonObject draft, string section, int index = 0) => draft[section]![index]!.AsObject();

    [Fact]
    public void A_fully_valid_draft_passes_with_no_errors_or_warnings()
    {
        var result = Validate(ValidDraft());

        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
        result.IsValid.ShouldBeTrue();
    }

    // ── Rule 1: parse + schema ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"schemaVersion\": \"1.0\", \"summary\": ")]
    [InlineData("null")]
    public void Unparseable_output_is_a_document_error(string raw)
    {
        var result = OutputValidator.Validate(raw, Evidence(), new DraftLimits(), "");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ItemId.ShouldBeNull();
    }

    [Fact]
    public void Output_wrapped_in_a_markdown_code_fence_is_accepted()
    {
        var result = OutputValidator.Validate($"```json\n{ValidDraft().ToJsonString()}\n```", Evidence(), new DraftLimits(), "");

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Wrong_schema_version_and_missing_summary_are_document_errors()
    {
        var draft = ValidDraft();
        draft["schemaVersion"] = "2.0";
        draft["summary"] = new JsonObject { ["headline"] = "short", ["overview"] = "too short" };

        var errors = Validate(draft).Errors;

        errors.Count.ShouldBe(2);
        errors.ShouldAllBe(e => e.ItemId == null);
    }

    [Fact]
    public void Bad_ids_duplicates_and_missing_required_text_are_item_errors()
    {
        var draft = ValidDraft();
        Item(draft, "insights", 1)["id"] = "ins_1";       // duplicate
        Item(draft, "insights", 2)["id"] = "insight-3";   // bad pattern
        Item(draft, "campaigns")["angle"] = " ";

        var errors = Validate(draft).Errors;

        errors.ShouldContain(e => e.ItemId == "ins_1" && e.Message.Contains("Duplicate"));
        errors.ShouldContain(e => e.ItemId == "insight-3");
        errors.ShouldContain(e => e.ItemId == "cam_1" && e.Message.Contains("angle"));
    }

    // ── Rule 2 + 4: evidence keys ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Unknown_evidence_key_is_an_item_error_naming_the_key()
    {
        var draft = ValidDraft();
        Item(draft, "insights", 1)["evidenceKeys"] = new JsonArray("ev_002", "ev_099");

        var error = Validate(draft).Errors.ShouldHaveSingleItem();

        error.ItemId.ShouldBe("ins_2");
        error.Message.ShouldContain("ev_099 does not exist");
    }

    [Theory]
    [InlineData("insights")]
    [InlineData("collaborations")]
    [InlineData("campaigns")]
    public void Every_recommendation_needs_at_least_one_evidence_key(string section)
    {
        var draft = ValidDraft();
        Item(draft, section)["evidenceKeys"] = new JsonArray();
        if (section == "collaborations")
            Item(draft, section)["partnerEvidenceKey"] = "ev_999";

        Validate(draft).Errors.ShouldContain(e => e.ItemId != null && (e.Message.Contains("at least one evidence key") || e.Message.Contains("ev_999")));
    }

    [Fact]
    public void Collaboration_partner_must_exist_in_evidence()
    {
        var draft = ValidDraft();
        Item(draft, "collaborations")["partnerEvidenceKey"] = "ev_042";

        Validate(draft).Errors.ShouldContain(e => e.ItemId == "col_1" && e.Message.Contains("ev_042"));
    }

    [Fact]
    public void Collaboration_partner_cannot_be_a_tag()
    {
        var draft = ValidDraft();
        Item(draft, "collaborations")["partnerEvidenceKey"] = "ev_003"; // Synthetic Street Culture is a tag

        Validate(draft).Errors.ShouldContain(e => e.ItemId == "col_1" && e.Message.Contains("tag"));
    }

    // ── Rule 3: partner name overwrite ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Partner_name_is_overwritten_with_the_evidence_name_and_partner_key_added_to_evidence_keys()
    {
        var draft = ValidDraft();
        Item(draft, "collaborations")["partnerName"] = "synthetic lagos streetwear COMPANY";
        Item(draft, "collaborations")["evidenceKeys"] = new JsonArray("ev_001");

        var result = Validate(draft);

        result.IsValid.ShouldBeTrue();
        var collaboration = result.Draft!.Collaborations!.Single();
        collaboration.PartnerName.ShouldBe("Synthetic Lagos Streetwear Co");
        collaboration.EvidenceKeys.ShouldBe(["ev_002", "ev_001"]);
    }

    // ── Rule 5: experiments ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Experiment_must_link_to_an_existing_recommendation_and_have_a_1_to_90_day_period()
    {
        var draft = ValidDraft();
        Item(draft, "experiments")["linkedRecommendationId"] = "cam_7";
        Item(draft, "experiments")["measurementPeriodDays"] = 120;

        var errors = Validate(draft).Errors;

        errors.ShouldContain(e => e.ItemId == "exp_1" && e.Message.Contains("cam_7"));
        errors.ShouldContain(e => e.ItemId == "exp_1" && e.Message.Contains("1-90"));
    }

    [Fact]
    public void Threshold_is_always_marked_as_a_proposed_default()
    {
        var draft = ValidDraft();
        Item(draft, "experiments")["thresholdIsProposedDefault"] = false;

        Validate(draft).Draft!.Experiments!.Single().ThresholdIsProposedDefault.ShouldBe(true);
    }

    [Fact]
    public void Campaign_without_an_experiment_is_only_a_warning_because_the_designer_fills_it()
    {
        var draft = ValidDraft();
        draft["experiments"] = new JsonArray();

        var result = Validate(draft);

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldContain(w => w.Contains("cam_1 has no experiment"));
    }

    // ── Rule 6: counts ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Too_few_insights_is_a_non_trimmable_document_error()
    {
        var error = Validate(ValidDraft(insights: 2)).Errors.ShouldHaveSingleItem();

        error.ItemId.ShouldBeNull();
        error.Trimmable.ShouldBeFalse();
    }

    [Fact]
    public void Too_many_items_is_a_trimmable_error()
    {
        var draft = ValidDraft(insights: 6);
        var insights = draft["insights"]!.AsArray();
        insights.Add(insights[0]!.DeepClone());
        insights[6]!["id"] = "ins_7";

        var error = Validate(draft).Errors.ShouldHaveSingleItem();

        error.Trimmable.ShouldBeTrue();
    }

    [Fact]
    public void Collaborations_are_rejected_when_the_request_excluded_them()
    {
        var error = Validate(ValidDraft(), new DraftLimits(MaxCollaborations: 0)).Errors.ShouldHaveSingleItem();

        error.Message.ShouldContain("collaborations");
        error.Trimmable.ShouldBeTrue();
    }

    // ── Rule 7: mentioned entities ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Null_key_mentions_become_unlinked_but_names_that_are_in_evidence_never_do()
    {
        var draft = ValidDraft();
        draft["mentionedEntities"] = new JsonArray(
            new JsonObject { ["name"] = "Invented Fashion House", ["evidenceKey"] = null },
            new JsonObject { ["name"] = "Synthetic Danfo Denim", ["evidenceKey"] = null },
            new JsonObject { ["name"] = "Made Up Key Brand", ["evidenceKey"] = "ev_777" });

        var result = Validate(draft);

        result.IsValid.ShouldBeTrue();
        result.UnlinkedMentions.ShouldBe(["Invented Fashion House", "Made Up Key Brand"]);
        result.Warnings.ShouldContain(w => w.Contains("ev_777"));
    }

    [Fact]
    public void Using_a_not_retrieved_entity_as_a_collaboration_partner_is_an_error()
    {
        var draft = ValidDraft();
        Item(draft, "collaborations")["partnerName"] = "Invented Fashion House";
        draft["mentionedEntities"] = new JsonArray(new JsonObject { ["name"] = "Invented Fashion House", ["evidenceKey"] = null });

        Validate(draft).Errors.ShouldContain(e => e.ItemId == "col_1" && e.Message.Contains("not in <evidence>"));
    }

    // ── Rule 8: sanitisation ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Html_is_stripped_and_overlong_text_is_trimmed_to_schema_max()
    {
        var draft = ValidDraft();
        Item(draft, "insights")["title"] = "<script>alert(1)</script><b>Bold</b> title";
        Item(draft, "insights")["finding"] = new string('x', 800);

        var insight = Validate(draft).Draft!.Insights![0];

        insight.Title.ShouldBe("alert(1) Bold title");
        insight.Finding!.Length.ShouldBe(500);
    }

    [Fact]
    public void Invalid_effort_category_becomes_Unknown_with_a_warning()
    {
        var draft = ValidDraft();
        Item(draft, "campaigns")["effortCategory"] = "Tiny";

        var result = Validate(draft);

        result.IsValid.ShouldBeTrue();
        result.Draft!.Campaigns![0].EffortCategory.ShouldBe("Unknown");
        result.Warnings.ShouldContain(w => w.Contains("effortCategory"));
    }

    // ── Rule 9: numbers not in brief/evidence ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Expect a 35% lift in sales.")]
    [InlineData("They have 120k followers in Lagos.")]
    [InlineData("An engagement rate of 4.5 is typical.")]
    public void Ungrounded_figures_add_a_warning_and_an_illustrative_assumption_but_do_not_block(string text)
    {
        var draft = ValidDraft();
        Item(draft, "insights")["whyItMatters"] = text;

        var result = Validate(draft);

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldContain(w => w.StartsWith("ins_1 contains figures"));
        result.Draft!.Insights![0].Assumptions.ShouldNotBeNull().ShouldContain(OutputValidator.IllustrativeFiguresAssumption);
    }

    [Fact]
    public void Figures_present_in_the_grounding_text_are_not_flagged()
    {
        var draft = ValidDraft();
        Item(draft, "insights")["whyItMatters"] = "Budget allows a 35% discount.";

        Validate(draft, grounding: "{\"objectiveNotes\":\"35% discount\"}").Warnings.ShouldBeEmpty();
    }

    // ── Drop step ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DropInvalidItems_removes_offending_items_and_experiments_linked_to_them()
    {
        var draft = ValidDraft(insights: 4);
        Item(draft, "campaigns")["evidenceKeys"] = new JsonArray("ev_099");
        Item(draft, "insights", 3)["evidenceKeys"] = new JsonArray("ev_098");
        var first = Validate(draft);

        var trimmed = OutputValidator.DropInvalidItems(first.Draft!, first.Errors, new DraftLimits(), out var dropped);

        dropped.ShouldBe(3); // ins_4, cam_1 and exp_1 (linked to cam_1)
        trimmed.Insights!.Select(i => i.Id).ShouldBe(["ins_1", "ins_2", "ins_3"]);
        trimmed.Campaigns.ShouldBeEmpty();
        trimmed.Experiments.ShouldBeEmpty();
    }
}
