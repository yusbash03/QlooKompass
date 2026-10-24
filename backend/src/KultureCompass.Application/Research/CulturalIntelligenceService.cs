using System.Collections.Concurrent;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Qloo;
using KultureCompass.Domain.Evidence;

namespace KultureCompass.Application.Research;

/// <summary>Executes a <see cref="ResearchPlan"/> against Qloo (BE-03 §4): bounded concurrency, partial-failure
/// tolerance, normalisation/dedupe, deterministic <c>ev_###</c> key assignment, and a hard cap on evidence count.
/// The LLM never reaches this — only <see cref="ResearchPlanner"/>'s fixed op menu does.</summary>
public sealed class CulturalIntelligenceService(
    IQlooClient qlooClient,
    ResearchPlanner planner,
    int maxEvidenceItems = 60,
    double strongAffinityThreshold = EvidenceQualityRules.DefaultStrongAffinityThreshold,
    TimeProvider? timeProvider = null) : ICulturalIntelligenceService
{
    private const int Concurrency = 3;
    private static readonly string[] CulturalThemeTagTypes = ["urn:tag:keyword:qloo", "urn:tag:genre:qloo"];

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<EvidenceSet> RetrieveAsync(ResearchBrief brief, ResearchBudget budget, IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(budget);

        var plan = planner.Plan(brief);
        if (plan.TotalOperations == 0)
            return EvidenceSet.Empty;

        var limitations = new ConcurrentDictionary<string, byte>();
        var operationsExecuted = new ConcurrentBag<string>();
        var resolvedSeeds = new ConcurrentBag<ResolvedSeed>();
        var resolvedTags = new ConcurrentBag<(string TagId, string Keyword)>();
        var rawHits = new ConcurrentBag<RawEvidenceHit>();

        var totalPlanned = plan.TotalOperations;
        var done = 0;
        var budgetExhausted = 0;

        void AddLimitation(string message) => limitations.TryAdd(message, 0);
        void ReportProgress() => progress?.Report($"{Interlocked.Increment(ref done)} of {totalPlanned} Qloo requests complete");

        await RunTierAsync(plan.Tier0, async (op, token) =>
        {
            if (!budget.TryReserve())
            {
                Interlocked.Exchange(ref budgetExhausted, 1);
                ReportProgress();
                return;
            }

            if (op.Kind == ResearchOperationKind.ResolveSeed)
                await ResolveSeedAsync(op, resolvedSeeds, AddLimitation, operationsExecuted, token).ConfigureAwait(false);
            else
                await ResolveTagAsync(op, resolvedTags, AddLimitation, operationsExecuted, token).ConfigureAwait(false);

            ReportProgress();
        }, ct).ConfigureAwait(false);

        var seedList = resolvedSeeds.ToList();
        var seedIds = seedList.Select(s => s.Entity.Id).Distinct().ToList();
        var tagIds = resolvedTags.Select(t => t.TagId).Distinct().ToList();
        var seedNames = seedList.Select(s => s.Candidate.Name).Distinct().ToList();
        var tagKeywords = resolvedTags.Select(t => t.Keyword).Distinct().ToList();
        var hasUserSeed = seedList.Any(s => s.Candidate.Origin == SeedOrigin.UserProvided);
        var hasAiSeed = seedList.Any(s => s.Candidate.Origin == SeedOrigin.AiSuggested);

        var tier1WithOrder = plan.Tier1.Select((op, index) => (Op: op, Order: index)).ToList();

        await RunTierAsync(tier1WithOrder, async (entry, token) =>
        {
            await ExecuteTier1OperationAsync(
                entry.Op, entry.Order, brief, seedIds, tagIds, hasUserSeed, hasAiSeed,
                budget, rawHits, AddLimitation, operationsExecuted, ReportProgress,
                () => Interlocked.Exchange(ref budgetExhausted, 1), token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        if (Volatile.Read(ref budgetExhausted) == 1)
            AddLimitation($"Qloo call budget ({budget.MaxCalls}) reached; remaining research operations were skipped.");

        var items = BuildEvidenceItems(rawHits.ToList(), seedNames, tagKeywords, maxEvidenceItems, strongAffinityThreshold);

        return new EvidenceSet(items, limitations.Keys.ToList(), budget.CallsMade, operationsExecuted.Distinct().ToList());
    }

    private static async Task RunTierAsync<T>(IReadOnlyList<T> items, Func<T, CancellationToken, ValueTask> work, CancellationToken ct)
    {
        if (items.Count == 0)
            return;

        try
        {
            await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = ct }, work)
                .ConfigureAwait(false);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Any(e => e is QlooAuthFailedException))
        {
            throw ex.InnerExceptions.OfType<QlooAuthFailedException>().First();
        }
    }

    private async Task ResolveSeedAsync(
        PlannedOperation op, ConcurrentBag<ResolvedSeed> resolvedSeeds, Action<string> addLimitation,
        ConcurrentBag<string> operationsExecuted, CancellationToken ct)
    {
        var candidate = op.SeedCandidate!;
        var request = new EntitySearchRequest { Query = candidate.Name, Types = SeedMatcher.SearchTypesFor(candidate.KindHint), Take = 3 };
        var result = await qlooClient.SearchEntitiesAsync(request, ct).ConfigureAwait(false);
        operationsExecuted.Add(op.Label);

        if (result is QlooResult<IReadOnlyList<QlooEntity>>.Failure failure)
        {
            if (failure.Kind == QlooErrorKind.AuthFailed)
                throw new QlooAuthFailedException($"Qloo search for '{candidate.Name}' failed authentication.");
            addLimitation($"Could not confidently match '{candidate.Name}' in Qloo.");
            return;
        }

        var hits = ((QlooResult<IReadOnlyList<QlooEntity>>.Success)result).Value;
        var match = SeedMatcher.Match(candidate, hits);
        if (match.Resolved is not null)
            resolvedSeeds.Add(match.Resolved);
        else if (match.Limitation is not null)
            addLimitation(match.Limitation);
    }

    private async Task ResolveTagAsync(
        PlannedOperation op, ConcurrentBag<(string TagId, string Keyword)> resolvedTags, Action<string> addLimitation,
        ConcurrentBag<string> operationsExecuted, CancellationToken ct)
    {
        var keyword = op.Keyword!;
        var result = await qlooClient.SearchTagsAsync(keyword, take: 5, ct).ConfigureAwait(false);
        operationsExecuted.Add(op.Label);

        if (result is QlooResult<IReadOnlyList<QlooTag>>.Failure failure)
        {
            if (failure.Kind == QlooErrorKind.AuthFailed)
                throw new QlooAuthFailedException($"Qloo tag lookup for '{keyword}' failed authentication.");
            addLimitation($"Could not find a Qloo tag for '{keyword}'.");
            return;
        }

        var tags = ((QlooResult<IReadOnlyList<QlooTag>>.Success)result).Value;
        var topTag = tags.Count > 0 ? tags[0] : null;
        if (topTag is not null)
            resolvedTags.Add((topTag.Id, keyword));
        else
            addLimitation($"Could not find a Qloo tag for '{keyword}'.");
    }

    private async Task ExecuteTier1OperationAsync(
        PlannedOperation op, int order, ResearchBrief brief, List<string> seedIds, List<string> tagIds,
        bool hasUserSeed, bool hasAiSeed, ResearchBudget budget, ConcurrentBag<RawEvidenceHit> rawHits,
        Action<string> addLimitation, ConcurrentBag<string> operationsExecuted, Action reportProgress,
        Action markBudgetExhausted, CancellationToken ct)
    {
        var requiresSeedSignal = op.Kind is ResearchOperationKind.CulturalThemes or ResearchOperationKind.MediaAffinities
            or ResearchOperationKind.LocalPlaces or ResearchOperationKind.Demographics;
        var allowsTagFallback = op.Kind is ResearchOperationKind.BrandAffinities or ResearchOperationKind.ArtistAffinities;

        IReadOnlyList<string> entitySignal = seedIds;
        IReadOnlyList<string> tagSignal = [];
        var isTagOnlySignal = false;

        if (requiresSeedSignal && seedIds.Count == 0)
        {
            addLimitation($"Skipped {op.Label}: no resolved seeds.");
            reportProgress();
            return;
        }

        if (allowsTagFallback && seedIds.Count == 0)
        {
            if (tagIds.Count == 0)
            {
                addLimitation($"Skipped {op.Label}: no resolved seeds or tags.");
                reportProgress();
                return;
            }
            entitySignal = [];
            tagSignal = tagIds;
            isTagOnlySignal = true;
        }

        if (!budget.TryReserve())
        {
            markBudgetExhausted();
            reportProgress();
            return;
        }

        var request = new InsightsRequest
        {
            FilterType = op.TargetEntityType!.Value,
            SeedEntityIds = entitySignal,
            SeedTagIds = tagSignal,
            TagTypes = op.Kind == ResearchOperationKind.CulturalThemes ? CulturalThemeTagTypes : [],
            LocationQuery = op.Kind == ResearchOperationKind.LocalPlaces ? brief.Snapshot.PrimaryLocation : null,
            Take = 10,
            Explainability = true,
        };

        var result = await qlooClient.GetInsightsAsync(request, ct).ConfigureAwait(false);
        operationsExecuted.Add(op.Label);
        reportProgress();

        if (result is QlooResult<InsightsResponse>.Failure failure)
        {
            if (failure.Kind == QlooErrorKind.AuthFailed)
                throw new QlooAuthFailedException($"Qloo insights call for {op.Label} failed authentication.");
            addLimitation($"{op.Label} failed: {failure.SafeMessage}");
            return;
        }

        var response = ((QlooResult<InsightsResponse>.Success)result).Value;

        if (op.Kind == ResearchOperationKind.LocalPlaces)
        {
            if (!LocationGuard.IsVerified(response.Localities, brief.Interpretation.Country))
            {
                addLimitation(LocationGuard.UnverifiedLimitation(brief.Snapshot.PrimaryLocation ?? string.Empty));
                return;
            }
        }

        var isUserSignal = hasUserSeed && !isTagOnlySignal;
        var isAiSignal = hasAiSeed && !isTagOnlySignal;
        foreach (var entity in response.Entities)
        {
            rawHits.Add(new RawEvidenceHit(entity, order, isTagOnlySignal, isUserSignal, isAiSignal));
        }
    }

    private List<EvidenceItem> BuildEvidenceItems(
        IReadOnlyList<RawEvidenceHit> rawHits, IReadOnlyList<string> seedNames, IReadOnlyList<string> tagKeywords,
        int maxEvidenceItems, double strongAffinityThreshold)
    {
        var now = _clock.GetUtcNow();

        var merged = rawHits
            .GroupBy(h => h.Entity.Id)
            .Select(group =>
            {
                var first = group.First().Entity;
                var affinities = group.Select(h => h.Entity.Affinity).Where(a => a.HasValue).Select(a => a!.Value).ToList();
                var popularities = group.Select(h => h.Entity.Popularity).Where(p => p.HasValue).Select(p => p!.Value).ToList();
                var isTagOnly = group.All(h => h.IsTagOnlySignal);
                var isUserSeed = group.Any(h => h.IsUserProvidedSeedSignal);
                var isAiSeed = group.Any(h => h.IsAiSuggestedSeedSignal);
                var appearedInOperations = group.Select(h => h.OpOrder).Distinct().Count();
                var minOrder = group.Min(h => h.OpOrder);
                double? affinity = affinities.Count > 0 ? affinities.Max() : null;

                var qualityInput = new EvidenceQualityInput(
                    IsUserProvidedSeedSignal: isUserSeed,
                    IsAiSuggestedSeedSignal: isAiSeed,
                    IsTagOnlySignal: isTagOnly,
                    Affinity: affinity,
                    AppearedInOperations: appearedInOperations,
                    LocationVerified: true,
                    SeedMatchedBelowThreshold: false);
                var classification = EvidenceQualityRules.Classify(qualityInput, strongAffinityThreshold);

                return new MergedEvidence(
                    first, affinity, popularities.Count > 0 ? popularities.Max() : null,
                    isTagOnly ? tagKeywords : seedNames, appearedInOperations, minOrder,
                    classification.Quality, classification.Reasons);
            })
            .OrderBy(m => m.MinOrder)
            .ThenByDescending(m => m.Affinity ?? -1)
            .ThenBy(m => m.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxEvidenceItems)
            .ToList();

        var items = new List<EvidenceItem>(merged.Count);
        for (var i = 0; i < merged.Count; i++)
        {
            var m = merged[i];
            items.Add(EvidenceItem.Create(
                key: $"ev_{i + 1:000}",
                qlooEntityId: m.Entity.Id,
                name: m.Entity.Name,
                entityType: m.Entity.Type,
                subtype: m.Entity.Subtype,
                sourceOperation: SourceOperation.Insights,
                sourceSignals: m.SourceSignals,
                affinity: m.Affinity,
                popularity: m.Popularity,
                tags: m.Entity.Tags.Take(10).ToList(),
                metadata: BuildMetadata(m.Entity.Properties),
                quality: m.Quality,
                qualityReasons: m.Reasons,
                appearedInOperations: m.AppearedInOperations,
                retrievedAt: now));
        }
        return items;
    }

    private static EvidenceMetadata BuildMetadata(IReadOnlyDictionary<string, string?> properties)
    {
        string? Get(params string[] keys) =>
            keys.Select(k => properties.TryGetValue(k, out var v) ? v : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var releaseYear = int.TryParse(Get("release_year", "releaseYear"), out var year) ? year : (int?)null;

        return new EvidenceMetadata(
            ShortDescription: Get("short_description", "description"),
            ImageUrl: Get("image_url", "imageUrl", "image"),
            ExternalUrl: Get("external_url", "externalUrl", "url"),
            ReleaseYear: releaseYear,
            Location: Get("city", "location"));
    }

    private sealed record RawEvidenceHit(QlooEntity Entity, int OpOrder, bool IsTagOnlySignal, bool IsUserProvidedSeedSignal, bool IsAiSuggestedSeedSignal);

    private sealed record MergedEvidence(
        QlooEntity Entity, double? Affinity, double? Popularity, IReadOnlyList<string> SourceSignals,
        int AppearedInOperations, int MinOrder, EvidenceQuality Quality, IReadOnlyList<string> Reasons);
}
