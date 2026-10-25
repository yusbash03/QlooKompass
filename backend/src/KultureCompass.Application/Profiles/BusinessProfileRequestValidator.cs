using FluentValidation;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Profiles;

/// <summary>Mirrors contracts/openapi.yaml BusinessProfileRequest limits exactly (and therefore the
/// BusinessProfile domain guards, so a valid request never trips a DomainException).</summary>
public sealed class BusinessProfileRequestValidator : AbstractValidator<BusinessProfileRequest>
{
    public BusinessProfileRequestValidator()
    {
        RuleFor(r => r.BrandName).NotEmpty().Length(2, 100);
        RuleFor(r => r.Description).MaximumLength(1000);
        RuleFor(r => r.Category).NotNull().IsInEnum();
        RuleFor(r => r.CategoryOther).MaximumLength(60);
        RuleFor(r => r.CategoryOther).NotEmpty()
            .When(r => r.Category == BusinessCategory.Other)
            .WithMessage("'Category Other' is required when category is Other.");
        RuleFor(r => r.ProductsDescription).NotEmpty().Length(10, 1000);
        RuleFor(r => r.TargetMarket).MaximumLength(200);
        RuleFor(r => r.PrimaryLocation).MaximumLength(100);
        RuleFor(r => r.MarketingObjective).NotNull().IsInEnum();
        RuleFor(r => r.ObjectiveNotes).MaximumLength(300);
        RuleFor(r => r.BrandIdentity).MaximumLength(500);
        RuleFor(r => r.BudgetRange).IsInEnum();
        RuleFor(r => r.ReferenceEntities).Must(list => list is null || list.Count <= 8)
            .WithMessage("At most 8 reference entities are allowed.");
        RuleForEach(r => r.ReferenceEntities).ChildRules(entity =>
        {
            entity.RuleFor(e => e.Name).NotEmpty().Length(1, 100);
            entity.RuleFor(e => e.KindHint).NotNull().IsInEnum();
        });
    }
}

public sealed class PageQueryValidator : AbstractValidator<PageQuery>
{
    public PageQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed record PageQuery(int Page = 1, int PageSize = 20);
