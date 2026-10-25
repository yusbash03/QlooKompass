using System.Text.Json;
using System.Text.Json.Serialization;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Plans;

namespace KultureCompass.Application.Generation;

/// <summary>JSON-encodes user/Qloo data for prompt data blocks (BE-04 §5: data is JSON-encoded, never spliced as
/// prose). Contains no owner ids, emails or other PII.</summary>
internal static class PromptJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Brief(BriefSnapshot s) => JsonSerializer.Serialize(new
    {
        s.BrandName,
        s.Description,
        s.Category,
        s.CategoryOther,
        s.ProductsDescription,
        s.TargetMarket,
        s.PrimaryLocation,
        s.MarketingObjective,
        s.ObjectiveNotes,
        s.BrandIdentity,
        ReferenceEntities = s.ReferenceEntities.Select(r => new { r.Name, r.KindHint }),
        s.BudgetRange,
    }, Options);

    public static string BriefWithInterpretation(BriefSnapshot s, BriefInterpretation i) => JsonSerializer.Serialize(new
    {
        s.BrandName,
        s.Description,
        s.Category,
        s.CategoryOther,
        s.ProductsDescription,
        s.TargetMarket,
        s.PrimaryLocation,
        s.MarketingObjective,
        s.ObjectiveNotes,
        s.BrandIdentity,
        s.BudgetRange,
        i.ProductThemes,
        i.AestheticKeywords,
        i.TargetCustomerSummary,
        i.Constraints,
    }, Options);

    /// <summary>Compact evidence list (BE-04 §1 step 5): key, name, type, affinity, popularity, ≤5 tags, quality, signal.</summary>
    public static string Evidence(IReadOnlyList<EvidenceItem> evidence) => JsonSerializer.Serialize(
        evidence.Select(e => new
        {
            e.Key,
            e.Name,
            Type = OutputValidator.ShortType(e.EntityType),
            e.Subtype,
            Affinity = e.Affinity is { } a ? Math.Round(a, 2) : (double?)null,
            Popularity = e.Popularity is { } p ? Math.Round(p, 2) : (double?)null,
            Tags = e.Tags.Take(5),
            Quality = e.Quality,
            SourceSignal = e.SourceSignals.Count > 0 ? string.Join(", ", e.SourceSignals.Take(3)) : null,
        }),
        Options);

    public static string Strings(IEnumerable<string> values) => JsonSerializer.Serialize(values, Options);
}
