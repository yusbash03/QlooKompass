using KultureCompass.Application.Qloo;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Profiles;
using Shouldly;

namespace KultureCompass.Application.Tests.Research;

public class SeedMatcherTests
{
    [Fact]
    public void Match_returns_resolved_seed_for_a_confident_same_type_hit()
    {
        var candidate = new SeedCandidate("Burna Boy", ReferenceKindHint.Artist, SeedOrigin.UserProvided);
        var hits = new[] { FakeQlooClient.Entity("artist-1", "Burna Boy", "artist") };

        var result = SeedMatcher.Match(candidate, hits);

        result.Limitation.ShouldBeNull();
        result.Resolved.ShouldNotBeNull();
        result.Resolved!.Entity.Id.ShouldBe("artist-1");
    }

    [Fact]
    public void Match_rejects_a_hit_of_the_wrong_type_even_if_the_name_matches_exactly()
    {
        var candidate = new SeedCandidate("Nike", ReferenceKindHint.Brand, SeedOrigin.UserProvided);
        var hits = new[] { FakeQlooClient.Entity("artist-99", "Nike", "artist") };

        var result = SeedMatcher.Match(candidate, hits);

        result.Resolved.ShouldBeNull();
        result.Limitation.ShouldBe("Could not confidently match 'Nike' in Qloo.");
    }

    [Fact]
    public void Match_drops_a_seed_below_the_similarity_threshold()
    {
        var candidate = new SeedCandidate("Burna Boy", ReferenceKindHint.Artist, SeedOrigin.UserProvided);
        var hits = new[] { FakeQlooClient.Entity("artist-2", "Completely Different Name", "artist") };

        var result = SeedMatcher.Match(candidate, hits);

        result.Resolved.ShouldBeNull();
        result.Limitation.ShouldBe("Could not confidently match 'Burna Boy' in Qloo.");
    }

    [Fact]
    public void Match_returns_no_limitation_message_when_no_hits_are_returned_at_all()
    {
        var candidate = new SeedCandidate("Burna Boy", ReferenceKindHint.Artist, SeedOrigin.UserProvided);

        var result = SeedMatcher.Match(candidate, []);

        result.Resolved.ShouldBeNull();
        result.Limitation.ShouldNotBeNull();
    }

    [Fact]
    public void Match_picks_the_highest_scoring_hit_among_several_same_type_candidates()
    {
        var candidate = new SeedCandidate("Burna Boy", ReferenceKindHint.Artist, SeedOrigin.UserProvided);
        var hits = new[]
        {
            FakeQlooClient.Entity("artist-close", "Burna Boyz", "artist"),
            FakeQlooClient.Entity("artist-exact", "Burna Boy", "artist"),
        };

        var result = SeedMatcher.Match(candidate, hits);

        result.Resolved!.Entity.Id.ShouldBe("artist-exact");
    }

    [Fact]
    public void Match_is_ambiguous_and_skips_when_hint_is_unknown_and_two_different_types_both_score_high()
    {
        var candidate = new SeedCandidate("Nike", ReferenceKindHint.Unknown, SeedOrigin.UserProvided);
        var hits = new[]
        {
            FakeQlooClient.Entity("brand-nike", "Nike", "brand"),
            FakeQlooClient.Entity("artist-nike", "Nike", "artist"),
        };

        var result = SeedMatcher.Match(candidate, hits);

        result.Resolved.ShouldBeNull();
        result.Limitation.ShouldNotBeNull();
    }

    [Fact]
    public void Match_with_unknown_hint_accepts_the_single_confident_hit_when_not_ambiguous()
    {
        var candidate = new SeedCandidate("Burna Boy", ReferenceKindHint.Unknown, SeedOrigin.AiSuggested);
        var hits = new[] { FakeQlooClient.Entity("artist-1", "Burna Boy", "artist") };

        var result = SeedMatcher.Match(candidate, hits);

        result.Resolved.ShouldNotBeNull();
        result.Resolved!.Candidate.Origin.ShouldBe(SeedOrigin.AiSuggested);
    }

    [Theory]
    [InlineData(ReferenceKindHint.Place)]
    public void Match_for_place_hint_accepts_either_place_or_destination_type(ReferenceKindHint hint)
    {
        var candidate = new SeedCandidate("Lagos", hint, SeedOrigin.UserProvided);
        var hits = new[] { FakeQlooClient.Entity("dest-lagos", "Lagos", "destination") };

        var result = SeedMatcher.Match(candidate, hits);

        result.Resolved.ShouldNotBeNull();
    }

    [Fact]
    public void Match_accepts_urn_form_entity_types_the_same_as_short_form()
    {
        // BE-03 §1 doesn't confirm whether Qloo returns "artist" or "urn:entity:artist" — the synthetic fixtures
        // use the URN form, so matching must tolerate either.
        var candidate = new SeedCandidate("Burna Boy", ReferenceKindHint.Artist, SeedOrigin.UserProvided);
        var hits = new[] { FakeQlooClient.Entity("artist-1", "Burna Boy", "urn:entity:artist") };

        var result = SeedMatcher.Match(candidate, hits);

        result.Resolved.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("brand", "brand")]
    [InlineData("urn:entity:brand", "brand")]
    [InlineData("URN:ENTITY:ARTIST", "ARTIST")]
    [InlineData(null, "")]
    public void NormalizeType_strips_the_urn_entity_prefix_when_present(string? input, string expected)
    {
        SeedMatcher.NormalizeType(input).ShouldBe(expected);
    }
}
