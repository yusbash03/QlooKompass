using System.Text.Json;
using System.Text.Json.Serialization;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KultureCompass.Api.DevTools;

/// <summary>Dev-only CLI for BE-4: <c>dotnet run -- research --profile-json &lt;file&gt;</c>. Runs
/// <see cref="CulturalIntelligenceService"/> against a hand-written profile JSON (no auth, no database, no LLM —
/// the brief interpretation is <see cref="BriefInterpretation.DeriveDeterministically"/>'s no-LLM fallback) and
/// prints the resulting evidence table. Works in both Qloo:Mode=Mock and Qloo:Mode=Live since it only talks to the
/// already-registered <see cref="IQlooClient"/>.</summary>
internal static class ResearchConsoleCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task RunAsync(IServiceProvider services, string profileJsonPath, CancellationToken ct)
    {
        if (!File.Exists(profileJsonPath))
        {
            Console.Error.WriteLine($"Profile JSON file not found: {profileJsonPath}");
            return;
        }

        var json = await File.ReadAllTextAsync(profileJsonPath, ct).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<ResearchProfileDto>(json, JsonOptions)
            ?? throw new InvalidOperationException($"'{profileJsonPath}' deserialized to null.");

        var snapshot = dto.ToBriefSnapshot();
        var interpretation = BriefInterpretation.DeriveDeterministically(snapshot);
        var brief = new ResearchBrief(snapshot, interpretation);

        var qlooOptions = services.GetRequiredService<IOptions<QlooOptions>>().Value;
        var generationSettings = services.GetRequiredService<IOptions<GenerationSettings>>().Value;
        var qlooClient = services.GetRequiredService<IQlooClient>();

        var planner = new ResearchPlanner(qlooOptions.LocationInsightsVerified, qlooOptions.PersonInsightsVerified, qlooOptions.DemographicsVerified);
        var service = new CulturalIntelligenceService(qlooClient, planner, generationSettings.MaxEvidenceItems);
        var budget = new ResearchBudget(qlooOptions.MaxCallsPerPlan);

        Console.WriteLine($"Qloo mode: {qlooOptions.Mode}");
        Console.WriteLine($"Brand: {snapshot.BrandName} | Category: {snapshot.Category} | Objective: {snapshot.MarketingObjective} | Location: {snapshot.PrimaryLocation}");
        Console.WriteLine($"User references: {string.Join(", ", snapshot.ReferenceEntities.Select(r => $"{r.Name} ({r.KindHint})"))}");
        Console.WriteLine();

        var progress = new Progress<string>(message => Console.WriteLine($"  {message}"));

        EvidenceSet evidenceSet;
        try
        {
            evidenceSet = await service.RetrieveAsync(brief, budget, progress, ct).ConfigureAwait(false);
        }
        catch (QlooAuthFailedException ex)
        {
            Console.Error.WriteLine($"Aborted: {ex.Message}");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Calls made: {evidenceSet.CallsMade}/{qlooOptions.MaxCallsPerPlan} | Operations executed: {evidenceSet.OperationsExecuted.Count}");
        Console.WriteLine();

        if (evidenceSet.Items.Count == 0)
        {
            Console.WriteLine("No evidence retrieved.");
        }
        else
        {
            Console.WriteLine($"{"Key",-8} {"Quality",-9} {"Type",-10} {"Affinity",-9} Name");
            foreach (var item in evidenceSet.Items)
            {
                Console.WriteLine($"{item.Key,-8} {item.Quality,-9} {item.EntityType,-10} {item.Affinity?.ToString("0.00") ?? "-",-9} {item.Name}");
            }
        }

        var evaluation = EvidenceEvaluator.Evaluate(evidenceSet, generationSettings.MinEvidenceItems);
        Console.WriteLine();
        Console.WriteLine(
            $"Evidence counts: Strong={evaluation.CountsByQuality[EvidenceQuality.Strong]}, " +
            $"Moderate={evaluation.CountsByQuality[EvidenceQuality.Moderate]}, Weak={evaluation.CountsByQuality[EvidenceQuality.Weak]}");
        Console.WriteLine($"Sufficient evidence (>= {generationSettings.MinEvidenceItems}): {evaluation.HasSufficientEvidence}");
        if (evaluation.InsufficientEvidenceMessage is not null)
            Console.WriteLine(evaluation.InsufficientEvidenceMessage);

        if (evidenceSet.Limitations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Limitations:");
            foreach (var limitation in evidenceSet.Limitations)
                Console.WriteLine($"  - {limitation}");
        }
    }

    private sealed class ResearchProfileDto
    {
        public string BrandName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public BusinessCategory Category { get; set; }
        public string? CategoryOther { get; set; }
        public string ProductsDescription { get; set; } = string.Empty;
        public string? TargetMarket { get; set; }
        public string? PrimaryLocation { get; set; }
        public MarketingObjective MarketingObjective { get; set; }
        public string? ObjectiveNotes { get; set; }
        public string? BrandIdentity { get; set; }
        public List<ReferenceEntityDto> ReferenceEntities { get; set; } = [];

        public BriefSnapshot ToBriefSnapshot() => new(
            BrandName,
            Description,
            Category,
            CategoryOther,
            ProductsDescription,
            TargetMarket,
            PrimaryLocation,
            MarketingObjective,
            ObjectiveNotes,
            BrandIdentity,
            ReferenceEntities.Select(r => ReferenceEntity.Create(r.Name, r.KindHint)).ToList(),
            BudgetRange: null);
    }

    private sealed class ReferenceEntityDto
    {
        public string Name { get; set; } = string.Empty;
        public ReferenceKindHint KindHint { get; set; }
    }
}
