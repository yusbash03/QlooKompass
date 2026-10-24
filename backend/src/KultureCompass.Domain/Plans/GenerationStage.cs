namespace KultureCompass.Domain.Plans;

public enum GenerationStage
{
    Queued,
    InterpretingBrief,
    PlanningResearch,
    RetrievingEvidence,
    EvaluatingEvidence,
    GeneratingRecommendations,
    ValidatingOutput,
    DesigningExperiments,
    Saving,
    Done,
}
