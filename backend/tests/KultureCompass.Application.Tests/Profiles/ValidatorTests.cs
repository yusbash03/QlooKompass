using KultureCompass.Application.Auth;
using KultureCompass.Application.Profiles;
using KultureCompass.Domain.Profiles;
using Shouldly;

namespace KultureCompass.Application.Tests.Profiles;

public class ValidatorTests
{
    private static BusinessProfileRequest Valid() => new(
        "Synthetic Brand", null, BusinessCategory.Streetwear, null, "Synthetic graphic tees", null, "Lagos, Nigeria",
        MarketingObjective.Sales, null, null, [new ReferenceEntityInput("Synthetic Star", ReferenceKindHint.Artist)], null);

    [Fact]
    public void A_valid_profile_request_passes() =>
        new BusinessProfileRequestValidator().Validate(Valid()).IsValid.ShouldBeTrue();

    public static TheoryData<string, BusinessProfileRequest> Invalid => new()
    {
        { "BrandName", Valid() with { BrandName = "X" } },
        { "BrandName", Valid() with { BrandName = new string('a', 101) } },
        { "Description", Valid() with { Description = new string('a', 1001) } },
        { "Category", Valid() with { Category = null } },
        { "CategoryOther", Valid() with { Category = BusinessCategory.Other, CategoryOther = null } },
        { "CategoryOther", Valid() with { CategoryOther = new string('a', 61) } },
        { "ProductsDescription", Valid() with { ProductsDescription = "too short" } },
        { "TargetMarket", Valid() with { TargetMarket = new string('a', 201) } },
        { "PrimaryLocation", Valid() with { PrimaryLocation = new string('a', 101) } },
        { "MarketingObjective", Valid() with { MarketingObjective = null } },
        { "ObjectiveNotes", Valid() with { ObjectiveNotes = new string('a', 301) } },
        { "BrandIdentity", Valid() with { BrandIdentity = new string('a', 501) } },
        { "ReferenceEntities", Valid() with { ReferenceEntities = [.. Enumerable.Repeat(new ReferenceEntityInput("Synthetic", ReferenceKindHint.Brand), 9)] } },
        { "ReferenceEntities[0].Name", Valid() with { ReferenceEntities = [new ReferenceEntityInput("", ReferenceKindHint.Brand)] } },
        { "ReferenceEntities[0].KindHint", Valid() with { ReferenceEntities = [new ReferenceEntityInput("Synthetic", null)] } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Openapi_limits_are_enforced(string expectedProperty, BusinessProfileRequest request)
    {
        var result = new BusinessProfileRequestValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.Select(e => e.PropertyName).ShouldContain(expectedProperty);
    }

    [Theory]
    [InlineData("Abcdefghij", false)]      // 2 classes
    [InlineData("abcdefgh12", false)]      // 2 classes
    [InlineData("Abcdefgh12", true)]       // upper + lower + digit
    [InlineData("abcdefgh1!", true)]       // lower + digit + symbol
    [InlineData("Abc1!", false)]           // 4 classes but too short
    public void Password_policy_is_min_10_and_3_of_4_classes(string password, bool valid) =>
        new RegisterRequestValidator().Validate(new RegisterRequest("synthetic@example.test", password, null))
            .IsValid.ShouldBe(valid);
}
