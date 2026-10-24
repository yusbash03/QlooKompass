using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Qloo;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Qloo;

public class QlooQueryBuilderTests
{
    [Fact]
    public void TryBuildSearchQuery_joins_types_in_urn_form_and_sets_take()
    {
        // BE-0 live probe confirmed types=artist (short form) returns 400; types=urn:entity:artist returns 200.
        var request = new EntitySearchRequest { Query = "Burna Boy", Types = [QlooEntityType.Artist], Take = 3 };

        QlooQueryBuilder.TryBuildSearchQuery(request, out var path, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        path.ShouldBe("/search?query=Burna%20Boy&types=urn:entity:artist&take=3");
    }

    [Fact]
    public void TryBuildSearchQuery_joins_multiple_types_in_urn_form()
    {
        var request = new EntitySearchRequest { Query = "Lagos", Types = [QlooEntityType.Place, QlooEntityType.Destination], Take = 5 };

        QlooQueryBuilder.TryBuildSearchQuery(request, out var path, out _);

        path.ShouldBe("/search?query=Lagos&types=urn:entity:place,urn:entity:destination&take=5");
    }

    [Fact]
    public void TryBuildSearchQuery_without_types_omits_the_types_param()
    {
        var request = new EntitySearchRequest { Query = "Wizkid", Take = 5 };

        QlooQueryBuilder.TryBuildSearchQuery(request, out var path, out _).ShouldBeTrue();

        path.ShouldBe("/search?query=Wizkid&take=5");
    }

    [Fact]
    public void TryBuildTagsQuery_builds_the_verified_form()
    {
        QlooQueryBuilder.TryBuildTagsQuery("streetwear", 10, out var path, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        path.ShouldBe("/v2/tags?filter.query=streetwear&take=10");
    }

    [Fact]
    public void TryBuildInsightsQuery_with_seed_entities_joins_ids_and_sets_filter_type()
    {
        var request = new InsightsRequest
        {
            FilterType = QlooEntityType.Brand,
            SeedEntityIds = ["id-1", "id-2"],
            Take = 10,
            Explainability = true,
        };

        QlooQueryBuilder.TryBuildInsightsQuery(request, out var path, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        path.ShouldBe("/v2/insights?filter.type=urn:entity:brand&signal.interests.entities=id-1,id-2&take=10&feature.explainability=true");
    }

    [Fact]
    public void TryBuildInsightsQuery_with_tag_signal_only_still_succeeds()
    {
        var request = new InsightsRequest { FilterType = QlooEntityType.Brand, SeedTagIds = ["tag-1"] };

        QlooQueryBuilder.TryBuildInsightsQuery(request, out var path, out var error).ShouldBeTrue();

        path.ShouldContain("signal.interests.tags=tag-1");
        error.ShouldBeNull();
    }

    [Fact]
    public void TryBuildInsightsQuery_without_any_signal_fails_like_the_documented_400()
    {
        var request = new InsightsRequest { FilterType = QlooEntityType.Brand };

        var ok = QlooQueryBuilder.TryBuildInsightsQuery(request, out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void TryBuildInsightsQuery_urn_tag_and_demographics_use_their_own_urn_forms()
    {
        var tagRequest = new InsightsRequest { FilterType = QlooEntityType.Tag, SeedEntityIds = ["id-1"] };
        var demoRequest = new InsightsRequest { FilterType = QlooEntityType.Demographics, SeedEntityIds = ["id-1"] };

        QlooQueryBuilder.TryBuildInsightsQuery(tagRequest, out var tagPath, out _);
        QlooQueryBuilder.TryBuildInsightsQuery(demoRequest, out var demoPath, out _);

        tagPath.ShouldContain("filter.type=urn:tag");
        demoPath.ShouldContain("filter.type=urn:demographics");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(100, 25)]
    public void TryBuildInsightsQuery_clamps_take_into_1_to_25(int requested, int expected)
    {
        var request = new InsightsRequest { FilterType = QlooEntityType.Brand, SeedEntityIds = ["id-1"], Take = requested };

        QlooQueryBuilder.TryBuildInsightsQuery(request, out var path, out _);

        path.ShouldContain($"take={expected}");
    }
}
