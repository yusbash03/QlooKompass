using KultureCompass.Application.Qloo;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using Shouldly;

namespace KultureCompass.Application.Tests.Research;

public class ResearchPlannerTests
{
    private static BriefSnapshot Snapshot(
        IReadOnlyList<ReferenceEntity>? referenceEntities = null, string? primaryLocation = "Lagos, Nigeria") => new(
        BrandName: "Test Brand",
        Description: null,
        Category: BusinessCategory.Streetwear,
        CategoryOther: null,
        ProductsDescription: "Graphic tees and hoodies",
        TargetMarket: "Young adults in Lagos",
        PrimaryLocation: primaryLocation,
        MarketingObjective: MarketingObjective.Sales,
        ObjectiveNotes: null,
        BrandIdentity: null,
        ReferenceEntities: referenceEntities ?? [],
        BudgetRange: null);

    private static BriefInterpretation Interpretation(
        IReadOnlyList<SuggestedSeedName>? suggestedSeeds = null, IReadOnlyList<string>? tagSearchTerms = null,
        string? country = "Nigeria", MarketingObjective objective = MarketingObjective.Sales) => new(
        Category: BusinessCategory.Streetwear,
        ProductThemes: [],
        AestheticKeywords: [],
        TargetCustomerSummary: "Young adults",
        Objective: objective,
        Constraints: [],
        RequestedOutputs: ["collaborations", "campaigns", "experiments"],
        TagSearchTerms: tagSearchTerms ?? ["streetwear"],
        SuggestedSeedNames: suggestedSeeds ?? [],
        Country: country,
        City: "Lagos");

    [Fact]
    public void Plan_produces_no_operations_when_the_brief_has_no_references_seeds_or_keywords()
    {
        var brief = new ResearchBrief(Snapshot(), Interpretation(tagSearchTerms: []));
        var plan = new ResearchPlanner().Plan(brief);

        plan.Tier0.ShouldBeEmpty();
        plan.Tier1.ShouldBeEmpty();
    }

    [Fact]
    public void Plan_caps_user_references_at_five_and_ai_seeds_at_three()
    {
        var references = Enumerable.Range(1, 8).Select(i => ReferenceEntity.Create($"Ref{i}", ReferenceKindHint.Artist)).ToList();
        var suggested = Enumerable.Range(1, 5).Select(i => new SuggestedSeedName($"Ai{i}", ReferenceKindHint.Artist)).ToList();
        var brief = new ResearchBrief(Snapshot(references), Interpretation(suggested, tagSearchTerms: []));

        var plan = new ResearchPlanner().Plan(brief);

        var userSeedOps = plan.Tier0.Count(op => op.Kind == ResearchOperationKind.ResolveSeed && op.SeedCandidate!.Origin == SeedOrigin.UserProvided);
        var aiSeedOps = plan.Tier0.Count(op => op.Kind == ResearchOperationKind.ResolveSeed && op.SeedCandidate!.Origin == SeedOrigin.AiSuggested);

        userSeedOps.ShouldBe(ResearchPlanner.MaxUserSeeds);
        aiSeedOps.ShouldBe(ResearchPlanner.MaxAiSeeds);
    }

    [Fact]
    public void Plan_runs_r1_and_r2_before_any_tier1_operation()
    {
        var references = new[] { ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist) };
        var brief = new ResearchBrief(Snapshot(references), Interpretation());

        var plan = new ResearchPlanner().Plan(brief);

