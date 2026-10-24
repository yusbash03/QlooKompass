using KultureCompass.Domain.Plans;

namespace KultureCompass.Application.Research;

/// <summary>Combines the user-provided brief (<see cref="BriefSnapshot"/> — reference entities, primary location,
/// budget) with the AI-proposed reading of it (<see cref="BriefInterpretation"/> — tag terms, suggested seeds,
/// parsed country/city). <see cref="ResearchPlanner"/> needs both: the fixed op menu (BE-03 §4) resolves the
/// user's own reference entities (R1) as well as AI-suggested ones, and R7's location filter uses the snapshot's
/// raw <c>PrimaryLocation</c> text rather than the interpretation's parsed city/country.</summary>
public sealed record ResearchBrief(BriefSnapshot Snapshot, BriefInterpretation Interpretation);
