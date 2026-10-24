using KultureCompass.Domain.Profiles;

namespace KultureCompass.Domain.Plans;

public sealed record BriefSnapshot(
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
    IReadOnlyList<ReferenceEntity> ReferenceEntities,
    BudgetRange? BudgetRange)
{
    public static BriefSnapshot FromProfile(BusinessProfile profile) => new(
        profile.BrandName,
        profile.Description,
        profile.Category,
        profile.CategoryOther,
        profile.ProductsDescription,
        profile.TargetMarket,
        profile.PrimaryLocation,
        profile.MarketingObjective,
        profile.ObjectiveNotes,
        profile.BrandIdentity,
        profile.ReferenceEntities,
        profile.BudgetRange);
}
