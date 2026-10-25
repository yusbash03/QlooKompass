using System.Globalization;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Profiles;

public sealed record ReferenceEntityInput(string? Name, ReferenceKindHint? KindHint);

/// <summary>contracts/openapi.yaml BusinessProfileRequest. Required fields are nullable here so absence is reported
/// by <see cref="BusinessProfileRequestValidator"/> as a 400 with <c>errors</c>, not as a binding failure.</summary>
public sealed record BusinessProfileRequest(
    string? BrandName,
    string? Description,
    BusinessCategory? Category,
    string? CategoryOther,
    string? ProductsDescription,
    string? TargetMarket,
    string? PrimaryLocation,
    MarketingObjective? MarketingObjective,
    string? ObjectiveNotes,
    string? BrandIdentity,
    IReadOnlyList<ReferenceEntityInput>? ReferenceEntities,
    BudgetRange? BudgetRange);

public sealed record ReferenceEntityDto(string Name, ReferenceKindHint KindHint);

public sealed record BusinessProfileDto(
    Guid Id,
    string BrandName,
    string? Description,
    BusinessCategory Category,
    string? CategoryOther,
    string ProductsDescription,
    string? TargetMarket,
    string? PrimaryLocation,
    MarketingObjective MarketingObjective,
    string? ObjectiveNotes,
    string? BrandIdentity,
    IReadOnlyList<ReferenceEntityDto> ReferenceEntities,
    BudgetRange? BudgetRange,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version)
{
    public static BusinessProfileDto From(BusinessProfile p) => new(
        p.Id, p.BrandName, p.Description, p.Category, p.CategoryOther, p.ProductsDescription, p.TargetMarket,
        p.PrimaryLocation, p.MarketingObjective, p.ObjectiveNotes, p.BrandIdentity,
        [.. p.ReferenceEntities.Select(r => new ReferenceEntityDto(r.Name, r.KindHint))],
        p.BudgetRange, p.CreatedAt, p.UpdatedAt, p.Version.ToString(CultureInfo.InvariantCulture));
}

public sealed record BusinessProfileSummary(
    Guid Id,
    string BrandName,
    BusinessCategory Category,
    string? PrimaryLocation,
    DateTimeOffset UpdatedAt,
    int PlanCount,
    GenerationStatus? LatestPlanStatus);
