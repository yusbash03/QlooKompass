# BE-05 — Backend Build Plan (Claude Code slices)

Run slices in order. Paste the **Prompt** into Claude Code. Each slice ends with: build ✔, tests ✔, docs updated,
summary of real-vs-mock verification, then **stop** so you can review and commit.

---
## BE-0 — Qloo probe & verified contract
**Read:** `docs/backend/BE-03-qloo-integration.md` §1–2, `docs/shared/02-security-and-config.md` §2
**Prompt:**
> Implement slice BE-0 from docs/backend/BE-05-build-plan.md. Create `scripts/qloo-probe.sh` and `scripts/qloo-probe.ps1`
> exactly as specified in BE-03 §2 (read key from env, never print it, ≤ 4 rps, save raw output to gitignored
> `qloo-probe-output/`). Add `.gitignore` entries. Then run the bash script (I have exported QLOO_API_KEY and QLOO_BASE_URL).
> From the real responses, write `docs/backend/qloo-verified-contract.md` (per request: verified/failed, exact param spelling,
> JSON paths, error shapes, rate-limit headers, Nigeria coverage notes) and update the status column in BE-03 §1.
> Then hand-write synthetic fixtures under `backend/tests/Fixtures/qloo/` (entity names prefixed "Synthetic ", `"_synthetic": true`).
> Do not copy any real names or values from the probe output into committed files.
**Done when:** verified contract doc exists; ≥ search + insights + tags verified or a blocker documented; fixtures exist; `git status` shows no probe output.

## BE-1 — Solution scaffold
**Read:** BE-01 all.
**Prompt:**
> Implement BE-1: create `backend/` .NET 10 solution per BE-01 §1 (projects, Directory.Build.props, central package management,
> test projects). Wire Program.cs: Serilog JSON console, options binding with ValidateOnStart (AppOptions, QlooOptions, LlmOptions,
> JwtOptions, GenerationOptions, FeatureOptions), JSON enum strings, ProblemDetails + exception handler mapping to
> `code`/`traceId`, `/api/v1/health` and `/api/v1/health/ready` (Npgsql check), `/api/v1/meta`, built-in OpenAPI + Scalar in Dev,
> CORS policy, security headers middleware. Create docs/adr/001–008 from BE-01 §6 (short). Add an architecture test enforcing
> dependency direction. Copy backend/CLAUDE.md into place. Build and test.
**Done when:** `dotnet build` / `dotnet test` green; `/api/v1/health` 200; `/api/v1/meta` returns modes; no secrets in appsettings.

## BE-2 — Domain + persistence
**Read:** BE-02.
**Prompt:**
> Implement BE-2: domain entities, enums, state machine and invariants from BE-02 §2–5 with unit tests for every transition and
> invariant; evidence quality rules from BE-03 §5 as a pure `EvidenceQualityRules` class with table-driven tests. Then
> Infrastructure persistence: AppDbContext, snake_case naming, configurations matching BE-02 §6 (jsonb, text[], indexes,
> unique constraints, xmin concurrency), focused stores (`IBusinessProfileStore`, `IGrowthPlanStore`, `IEvaluationStore`),
> initial migration, design-time factory, and a `--migrate` CLI switch in Api. Add Testcontainers tests that apply migrations
> and verify unique (owner_id, idempotency_key) and jsonb round-trips.
**Done when:** migration applies to a fresh Postgres; domain + infra tests green.

## BE-3 — Qloo client
**Read:** BE-03 §3, `qloo-verified-contract.md`.
**Prompt:**
> Implement BE-3: `IQlooClient` + Application models, Infrastructure `QlooClient` using only the VERIFIED contract, typed
> `InsightsRequest` builder + `QlooQueryBuilder`, handlers (`QlooApiKeyHandler`, `QlooThrottleHandler`, `RedactingLoggingHandler`),
> standard resilience with Retry-After-aware delays and no retries on 400/401/403/404, `QlooResult` error mapping, `MockQlooClient`
> over synthetic fixtures, mode switch by `Qloo:Mode`. Unit tests with a fake HttpMessageHandler covering every item in
> 03-testing-strategy §2 Infrastructure/Qloo. Add `[Trait("Category","Live")]` tests that run only when QLOO_API_KEY is set.
> Run the Live tests once and report results honestly.
**Done when:** unit tests green; Live tests pass or failures are documented with cause.

## BE-4 — Cultural intelligence service
**Read:** BE-03 §4–5.
**Prompt:**
> Implement BE-4: `ResearchPlanner` (deterministic op menu R1–R8, only ops marked verified, budget cap, dependency order),
> seed matching with name similarity threshold, location guard using echoed localities, `CulturalIntelligenceService`
> (bounded concurrency 3, progress reporting, partial-failure handling, normalisation, dedupe, deterministic ev_### keys,
> cap), `EvidenceEvaluator` (quality labels + limitations + min-evidence gate). Application tests: empty results, partial
> failure, auth failure aborts, ambiguous seeds, unresolved location, budget never exceeded, deterministic key ordering.
> Add a dev-only console command `dotnet run -- research --profile-json <file>` that prints the evidence table (Live or Mock).
**Done when:** tests green; a live research run on a sample Lagos streetwear brief is executed and its summary (counts by
type/quality, limitations — no raw names in committed files) is reported.

