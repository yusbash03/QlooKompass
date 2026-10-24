using KultureCompass.Domain.Common;

namespace KultureCompass.Domain.Profiles;

public sealed class BusinessProfile
{
    private readonly List<ReferenceEntity> _referenceEntities = [];

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public string BrandName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public BusinessCategory Category { get; private set; }
    public string? CategoryOther { get; private set; }
    public string ProductsDescription { get; private set; } = string.Empty;
    public string? TargetMarket { get; private set; }
    public string? PrimaryLocation { get; private set; }
    public MarketingObjective MarketingObjective { get; private set; }
    public string? ObjectiveNotes { get; private set; }
    public string? BrandIdentity { get; private set; }
    public IReadOnlyList<ReferenceEntity> ReferenceEntities => _referenceEntities;
    public BudgetRange? BudgetRange { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    /// Reserved for EF Core materialization (field-based property access).
    private BusinessProfile()
    {
    }

    private BusinessProfile(Guid id, string ownerId, DateTimeOffset now)
    {
        Id = id;
        OwnerId = ownerId;
        BrandName = string.Empty;
        ProductsDescription = string.Empty;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static BusinessProfile Create(
        string ownerId,
        string brandName,
        string? description,
        BusinessCategory category,
        string? categoryOther,
        string productsDescription,
        string? targetMarket,
        string? primaryLocation,
        MarketingObjective marketingObjective,
        string? objectiveNotes,
        string? brandIdentity,
        IReadOnlyList<ReferenceEntity> referenceEntities,
        BudgetRange? budgetRange,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new DomainException("OwnerId is required.");

        var profile = new BusinessProfile(Guid.CreateVersion7(), ownerId, now);
        profile.SetCore(
            brandName, description, category, categoryOther, productsDescription, targetMarket,
            primaryLocation, marketingObjective, objectiveNotes, brandIdentity, referenceEntities, budgetRange, now);
        return profile;
    }

    public void Update(
        string brandName,
        string? description,
        BusinessCategory category,
        string? categoryOther,
        string productsDescription,
        string? targetMarket,
        string? primaryLocation,
        MarketingObjective marketingObjective,
        string? objectiveNotes,
        string? brandIdentity,
        IReadOnlyList<ReferenceEntity> referenceEntities,
        BudgetRange? budgetRange,
        DateTimeOffset now) =>
        SetCore(
            brandName, description, category, categoryOther, productsDescription, targetMarket,
            primaryLocation, marketingObjective, objectiveNotes, brandIdentity, referenceEntities, budgetRange, now);

    private void SetCore(
        string brandName,
        string? description,
        BusinessCategory category,
        string? categoryOther,
        string productsDescription,
        string? targetMarket,
        string? primaryLocation,
        MarketingObjective marketingObjective,
        string? objectiveNotes,
        string? brandIdentity,
        IReadOnlyList<ReferenceEntity> referenceEntities,
        BudgetRange? budgetRange,
        DateTimeOffset now)
    {
        BrandName = Guard.RequiredLength(brandName, nameof(BrandName), 2, 100);
        Description = Guard.OptionalMaxLength(description, nameof(Description), 1000);
        Category = category;

        if (category == BusinessCategory.Other && string.IsNullOrWhiteSpace(categoryOther))
            throw new DomainException("CategoryOther is required when Category is Other.");
        CategoryOther = Guard.OptionalMaxLength(categoryOther, nameof(CategoryOther), 60);

        ProductsDescription = Guard.RequiredLength(productsDescription, nameof(ProductsDescription), 10, 1000);
        TargetMarket = Guard.OptionalMaxLength(targetMarket, nameof(TargetMarket), 200);
        PrimaryLocation = Guard.OptionalMaxLength(primaryLocation, nameof(PrimaryLocation), 100);
        MarketingObjective = marketingObjective;
        ObjectiveNotes = Guard.OptionalMaxLength(objectiveNotes, nameof(ObjectiveNotes), 300);
        BrandIdentity = Guard.OptionalMaxLength(brandIdentity, nameof(BrandIdentity), 500);

        Guard.MaxItems(referenceEntities, nameof(ReferenceEntities), 8);
        _referenceEntities.Clear();
        _referenceEntities.AddRange(referenceEntities);

        BudgetRange = budgetRange;
        UpdatedAt = now;
    }
}
