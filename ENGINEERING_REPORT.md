# Engineering report — Kulture Compass backend

Date: 2026-10-25 · Scope: backend slices BE-0 to BE-10 (`docs/backend/BE-05-build-plan.md`) · Branch:
`be/qloo-fix-auth-generation` (fast-forwards onto `main`).

Everything below that says *verified* was executed; *mock* means synthetic fixtures or the deterministic mock
LLM; *unverified* means it was not run against the real thing.

## 1. What was implemented
- **Qloo client** (`/search`, `/v2/tags`, `/v2/insights`): typed requests, throttling, retries that honour
  Retry-After, error mapping, and a mock over synthetic fixtures.
- **Cultural-intelligence research:**
  - deterministic op menu R1–R8 within a 12-call budget;
  - seed matching, location guard, dedupe, deterministic `ev_###` keys;
  - evidence-quality rules (deterministic, never LLM-made).
- **Auth:** ASP.NET Core Identity, HS256 JWTs, rotating HttpOnly refresh cookie with family reuse detection,
  CSRF header guard, lockout, rate limits.
- **Business profiles API**, with If-Match/ETag concurrency.
- **LLM layer:** Gemini (verified), OpenAI-compatible (unverified), and a deterministic mock. Versioned prompts.
- **8-stage generation pipeline:**
  - brief interpretation with a deterministic fallback;
  - synthesis with JSON schema;
  - `OutputValidator` (every BE-04 rule; keys must exist) with ≤ 2 repairs and a drop step;
  - experiment designer;
  - stage_log persisted after every stage.
- **Growth-plans API:**
  - idempotency (SHA-256 request hash);
  - one active plan per profile;
  - daily quota with Retry-After;
  - background worker (2 at a time) and startup recovery;
  - status, list and quota endpoints.
- **BE-8:**
  - experiment tracking (PATCH with If-Match);
  - evaluation mode: grounded vs LLM-only, scored deterministically;
  - public synthetic sample plan;
  - all behind feature flags.
- **Hardening:**
  - security headers, 64 KB body limit, HTTPS/HSTS, forwarded-headers opt-in, startup guards;
  - log-redaction tests;
  - OpenAPI drift script, gitleaks config, GitHub Actions.
- **Ops:** multi-stage non-root Dockerfile, compose (db → migrate → api, `web` profile), demo script, docs.

## 2. Architecture and key decisions
Modular monolith, Clean Architecture (Api → Infrastructure → Application → Domain, enforced by NetArchTest),
minimal APIs, focused stores instead of a generic repository, no MediatR, no message broker. ADRs: `docs/adr/001`–`008`.

Decisions made during the build:
- **Login without `SignInManager`:** it uses `UserManager` lockout primitives, which keeps HTTP types out of
  Infrastructure (02-security §1 updated).
- **Gemini schema retry:** Gemini rejects the full v1.0 schema for complexity, so the client retries once with
  array bounds stripped and caches that per schema.
- **Status polling uses a projection:** EF always loads owned collections with their owner, and polls are
  frequent.
- **Startup recovery fails every Running plan.** The deployment is single-process, so none can still be live.
- **Evaluation idempotency without a migration:** the grounded plan's key is derived from the evaluation's key.
- **Mock search echoes the user's query** so mock mode works for any brief.
- **Names from the brief are never "AI suggestions".** The user's own brand and references are never labelled as
  such or counted as unsupported.

## 3. Projects and files
`backend/src`: `KultureCompass.Domain`, `.Application` (Auth, Profiles, Plans, Generation incl. prompts and schemas,
Evaluation, Research, Samples), `.Infrastructure` (Persistence, Identity, Qloo, Llm, Generation worker), `.Api`
(endpoints, auth wiring, middleware). `backend/tests`: Domain, Application, Infrastructure, Api, Architecture.
Repo: `docker-compose.yml`, `backend/Dockerfile`, `.dockerignore`, `.github/workflows/{backend,secrets}.yml`,
`.gitleaks.toml`, `scripts/{demo.sh,openapi-drift.py}`, `KNOWN_LIMITATIONS.md`, this report, and the
`docs/backend/*-verified-contract.md` and `security-review.md` docs.

## 4. Qloo: verified vs unverified
All verified with the hackathon key against `https://hackathon.api.qloo.com`, 2026-10-24/25. Details are in
`docs/backend/qloo-verified-contract.md`.

