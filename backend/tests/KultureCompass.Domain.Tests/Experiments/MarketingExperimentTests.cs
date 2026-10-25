using KultureCompass.Domain.Experiments;
using Shouldly;

namespace KultureCompass.Domain.Tests.Experiments;

public class MarketingExperimentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private static MarketingExperiment New() => MarketingExperiment.Create(
        "exp_1", "cam_1", "h", "s", "p", "m", 14, "≥ 20%", thresholdIsProposedDefault: true, "yes", "no");

    [Fact]
    public void Starting_sets_StartedAt_once_and_concluding_sets_ConcludedAt()
    {
        var e = New();

        e.UpdateTracking(ExperimentStatus.Running, null, null, T0);
        e.UpdateTracking(ExperimentStatus.Running, "week 1", null, T0.AddDays(7)); // still running: StartedAt unchanged
        e.UpdateTracking(ExperimentStatus.Concluded, "done", null, T0.AddDays(14));

        e.StartedAt.ShouldBe(T0);
        e.ConcludedAt.ShouldBe(T0.AddDays(14));
    }

    [Fact]
    public void Jumping_straight_to_an_end_state_backfills_StartedAt()
    {
        var e = New();

        e.UpdateTracking(ExperimentStatus.Abandoned, null, "not pursued", T0);

        e.StartedAt.ShouldBe(T0);
        e.ConcludedAt.ShouldBe(T0);
    }

    [Fact]
    public void Reopening_clears_ConcludedAt_and_resetting_to_Proposed_clears_both()
    {
        var e = New();
        e.UpdateTracking(ExperimentStatus.Concluded, null, null, T0);

        e.UpdateTracking(ExperimentStatus.Running, null, null, T0.AddDays(1));
        e.ConcludedAt.ShouldBeNull();
        e.StartedAt.ShouldBe(T0);

        e.UpdateTracking(ExperimentStatus.Proposed, null, null, T0.AddDays(2));
        e.StartedAt.ShouldBeNull();
        e.ConcludedAt.ShouldBeNull();
    }
}
