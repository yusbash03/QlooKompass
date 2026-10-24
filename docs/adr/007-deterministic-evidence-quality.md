# ADR 007 — Evidence quality is deterministic, never an LLM-made probability

**Decision:** `Strong`/`Moderate`/`Weak` evidence quality (and the derived recommendation
`EvidenceStrength`) is computed by a pure, unit-tested `EvidenceQualityRules` class from documented,
config-backed thresholds (seed-match confidence, affinity thresholds, operation count) — never asked
of or influenced by the LLM.

**Why:** An LLM-reported "confidence" score is not a calibrated probability and would be
indistinguishable from a plausible-sounding guess to the user. A deterministic, documented rule set
is explainable (`QualityReasons` lists exactly which clauses fired), reproducible, and testable without
any LLM call.

**Consequences:** Improving evidence-quality classification means changing the rules/thresholds (code
+ tests), not prompt-tuning. The UI tooltip explicitly says this is a heuristic, not a probability.
