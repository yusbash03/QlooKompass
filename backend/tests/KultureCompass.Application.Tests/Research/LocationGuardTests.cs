using KultureCompass.Application.Qloo;
using KultureCompass.Application.Research;
using Shouldly;

namespace KultureCompass.Application.Tests.Research;

public class LocationGuardTests
{
    [Fact]
    public void IsVerified_true_when_echoed_country_matches_expected_country()
    {
        IReadOnlyList<QlooLocality> localities = [new QlooLocality("Lagos", "Nigeria")];

        LocationGuard.IsVerified(localities, "Nigeria").ShouldBeTrue();
    }

    [Fact]
    public void IsVerified_is_case_insensitive()
    {
        IReadOnlyList<QlooLocality> localities = [new QlooLocality("Lagos", "NIGERIA")];

        LocationGuard.IsVerified(localities, "nigeria").ShouldBeTrue();
    }

    [Fact]
    public void IsVerified_false_when_echoed_country_does_not_match()
    {
        IReadOnlyList<QlooLocality> localities = [new QlooLocality("Paris", "France")];

        LocationGuard.IsVerified(localities, "Nigeria").ShouldBeFalse();
    }

    [Fact]
    public void IsVerified_false_when_no_localities_were_echoed()
    {
        LocationGuard.IsVerified([], "Nigeria").ShouldBeFalse();
    }

    [Fact]
    public void IsVerified_false_when_expected_country_is_unknown()
    {
        IReadOnlyList<QlooLocality> localities = [new QlooLocality("Lagos", "Nigeria")];

        LocationGuard.IsVerified(localities, null).ShouldBeFalse();
    }

    [Fact]
    public void UnverifiedLimitation_names_the_location_that_could_not_be_resolved()
    {
        LocationGuard.UnverifiedLimitation("Lagos, Nigeria")
            .ShouldBe("Qloo could not resolve 'Lagos, Nigeria' reliably; local venue suggestions omitted.");
    }
}