## BE-5 — Business profiles API
**Read:** 01-api-contract, openapi.yaml (BusinessProfiles), 02-security §1.
**Prompt:**
> Implement BE-5 in two parts. PART A — auth per docs/shared/02-security-and-config.md §1: ASP.NET Core Identity (AddIdentityCore,
> AppUser with Guid key, snake_case tables, password/lockout options), `JwtTokenService` (JsonWebTokenHandler, HS256, claims sub/jti/
> iss/aud/email/sstamp), refresh tokens (hashed, rotation, family reuse detection, HttpOnly SameSite=Strict cookie on /api/v1/auth,
> X-Requested-With CSRF guard), endpoints register/login/refresh/logout/me, JWT bearer validation (MapInboundClaims=false, issuer,
> audience, lifetime, HS256 only, security-stamp check), fallback authorization policy, `auth` rate-limit policies, startup key guard,
> optional DemoUserSeeder (non-Production only), migration for Identity + refresh_tokens. Tests: every auth case in 03-testing §2 Api.Tests.
> PART B — `ICurrentUser`, profile
> handlers (create, get, list paged, update with If-Match/ETag from xmin), FluentValidation mirroring openapi limits,
> Result→ProblemDetails mapping, 404 for non-owned. API tests with a test auth scheme + Testcontainers: 401, 404 other owner,
> 400 validation shape, 428/412 concurrency, 201 + Location + ETag.
**Done when:** API tests green; generated `/openapi/v1.json` matches contracts/openapi.yaml for these paths.

## BE-6 — LLM clients + generation pipeline
**Read:** BE-04 all, growth-plan-llm-output.schema.json.
**Prompt:**
> Implement BE-6: `ILlmClient`; `GeminiLlmClient` (verify current Gemini generateContent request/response fields against
> Google's docs before coding; header auth; responseJsonSchema with fallback; error mapping), `OpenAiCompatibleLlmClient`,
> deterministic `MockLlmClient` with failure switches; prompts as versioned embedded resources; `BriefInterpreter` with
> deterministic fallback; `RecommendationSynthesizer`; `OutputValidator` (all rules in BE-04 §1 step 6) with exhaustive unit
> tests; bounded repair loop; `ExperimentDesigner`; `GrowthPlanPipeline` persisting stage_log after every stage and mapping
> failures to PlanErrorCode. Application tests: happy path (mock), invalid JSON→repair→success, unknown key→repair exhausted→
> drop or fail, LLM quota→Failed(llm_quota_exceeded), Qloo no evidence→Failed(qloo_no_evidence), limitations→CompletedWithLimitations.
**Done when:** tests green; one Gemini call verified live with a tiny schema (report model id + token usage) or documented blocker.

## BE-7 — Growth plans API + background worker
**Read:** 01-api-contract §3, BE-01 §4.
**Prompt:**
> Implement BE-7: `StartGenerationHandler` (validation, profile ownership, one-running-per-profile → 409 plan_in_progress, daily
> quota → 429 + Retry-After, idempotency with SHA-256 request hash → replay/409), `ChannelGenerationQueue`, `GenerationWorker`
> (MaxConcurrency 2, scope per plan, overall timeout), `StuckPlanRecovery` on startup, GET plan (split query, no N+1), GET status,
> list paged with filters, `/me/quota`, rate-limit policies from 02-security §5. API tests incl. replay returns same id and
> worker completes a plan end-to-end in mock mode.
**Done when:** `POST → poll → GET` works end-to-end in mock mode via tests and manually via Scalar.

## BE-8 — Experiment tracking (launch → measure) + Evaluation mode + sample plan
**Read:** BE-04 §4, openapi Evaluations.
**Prompt:**
> Implement BE-8: PATCH experiment (If-Match, only tracking fields; Proposed→Running sets started_at, →Concluded/Abandoned sets
> concluded_at; illegal transitions → 400), GET /experiments hub (owner-scoped, paged, status filter), evaluation endpoints behind Features:EvaluationMode
> (grounded plan via pipeline + LLM-only call with llm-only prompt), deterministic `EvaluationScorer` with unit tests, and
> `/sample-plan` serving an embedded synthetic plan JSON (isMock=true, isSample=true, entity names prefixed "Synthetic ").
**Done when:** tests green; evaluation runs in mock mode; feature flag off → 503 feature_disabled.

## BE-9 — Hardening
**Prompt:**
> Implement BE-9: review against 02-security fully (headers, CORS, body limits, log redaction test that asserts keys never
> appear in captured logs, startup guards for Production), add OpenAPI drift check script comparing generated doc to
> contracts/openapi.yaml (paths, operationIds, required fields), gitleaks config, GitHub Actions backend.yml with Postgres service.
> Run an auth security review: decode a real access token and confirm claims/lifetime; confirm refresh cookie flags in the browser;
> confirm tokens/passwords never appear in logs; confirm `alg=none` and wrong-key tokens are rejected; record results.
**Done when:** CI green locally (act or manual), redaction test passes, drift check passes.

## BE-10 — Docker, seed, README, report
**Read:** 04-deployment-and-demo.
**Prompt:**
> Implement BE-10: backend Dockerfile (multi-stage, non-root), docker-compose.yml (db, migrate, api, web), `.env.example` sync,
> README sections listed in the master analysis §16, KNOWN_LIMITATIONS.md, demo script, and fill ENGINEERING_REPORT.md
> truthfully with commands executed and real-vs-mock table.
**Done when:** `docker compose up --build` in mock mode serves a working API + PWA; README steps work from a clean clone.
