# ADR 008 — No committed real Qloo data; synthetic fixtures only; cache disabled by default

**Decision:** Raw Qloo API responses are never written to any file tracked by Git. Probe output goes
to the gitignored `qloo-probe-output/`. Test/mock fixtures under `backend/tests/Fixtures/qloo/` are
hand-written synthetic JSON shaped like the (documented or verified) contract, with every entity name
prefixed `"Synthetic "` and a top-level `"_synthetic": true` marker. The optional in-memory response
cache (`Qloo:CacheTtlMinutes`) defaults to `0` (disabled).

**Why:** The hackathon's terms are reported to forbid publishing Qloo response data. Committing real
responses — even in a private-looking test fixture — risks violating that, and would be impossible to
fully scrub from Git history later. Synthetic fixtures give us deterministic, reviewable test data
without that risk.

**Consequences:** Fixtures must be kept in sync by hand with `docs/backend/qloo-verified-contract.md`
whenever the verified contract changes. Enabling the cache requires first confirming Qloo's terms
permit it.
