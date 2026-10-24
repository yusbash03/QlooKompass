using System.Collections.Concurrent;
using KultureCompass.Application.Qloo;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using Shouldly;

namespace KultureCompass.Application.Tests.Research;

public class CulturalIntelligenceServiceTests
{
    private static readonly QlooResultMeta Meta = new(TimeSpan.Zero, RateLimitRemaining: null);

    private sealed class SyncProgress : IProgress<string>
    {
        public ConcurrentQueue<string> Reports { get; } = new();
        public void Report(string value) => Reports.Enqueue(value);
    }

    private static BriefSnapshot Snapshot(IReadOnlyList<ReferenceEntity>? referenceEntities = null, string? primaryLocation = "Lagos, Nigeria") => new(
        BrandName: "Test Brand",
        Description: null,
        Category: BusinessCategory.Streetwear,
        CategoryOther: null,
        ProductsDescription: "Graphic tees and hoodies",
        TargetMarket: "Young adults",
        PrimaryLocation: primaryLocation,
        MarketingObjective: MarketingObjective.Sales,
        ObjectiveNotes: null,
        BrandIdentity: null,
        ReferenceEntities: referenceEntities ?? [ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist)],
        BudgetRange: null);

    private static BriefInterpretation Interpretation(string? country = "Nigeria", IReadOnlyList<string>? tagSearchTerms = null) => new(
        Category: BusinessCategory.Streetwear,
        ProductThemes: [],
        AestheticKeywords: [],
        TargetCustomerSummary: "Young adults",
        Objective: MarketingObjective.Sales,
        Constraints: [],
        RequestedOutputs: ["collaborations"],
        TagSearchTerms: tagSearchTerms ?? ["streetwear"],
        SuggestedSeedNames: [],
        Country: country,
        City: "Lagos");

    private static CulturalIntelligenceService Service(FakeQlooClient client, ResearchPlanner? planner = null, int maxEvidenceItems = 60) =>
        new(client, planner ?? new ResearchPlanner(), maxEvidenceItems);

    [Fact]
    public async Task RetrieveAsync_returns_empty_evidence_with_limitations_when_qloo_finds_nothing()
    {
        var client = new FakeQlooClient(); // every call defaults to an empty success
        var brief = new ResearchBrief(Snapshot(), Interpretation());
        var budget = new ResearchBudget(12);

        var result = await Service(client).RetrieveAsync(brief, budget, progress: null, CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.Limitations.ShouldContain("Could not confidently match 'Burna Boy' in Qloo.");
        result.Limitations.ShouldContain("Could not find a Qloo tag for 'streetwear'.");
        result.CallsMade.ShouldBe(2); // R1 seed search + R2 tag search; tier1 never reserves budget because nothing resolved
    }

    [Fact]
    public async Task RetrieveAsync_tolerates_partial_failures_and_keeps_evidence_from_successful_operations()
    {
        var client = new FakeQlooClient
        {
            OnSearchEntities = _ => QlooResult<IReadOnlyList<QlooEntity>>.Ok(
                [FakeQlooClient.Entity("artist-1", "Burna Boy", "artist")], Meta),
            OnSearchTags = (_, _) => QlooResult<IReadOnlyList<QlooTag>>.Fail(QlooErrorKind.Unavailable, "tags down"),
            OnGetInsights = req => req.FilterType == QlooEntityType.Brand
                ? QlooResult<InsightsResponse>.Ok(new InsightsResponse([FakeQlooClient.Entity("brand-1", "Daily Paper", "brand", affinity: 0.9)], []), Meta)
                : QlooResult<InsightsResponse>.Fail(QlooErrorKind.RateLimited, "rate limited"),
        };
        var brief = new ResearchBrief(Snapshot(), Interpretation());
        var budget = new ResearchBudget(12);

        var result = await Service(client).RetrieveAsync(brief, budget, progress: null, CancellationToken.None);

        result.Items.ShouldHaveSingleItem();
        result.Items[0].Name.ShouldBe("Daily Paper");
        result.Limitations.ShouldContain(l => l.Contains("rate limited"));
        result.Limitations.ShouldContain("Could not find a Qloo tag for 'streetwear'.");
    }

    [Fact]
    public async Task RetrieveAsync_throws_and_aborts_immediately_on_auth_failure()
    {
        var client = new FakeQlooClient
        {
            OnSearchEntities = _ => QlooResult<IReadOnlyList<QlooEntity>>.Fail(QlooErrorKind.AuthFailed, "bad key"),
        };
        var brief = new ResearchBrief(Snapshot(), Interpretation());
        var budget = new ResearchBudget(12);

        await Should.ThrowAsync<QlooAuthFailedException>(() => Service(client).RetrieveAsync(brief, budget, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_skips_an_ambiguous_seed_with_no_kind_hint()
    {
        var client = new FakeQlooClient
        {
            OnSearchEntities = _ => QlooResult<IReadOnlyList<QlooEntity>>.Ok(
                [FakeQlooClient.Entity("brand-nike", "Nike", "brand"), FakeQlooClient.Entity("artist-nike", "Nike", "artist")],
                Meta),
        };
        var references = new[] { ReferenceEntity.Create("Nike", ReferenceKindHint.Unknown) };
        var brief = new ResearchBrief(Snapshot(references), Interpretation(tagSearchTerms: []));
        var budget = new ResearchBudget(12);

        var result = await Service(client).RetrieveAsync(brief, budget, progress: null, CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.Limitations.ShouldContain("Could not confidently match 'Nike' in Qloo.");
    }

    [Fact]
    public async Task RetrieveAsync_discards_local_places_results_when_the_echoed_country_does_not_match()
    {
        var client = new FakeQlooClient
        {
            OnSearchEntities = _ => QlooResult<IReadOnlyList<QlooEntity>>.Ok(
                [FakeQlooClient.Entity("artist-1", "Burna Boy", "artist")], Meta),
            OnGetInsights = req => req.FilterType == QlooEntityType.Place
                ? QlooResult<InsightsResponse>.Ok(
                    new InsightsResponse([FakeQlooClient.Entity("place-1", "Some Place", "place")], [new QlooLocality("Paris", "France")]),
                    Meta)
                : QlooResult<InsightsResponse>.Ok(new InsightsResponse([], []), Meta),
        };
        var planner = new ResearchPlanner(locationInsightsVerified: true);
        var brief = new ResearchBrief(Snapshot(), Interpretation());
        var budget = new ResearchBudget(12);

        var result = await Service(client, planner).RetrieveAsync(brief, budget, progress: null, CancellationToken.None);

        result.Items.ShouldNotContain(i => i.Name == "Some Place");
        result.Limitations.ShouldContain("Qloo could not resolve 'Lagos, Nigeria' reliably; local venue suggestions omitted.");
    }

    [Fact]
    public async Task RetrieveAsync_never_exceeds_the_call_budget()
    {
        var client = new FakeQlooClient
        {
            OnSearchEntities = _ => QlooResult<IReadOnlyList<QlooEntity>>.Ok(
                [FakeQlooClient.Entity("artist-1", "Burna Boy", "artist")], Meta),
            OnSearchTags = (_, _) => QlooResult<IReadOnlyList<QlooTag>>.Ok([FakeQlooClient.Tag("tag-1", "streetwear")], Meta),
            OnGetInsights = _ => QlooResult<InsightsResponse>.Ok(new InsightsResponse([], []), Meta),
        };
        var planner = new ResearchPlanner(locationInsightsVerified: true, personInsightsVerified: true, demographicsVerified: true);
        var brief = new ResearchBrief(Snapshot(), Interpretation());
        var budget = new ResearchBudget(2); // far fewer than the ~9 ops this plan would otherwise need

        var result = await Service(client, planner).RetrieveAsync(brief, budget, progress: null, CancellationToken.None);

        budget.CallsMade.ShouldBeLessThanOrEqualTo(2);
        result.CallsMade.ShouldBeLessThanOrEqualTo(2);
        result.Limitations.ShouldContain(l => l.Contains("budget"));
    }

    [Fact]
    public async Task RetrieveAsync_assigns_evidence_keys_deterministically_by_operation_order_then_affinity_then_name()
    {
        var client = new FakeQlooClient
        {
            OnSearchEntities = _ => QlooResult<IReadOnlyList<QlooEntity>>.Ok(
                [FakeQlooClient.Entity("artist-1", "Burna Boy", "artist")], Meta),
            OnSearchTags = (_, _) => QlooResult<IReadOnlyList<QlooTag>>.Fail(QlooErrorKind.Unavailable, "down"),
            OnGetInsights = req => req.FilterType switch
            {
                QlooEntityType.Brand => QlooResult<InsightsResponse>.Ok(
                    new InsightsResponse(
                        [
                            FakeQlooClient.Entity("brand-low", "Zebra Brand", "brand", affinity: 0.5),
                            FakeQlooClient.Entity("brand-high", "Alpha Brand", "brand", affinity: 0.95),
                        ], []),
                    Meta),
                QlooEntityType.Artist => QlooResult<InsightsResponse>.Ok(
                    new InsightsResponse([FakeQlooClient.Entity("artist-2", "Some Artist", "artist", affinity: 0.99)], []),
                    Meta),
                _ => QlooResult<InsightsResponse>.Ok(new InsightsResponse([], []), Meta),
            },
        };
        var brief = new ResearchBrief(Snapshot(), Interpretation());
        var budget = new ResearchBudget(12);

        var result = await Service(client).RetrieveAsync(brief, budget, progress: null, CancellationToken.None);

        // R3 (brand affinities) is planned before R4 (artist affinities); within R3, higher affinity sorts first.
        result.Items.Select(i => i.Key).ShouldBe(["ev_001", "ev_002", "ev_003"]);
        result.Items.Select(i => i.Name).ShouldBe(["Alpha Brand", "Zebra Brand", "Some Artist"]);
    }

    [Fact]
    public async Task RetrieveAsync_reports_progress_once_per_planned_operation()
    {
        var client = new FakeQlooClient(); // nothing resolves, so tier1's 4 ops are all skipped (still reported)
        var brief = new ResearchBrief(Snapshot(), Interpretation());
        var budget = new ResearchBudget(12);
        var progress = new SyncProgress();

        await Service(client).RetrieveAsync(brief, budget, progress, CancellationToken.None);

        // Tier0: R1 seed search + R2 tag search = 2. Tier1: R3/R4/R5/R6 planned (seed+tag were queued) but all skip
        // at execution time since nothing resolved = 4 more. Total planned = 6.
        progress.Reports.Count.ShouldBe(6);
        progress.Reports.ShouldContain("6 of 6 Qloo requests complete");
    }
}
