using KultureCompass.Domain.Common;
using KultureCompass.Domain.Profiles;
using Shouldly;

namespace KultureCompass.Domain.Tests.Profiles;

public class BusinessProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BusinessProfile CreateValid(
        BusinessCategory category = BusinessCategory.Streetwear,
        string? categoryOther = null,
        IReadOnlyList<ReferenceEntity>? referenceEntities = null) =>
        BusinessProfile.Create(
            ownerId: "owner-1",
            brandName: "Eko Thread Co.",
            description: "Lagos streetwear label",
            category: category,
            categoryOther: categoryOther,
            productsDescription: "Afrobeats-inspired graphic tees",
            targetMarket: "Lagos 18-30",
            primaryLocation: "Lagos, Nigeria",
            marketingObjective: MarketingObjective.Sales,
            objectiveNotes: null,
            brandIdentity: null,
            referenceEntities: referenceEntities ?? [],
            budgetRange: BudgetRange.From100kTo500k,
            now: Now);

    [Fact]
    public void Create_succeeds_with_valid_fields()
    {
        var profile = CreateValid();
        profile.BrandName.ShouldBe("Eko Thread Co.");
        profile.Version.ShouldBe(0u);
        profile.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Create_throws_when_brand_name_too_short()
    {
        Should.Throw<DomainException>(() => BusinessProfile.Create(
            "owner-1", "A", null, BusinessCategory.Fashion, null, "Valid products description here",
            null, null, MarketingObjective.Awareness, null, null, [], null, Now));
    }

    [Fact]
    public void Create_throws_when_category_other_missing_for_other_category()
    {
        Should.Throw<DomainException>(() => CreateValid(category: BusinessCategory.Other, categoryOther: null));
    }

    [Fact]
    public void Create_succeeds_when_category_other_provided_for_other_category()
    {
        var profile = CreateValid(category: BusinessCategory.Other, categoryOther: "Home decor");
        profile.CategoryOther.ShouldBe("Home decor");
    }

    [Fact]
    public void Create_throws_when_more_than_eight_reference_entities()
    {
        var refs = Enumerable.Range(0, 9).Select(i => ReferenceEntity.Create($"Ref {i}", ReferenceKindHint.Brand)).ToList();
        Should.Throw<DomainException>(() => CreateValid(referenceEntities: refs));
    }

    [Fact]
    public void Update_changes_fields_and_bumps_updated_at()
    {
        var profile = CreateValid();
        var later = Now.AddDays(1);
        profile.Update(
            "New Name", null, BusinessCategory.Fashion, null, "Updated products description here",
            null, null, MarketingObjective.Engagement, null, null, [], null, later);

        profile.BrandName.ShouldBe("New Name");
        profile.Category.ShouldBe(BusinessCategory.Fashion);
        profile.UpdatedAt.ShouldBe(later);
    }
}
