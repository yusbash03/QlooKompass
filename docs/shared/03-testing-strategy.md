# 03 — Testing Strategy

Tests ship with each slice. A slice is not done until its tests pass locally.

## 1. Fixture policy
- **No real Qloo responses in Git** (hackathon terms restrict publishing response data). Probe output goes to the
  gitignored `qloo-probe-output/`.
- Fixtures in `backend/tests/Fixtures/qloo/*.json` are **hand-written synthetic** JSON shaped like the verified
  contract (`docs/backend/qloo-verified-contract.md`), with invented entity names prefixed `Synthetic ` and a top-level
  `"_synthetic": true`. The mock Qloo provider uses the same fixtures.
- LLM fixtures: valid output, invalid JSON, schema-valid-but-bad-refs, oversize output.

## 2. Backend (xUnit + FluentAssertions-free assertions or Shouldly; pick one and stay consistent)
| Project | Focus | Examples |
|---|---|---|
| `Domain.Tests` | Pure rules | status transitions (illegal → throws), evidence-key format, quality classification table, strength aggregation |
| `Application.Tests` | Use cases with fakes | brief validation; research planner bounds (≤ MaxCallsPerPlan); empty Qloo → `CompletedWithLimitations` or `Failed(qloo_no_evidence)`; partial Qloo failure; invalid LLM output → repair → success; repair exhausted → Failed; unknown evidence key rejected; collaboration partner not in evidence rejected; duplicate idempotency key returns same plan; key reuse with different body → conflict; experiments generated per campaign |
| `Infrastructure.Tests` | HTTP + EF | Qloo client: `X-Api-Key` header present & not logged; query serialisation (`filter.type`, `signal.interests.entities` comma-joined); deserialisation of synthetic fixtures; 429 honours `Retry-After`; 5xx retried ≤ N; 400/401 not retried; timeout → `QlooUnavailable`; non-JSON content type → error. Gemini client: request has `responseMimeType=application/json` + schema; maps 429 → `LlmQuotaExceeded`. EF: migrations apply on Testcontainers PostgreSQL; unique (owner_id, idempotency_key) enforced; jsonb round-trip |
| `Api.Tests` | `WebApplicationFactory` + Testcontainers Postgres (real JWT issuance via a test signing key) | auth: register → login → access protected endpoint; wrong password → 401 `invalid_credentials`; 5 failures → 423; refresh rotates (old cookie then fails); reusing a rotated refresh token revokes the family; refresh without `X-Requested-With` → 400/401; logout revokes; expired/tampered/wrong-issuer/wrong-audience/`alg=none` tokens rejected; password hash never returned; 401 without token; 404 for other owner's plan; validation → 400 problem+json with `code`; PUT without If-Match → 428; stale → 412; POST plan → 202 + Location; replay → same id; health endpoints; `/meta` reveals no secrets |
| `Integration.Live` (category `Live`, skipped unless `QLOO_API_KEY` set) | Real Qloo | search returns entities with ids; insights for a known seed returns results; error shape for bad request |

Commands:
```bash
dotnet test backend/KultureCompass.sln --filter "Category!=Live"
QLOO_API_KEY=… dotnet test backend/KultureCompass.sln --filter "Category=Live"
```

## 3. Frontend (Vitest + React Testing Library + MSW)
- MSW handlers generated around the OpenAPI types; one handler file per feature.
- Must-have tests: profile form validation (client Zod mirrors server limits); submit sends `Idempotency-Key`
  and reuses it on retry; generate disabled offline; progress view renders only backend-reported stages;
  plan view separates "Retrieved from Qloo" vs "AI-generated"; evidence chips resolve to drawer; unlinked mentions
  labelled; empty/partial/failed states; mock banner when `isMock`; sign-out clears query cache + IndexedDB;
  install prompt only when `beforeinstallprompt` fired.
- PWA: unit-test the SW config object (no `/api/` runtime caching); Playwright e2e checks manifest, SW registration,
  offline navigation to `/app/plans` shows offline notice, sample plan readable offline after first visit.
- Accessibility: `vitest-axe` on key pages; Playwright + `@axe-core/playwright` on landing, form, plan.

## 4. End-to-end acceptance (Playwright, mock mode in CI)
1. Visit landing → open sample plan.
2. Register a fresh test user (or seeded demo user) → create profile → generate → progress → plan rendered.
3. Every evidence chip opens a drawer whose `key` exists in evidence list.
4. Reload → plan appears in Saved Plans.
5. Same scenario documented as a **manual live run** with real keys; results recorded in the final engineering report.

## 5. CI (GitHub Actions)
`backend.yml`: restore, build, test (non-Live) with Postgres service, OpenAPI drift check.
`frontend.yml`: install, typecheck, lint, test, build, Lighthouse-CI PWA assertions (optional).
`secrets.yml`: gitleaks.
