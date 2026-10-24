# ADR 006 — Plan content as versioned JSONB + relational evidence/recommendation/experiment tables

**Decision:** `growth_plans` stores free-form generation artifacts (brief snapshot, interpretation,
research plan, summary/limitations) as `jsonb`, tagged with a `schema_version`/`prompt_version`.
Evidence items, recommendations and experiments — the entities the UI needs to query, join, filter
and paginate — are their own relational tables (`growth_plan_evidence`,
`growth_plan_recommendations`, `marketing_experiments`) with real columns, indexes and constraints.

**Why:** The LLM output schema will evolve faster than a fully relational model could track without
constant migrations, but evidence/recommendation/experiment records need referential constraints
(unique keys, FK-like id references validated by the domain) and are read individually (e.g. PATCHing
one experiment) — a pure-jsonb blob would make that clumsy and untestable at the DB layer.

**Consequences:** Reading a full plan requires a split query across 4 tables (see BE-02 §7) instead of
one row fetch; this is an accepted cost for the query flexibility and invariant enforcement gained.
