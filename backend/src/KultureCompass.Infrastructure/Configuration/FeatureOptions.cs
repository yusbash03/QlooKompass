namespace KultureCompass.Infrastructure.Configuration;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool EvaluationMode { get; set; }

    public bool SamplePlan { get; set; }

    public bool ExperimentTracking { get; set; }
}
