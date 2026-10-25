using KultureCompass.Application.Evaluation;
using KultureCompass.Application.Generation;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using Shouldly;

namespace KultureCompass.Application.Tests.Evaluation;

public class EvaluationScorerTests
{
    private static readonly string[] BriefTerms = ["Streetwear", "Sales", "Lagos", "Nigeria"];

    private static ScoringSubject Subject(params (string Id, string Text, int Keys)[] recs) =>
        new([.. recs.Select(r => new ScoredRecommendation(r.Id, [r.Text], r.Keys))], []);

    [Fact]
    public void Grounded_output_counts_evidence_names_found_in_text_as_traceable()
    {
        var subject = new ScoringSubject(
            [
                new("ins_1", ["Fans of Synthetic Afrobeat Star love Lagos streetwear."], 1),
                new("col_1", ["Partner with Synthetic Danfo Denim."], 2),
            ],
            ["Synthetic Danfo Denim", "Synthetic Invented Festival"]);

        var m = EvaluationScorer.Score(subject, ["Synthetic Afrobeat Star", "Synthetic Danfo Denim", "Synthetic Unused"], "", BriefTerms);

        m.NamedEntities.ShouldBe(3);
        m.TraceableEntities.ShouldBe(2);
        m.UnsupportedEntities.ShouldBe(1);
        m.RecommendationsWithEvidencePct.ShouldBe(100);
        m.AvgEntitiesPerRecommendation.ShouldBe(1);
        m.BriefTermCoveragePct.ShouldBe(50); // streetwear + Lagos
    }

    [Fact]
    public void Llm_only_output_has_zero_traceable_and_no_evidence_percentage()
    {
        var subject = new ScoringSubject([new("ins_1", ["Collaborate with Synthetic Invented Label for sales in Nigeria."], 0)], ["Synthetic Invented Label"]);

        var m = EvaluationScorer.Score(subject, [], "", BriefTerms);

        m.NamedEntities.ShouldBe(1);
        m.TraceableEntities.ShouldBe(0);
        m.UnsupportedEntities.ShouldBe(1);
        m.RecommendationsWithEvidencePct.ShouldBeNull();
    }

    [Fact]
    public void Numeric_claims_absent_from_the_grounding_text_are_counted()
    {
        var subject = Subject(("cam_1", "Expect a 40% lift and 10k followers; the 15% discount stays.", 1));

        EvaluationScorer.Score(subject, ["x"], "{\"objectiveNotes\":\"15% discount\"}", BriefTerms).UnsupportedNumericClaims.ShouldBe(2);
    }

    [Fact]
    public void Brief_terms_come_from_category_objective_and_location()
    {
        var brief = new BriefSnapshot("Synthetic", null, BusinessCategory.Other, "Jewellery", "Handmade pieces", null, "Abuja, Nigeria",
            MarketingObjective.Community, null, null, [], null);

        EvaluationScorer.BriefTerms(brief).ShouldBe(["Jewellery", "Community", "Abuja", "Nigeria"]);
    }

    [Fact]
    public void FromDraft_declares_mentioned_entities_and_partners()
    {
        var draft = new GrowthPlanDraft
        {
            Insights = [new InsightDraft { Id = "ins_1", Title = "t", Finding = "f", WhyItMatters = "w" }],
            Collaborations = [new CollaborationDraft { Id = "col_1", PartnerName = "Synthetic Partner" }],
            MentionedEntities = [new MentionedEntityDraft { Name = "Synthetic Mention" }],
        };

        var subject = EvaluationScorer.FromDraft(draft);

        subject.Recommendations.Count.ShouldBe(2);
        subject.DeclaredEntityNames.ShouldBe(["Synthetic Mention", "Synthetic Partner"], ignoreOrder: true);
    }
}
