using KultureCompass.Application.Research;
using Shouldly;

namespace KultureCompass.Application.Tests.Research;

public class NameSimilarityTests
{
    [Theory]
    [InlineData("Burna Boy", "Burna Boy", 1.0)]
    [InlineData("Burna Boy", "burna boy", 1.0)]
    [InlineData("  Burna Boy  ", "Burna Boy", 1.0)]
    [InlineData("", "", 1.0)]
    public void JaroWinkler_returns_one_for_equal_normalized_names(string a, string b, double expected)
    {
        NameSimilarity.JaroWinkler(a, b).ShouldBe(expected, tolerance: 0.0001);
    }

    [Fact]
    public void JaroWinkler_returns_zero_when_one_side_is_empty()
    {
        NameSimilarity.JaroWinkler("Burna Boy", "").ShouldBe(0.0);
        NameSimilarity.JaroWinkler(null, "Burna Boy").ShouldBe(0.0);
    }

    [Fact]
    public void JaroWinkler_scores_a_near_match_above_the_seed_matching_threshold()
    {
        NameSimilarity.JaroWinkler("Burna Boy", "Burna Boyz").ShouldBeGreaterThanOrEqualTo(SeedMatcher.SimilarityThreshold);
    }

    [Fact]
    public void JaroWinkler_scores_unrelated_names_below_the_seed_matching_threshold()
    {
        NameSimilarity.JaroWinkler("Burna Boy", "Nike").ShouldBeLessThan(SeedMatcher.SimilarityThreshold);
    }

    [Fact]
    public void JaroWinkler_is_symmetric()
    {
        NameSimilarity.JaroWinkler("Orange Culture", "Orang Culture")
            .ShouldBe(NameSimilarity.JaroWinkler("Orang Culture", "Orange Culture"), tolerance: 0.0001);
    }
}
