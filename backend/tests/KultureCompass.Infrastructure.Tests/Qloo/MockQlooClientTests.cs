using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Qloo;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Qloo;

public class MockQlooClientTests
{
    private readonly MockQlooClient _sut = new();

    [Fact]
    public async Task SearchEntitiesAsync_is_deterministic_and_every_fixture_name_is_prefixed_Synthetic()
    {
        var first = await _sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "Burna Boy", Types = [QlooEntityType.Artist] }, CancellationToken.None);
        var second = await _sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "Burna Boy", Types = [QlooEntityType.Artist] }, CancellationToken.None);

        first.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Success>();
        var firstValue = ((QlooResult<IReadOnlyList<QlooEntity>>.Success)first).Value;
        var secondValue = ((QlooResult<IReadOnlyList<QlooEntity>>.Success)second).Value;

        // Compare by id sequence rather than full record equality: Tags/Properties are List/Dictionary-backed
        // and two independently-deserialized instances are reference-unequal even with identical content.
        firstValue.Select(e => e.Id).ShouldBe(secondValue.Select(e => e.Id));
        firstValue.ShouldNotBeEmpty();
        // Top hit echoes the user's own query (see Search_echoes_the_users_query…); everything else is a fixture.
        firstValue.Skip(1).ShouldAllBe(e => e.Name.StartsWith("Synthetic ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchEntitiesAsync_selects_the_brand_fixture_for_brand_only_requests()
    {
        var result = await _sut.SearchEntitiesAsync(
            new EntitySearchRequest { Query = "Nike", Types = [QlooEntityType.Brand] }, CancellationToken.None);

        var value = ((QlooResult<IReadOnlyList<QlooEntity>>.Success)result).Value;
        value.ShouldAllBe(e => e.Type == "urn:entity:brand");
    }

    [Fact]
    public async Task SearchTagsAsync_returns_synthetic_tags()
    {
        var result = await _sut.SearchTagsAsync("streetwear", 10, CancellationToken.None);

        var value = ((QlooResult<IReadOnlyList<QlooTag>>.Success)result).Value;
        value.ShouldNotBeEmpty();
        value.ShouldAllBe(t => t.Name.Length > 0);
    }

    [Fact]
    public async Task GetInsightsAsync_returns_entities_with_affinity_and_echoed_locality()
    {
        var request = new InsightsRequest { FilterType = QlooEntityType.Brand, SeedEntityIds = ["synthetic-artist-001"] };

        var result = await _sut.GetInsightsAsync(request, CancellationToken.None);

        var value = ((QlooResult<InsightsResponse>.Success)result).Value;
        value.Entities.ShouldNotBeEmpty();
        value.Entities.ShouldAllBe(e => e.Affinity.HasValue);
        value.Localities.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Search_echoes_the_users_query_as_the_top_hit_so_any_brief_resolves_in_mock_mode()
    {
        var sut = new MockQlooClient();

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "Any Artist Name", Types = [QlooEntityType.Artist], Take = 3 }, CancellationToken.None);

        var hits = result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Success>().Value;
        hits[0].Name.ShouldBe("Any Artist Name");
        hits[0].Id.ShouldStartWith("synthetic-mock-seed-");
        hits[0].Type.ShouldBe("urn:entity:artist");
        hits.Skip(1).ShouldAllBe(h => h.Name.StartsWith("Synthetic "));
    }
}
