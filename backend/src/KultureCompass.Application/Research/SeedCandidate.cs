using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Research;

/// <summary>A name to resolve against Qloo /search in R1, before it becomes a <see cref="ResolvedSeed"/>.</summary>
public sealed record SeedCandidate(string Name, ReferenceKindHint KindHint, SeedOrigin Origin);

/// <summary>A <see cref="SeedCandidate"/> successfully matched to a Qloo entity (similarity >= threshold, BE-03 §4).</summary>
public sealed record ResolvedSeed(SeedCandidate Candidate, Qloo.QlooEntity Entity);