        plan.Tier0.ShouldAllBe(op => op.Kind == ResearchOperationKind.ResolveSeed || op.Kind == ResearchOperationKind.ResolveTag);
        plan.Tier1.ShouldAllBe(op => op.Kind != ResearchOperationKind.ResolveSeed && op.Kind != ResearchOperationKind.ResolveTag);
    }

    [Fact]
    public void Plan_includes_brand_and_artist_affinities_when_seeds_exist()
    {
        var references = new[] { ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist) };
        var brief = new ResearchBrief(Snapshot(references), Interpretation());

        var plan = new ResearchPlanner().Plan(brief);

        plan.Tier1.ShouldContain(op => op.Kind == ResearchOperationKind.BrandAffinities);
        plan.Tier1.ShouldContain(op => op.Kind == ResearchOperationKind.ArtistAffinities && op.TargetEntityType == QlooEntityType.Artist);
    }

    [Fact]
    public void Plan_does_not_include_cultural_themes_or_media_when_only_tags_exist_with_no_seeds()
    {
        var brief = new ResearchBrief(Snapshot(), Interpretation(tagSearchTerms: ["streetwear"]));

        var plan = new ResearchPlanner().Plan(brief);

        plan.Tier1.ShouldContain(op => op.Kind == ResearchOperationKind.BrandAffinities);
        plan.Tier1.ShouldNotContain(op => op.Kind == ResearchOperationKind.CulturalThemes);
        plan.Tier1.ShouldNotContain(op => op.Kind == ResearchOperationKind.MediaAffinities);
    }

    [Fact]
    public void Plan_does_not_include_r4_person_affinities_unless_person_insights_are_verified()
    {
        var references = new[] { ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist) };
        var brief = new ResearchBrief(Snapshot(references), Interpretation());

        new ResearchPlanner(personInsightsVerified: false).Plan(brief).Tier1
            .ShouldNotContain(op => op.TargetEntityType == QlooEntityType.Person);

        new ResearchPlanner(personInsightsVerified: true).Plan(brief).Tier1
            .ShouldContain(op => op.TargetEntityType == QlooEntityType.Person);
    }

    [Fact]
    public void Plan_does_not_include_local_places_unless_location_insights_are_verified_and_a_location_is_set()
    {
        var references = new[] { ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist) };

        var briefWithoutVerification = new ResearchBrief(Snapshot(references), Interpretation());
        new ResearchPlanner(locationInsightsVerified: false).Plan(briefWithoutVerification).Tier1
            .ShouldNotContain(op => op.Kind == ResearchOperationKind.LocalPlaces);

        var briefWithoutLocation = new ResearchBrief(Snapshot(references, primaryLocation: null), Interpretation());
        new ResearchPlanner(locationInsightsVerified: true).Plan(briefWithoutLocation).Tier1
            .ShouldNotContain(op => op.Kind == ResearchOperationKind.LocalPlaces);

        var briefVerifiedWithLocation = new ResearchBrief(Snapshot(references), Interpretation());
        new ResearchPlanner(locationInsightsVerified: true).Plan(briefVerifiedWithLocation).Tier1
            .ShouldContain(op => op.Kind == ResearchOperationKind.LocalPlaces);
    }

    [Fact]
    public void Plan_does_not_include_demographics_unless_verified()
    {
        var references = new[] { ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist) };
        var brief = new ResearchBrief(Snapshot(references), Interpretation());

        new ResearchPlanner(demographicsVerified: false).Plan(brief).Tier1
            .ShouldNotContain(op => op.Kind == ResearchOperationKind.Demographics);

        new ResearchPlanner(demographicsVerified: true).Plan(brief).Tier1
            .ShouldContain(op => op.Kind == ResearchOperationKind.Demographics);
    }

    [Theory]
    [InlineData(MarketingObjective.Awareness, QlooEntityType.TvShow)]
    [InlineData(MarketingObjective.Launch, QlooEntityType.TvShow)]
    [InlineData(MarketingObjective.Sales, QlooEntityType.Podcast)]
    [InlineData(MarketingObjective.Engagement, QlooEntityType.Podcast)]
    [InlineData(MarketingObjective.Community, QlooEntityType.Podcast)]
    [InlineData(MarketingObjective.Partnerships, QlooEntityType.Podcast)]
    public void SelectMediaType_is_deterministic_per_objective(MarketingObjective objective, QlooEntityType expected)
    {
        ResearchPlanner.SelectMediaType(objective).ShouldBe(expected);
    }

    [Fact]
    public void Plan_is_pure_and_deterministic_for_the_same_input()
    {
        var references = new[] { ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist) };
        var brief = new ResearchBrief(Snapshot(references), Interpretation());
        var planner = new ResearchPlanner(locationInsightsVerified: true, personInsightsVerified: true, demographicsVerified: true);

        var first = planner.Plan(brief);
        var second = planner.Plan(brief);

        first.Tier0.ShouldBe(second.Tier0);
        first.Tier1.ShouldBe(second.Tier1);
    }
}