| Item | Status |
|---|---|
| Auth header `X-Api-Key`; wrong key → 401 | ✅ verified |
| `/search?query=&types=urn:entity:…&take=`, flat `results[]` with `types[]` | ✅ verified |
| `/v2/tags?filter.query=` | ✅ verified |
| `/v2/insights`: entity filter types (brand/artist/person/place/podcast/tv_show/videogame) with entity or tag signals, `filter.location.query` | ✅ verified |
| `/v2/insights?filter.type=urn:tag` → `results.tags[]` (fixed BE-4's empty R5) | ✅ verified |
| `urn:entity:video_game` (listed in the official guide) → 400; `videogame` works | ✅ verified (guide is wrong) |
| `urn:demographics` | responds, but isn't entity-shaped; feature off |
| Rate-limit headers (10 rps / 50k per month on this key) | ✅ observed |
| Error bodies | known shape, not parsed |

## 5. LLM provider
Gemini `generateContent` with `responseJsonSchema`, header `x-goog-api-key`, model `gemini-flash-lite-latest`
(resolved to `gemini-3.5-flash-lite`), temperature ≤ 0.5, `maxOutputTokens` 6000, ≤ 2 repairs.
Measurements are in `docs/backend/llm-verified-contract.md`. A typical plan used ~4.4–5.0k prompt and
~2.2–2.5k output tokens over 2 calls. `OpenAiCompatible` is implemented but **unverified** live.

## 6. Database
PostgreSQL 17, EF Core 10, snake_case. One migration, `20261024122741_InitialCreate`, creating:
`users`, `user_claims`, `user_logins`, `user_tokens`, `refresh_tokens`, `business_profiles`, `growth_plans`,
`growth_plan_evidence`, `growth_plan_recommendations`, `marketing_experiments`, `evaluation_runs`.
- Concurrency uses `xmin`.
- Unique `(owner_id, idempotency_key)`.
- jsonb for snapshots, stage log and payloads.
- Later slices needed no further migrations (checked with `dotnet ef migrations has-pending-model-changes`).

## 7. Build and test results (executed)
```bash
dotnet build backend/KultureCompass.sln -c Release          # 0 warnings, 0 errors (warnings are errors)
dotnet test  backend/KultureCompass.sln --filter "Category!=Live"
#   Domain 39 · Application 136 · Architecture 3 · Infrastructure 51 · Api 63  → 292 passed, 0 failed
LLM_API_KEY=… QLOO_API_KEY=… dotnet test backend/KultureCompass.sln --filter "Category=Live"
#   6 Live tests (2 Qloo, 4 Gemini incl. full live pipeline and the LLM-only schema): all passed when run
python3 scripts/openapi-drift.py openapi.generated.json contracts/openapi.yaml   # 21 operations, 0 drift
docker run … zricethezav/gitleaks:v8.21.2 git /repo --config /repo/.gitleaks.toml # no leaks (history + tree)
```
One unreproduced test-infrastructure flake is recorded in KNOWN_LIMITATIONS (Testing).

## 8. Real services vs mocks

| Feature | Real services | Mock / tests only |
|---|---|---|
| Qloo search/tags/insights/location | ✅ live (tests, research CLI, full pipeline, HTTP and compose demos) | fixtures for CI |
| Gemini structured output (plan schema, LLM-only schema, interpretation) | ✅ live | scripted fakes for edge cases |
| Repair/drop path | ❌ never needed in live runs (0 repairs) | ✅ scripted fakes |
| Gemini 429 / quota handling | ❌ not observed live | ✅ fake HTTP responses |
| OpenAI-compatible provider | ❌ unverified | ✅ fake HTTP responses |
| Full plan via HTTP (dotnet run + Postgres) | ✅ live, 60 evidence, ~17 s | ✅ mock |
| Full plan via Docker compose | ✅ live (twice) and mock | — |
| Evaluation mode end to end | ✅ live via compose (grounded 10/10 traceable vs LLM-only 0/4) | ✅ mock |
| Auth security (token claims, alg=none, wrong key, cookie flags) | ✅ manual against the running API | ✅ automated |
| Refresh cookie in a real browser | ❌ not checked (no frontend) | — |
| PWA / frontend | ❌ not built | — |

## 9. Blockers and known limitations
No blockers for the backend. Open items, all in `KNOWN_LIMITATIONS.md`:
- the frontend is missing;
- there's no public deployment;
- the in-process queue assumes a single instance;
- the Gemini schema workaround;
- unverified provider paths;
- the test flake.

## 10. Exact local run commands
```bash
QLOO_MODE=Mock LLM_PROVIDER=Mock docker compose up --build        # mock, no keys
cp .env.example .env && $EDITOR .env && docker compose up --build  # live
scripts/demo.sh [http://localhost:8080] [profile.json]
docker compose down -v                                             # stop + drop data
```

## 11. Next steps before public launch
- Build the PWA (FE-0 to FE-9), then run the demo in a real browser and check the refresh cookie over HTTPS.
- Deploy same-origin, with a fresh `Jwt:SigningKey`, `Jwt:CookieSecure=true` and `App:TrustForwardedHeaders` set
  for the proxy. Then verify the full demo on the public URL. Don't call it deployed before that.
- Move to a paid LLM tier (free tier isn't private and its quota is small); verify the OpenAI-compatible fallback.
- Review Qloo's terms before enabling the response cache or any public-facing use; **rotate the Qloo and Gemini
  keys**, which were shared in a chat.
- Add a privacy policy and data retention (including the refresh-token cleanup job). Team accounts are out of MVP.
