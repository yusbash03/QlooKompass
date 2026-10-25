using KultureCompass.Domain.Common;

namespace KultureCompass.Domain.Experiments;

public sealed class MarketingExperiment
{
    public string Id { get; private set; } = null!;
    public string LinkedRecommendationId { get; private set; } = null!;
    public string Hypothesis { get; private set; } = null!;
    public string Segment { get; private set; } = null!;
    public string ProposedCampaign { get; private set; } = null!;
    public string PrimaryMetric { get; private set; } = null!;
    public int MeasurementPeriodDays { get; private set; }
    public string SuccessThreshold { get; private set; } = null!;
    public bool ThresholdIsProposedDefault { get; private set; }
    public string IfSuccess { get; private set; } = null!;
    public string IfFailure { get; private set; } = null!;
    public ExperimentStatus Status { get; private set; }
    public string? ObservedResult { get; private set; }
    public string? ResultNotes { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? ConcludedAt { get; private set; }
    public uint Version { get; private set; }

    /// Reserved for EF Core materialization (field-based property access).
    private MarketingExperiment()
    {
    }

    private MarketingExperiment(
        string id, string linkedRecommendationId, string hypothesis, string segment, string proposedCampaign,
        string primaryMetric, int measurementPeriodDays, string successThreshold, bool thresholdIsProposedDefault,
        string ifSuccess, string ifFailure)
    {
        Id = id;
        LinkedRecommendationId = linkedRecommendationId;
        Hypothesis = hypothesis;
        Segment = segment;
        ProposedCampaign = proposedCampaign;
        PrimaryMetric = primaryMetric;
        MeasurementPeriodDays = measurementPeriodDays;
        SuccessThreshold = successThreshold;
        ThresholdIsProposedDefault = thresholdIsProposedDefault;
        IfSuccess = ifSuccess;
        IfFailure = ifFailure;
        Status = ExperimentStatus.Proposed;
    }

    public static MarketingExperiment Create(
        string id,
        string linkedRecommendationId,
        string hypothesis,
        string segment,
        string proposedCampaign,
        string primaryMetric,
        int measurementPeriodDays,
        string successThreshold,
        bool thresholdIsProposedDefault,
        string ifSuccess,
        string ifFailure)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new DomainException("Experiment Id is required.");
        if (string.IsNullOrWhiteSpace(linkedRecommendationId))
            throw new DomainException("Experiment must link to an existing recommendation id.");
        Guard.Range(measurementPeriodDays, nameof(MeasurementPeriodDays), 1, 90);

        return new MarketingExperiment(
            id, linkedRecommendationId, hypothesis, segment, proposedCampaign, primaryMetric,
            measurementPeriodDays, successThreshold, thresholdIsProposedDefault, ifSuccess, ifFailure);
    }

    /// <summary>Records progress. <see cref="StartedAt"/> is set the first time the experiment becomes Running (and
    /// back-filled if it jumps straight to an end state); <see cref="ConcludedAt"/> is set on Concluded/Abandoned and
    /// cleared if it is reopened.</summary>
    public void UpdateTracking(ExperimentStatus status, string? observedResult, string? resultNotes, DateTimeOffset now)
    {
        if (status is ExperimentStatus.Running or ExperimentStatus.Concluded or ExperimentStatus.Abandoned)
            StartedAt ??= now;
        ConcludedAt = status is ExperimentStatus.Concluded or ExperimentStatus.Abandoned ? ConcludedAt ?? now : null;
        if (status == ExperimentStatus.Proposed)
            StartedAt = null;

        Status = status;
        ObservedResult = Guard.OptionalMaxLength(observedResult, nameof(ObservedResult), 200);
        ResultNotes = Guard.OptionalMaxLength(resultNotes, nameof(ResultNotes), 1000);
    }
}
