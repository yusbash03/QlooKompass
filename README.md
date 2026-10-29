# Kulture Compass

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> Evidence-grounded cultural growth planning for small fashion, streetwear and lifestyle brands, starting
> with Nigeria. **Qloo** supplies the cultural evidence, an **LLM** interprets it, and you decide.

You describe your brand (category, products, location, objective, a few reference artists or brands). Kulture
Compass then:
1. asks Qloo what your audience's taste looks like;
2. has an LLM turn that evidence into insights, collaboration ideas and campaigns;
3. validates that every recommendation cites real retrieved evidence;
4. gives you a measurable experiment for each campaign.

Retrieved facts and AI-generated hypotheses stay separate everywhere: in the data, in the API and in the UI.

**Status:** the backend (this repository, slices BE-0 to BE-10) is complete and verified against the live Qloo
hackathon API and Gemini. The PWA is a **separate project**, `QlooKompassUI` (React + Vite), checked out next to
this one at `../../frontend/QlooKompassUI` and still in progress. See [ENGINEERING_REPORT.md](ENGINEERING_REPORT.md)
and [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

---

## Setup

### Prerequisites

| Tool | Needed for |
|---|---|
| [Git](https://git-scm.com/) | cloning |
| [Docker](https://docs.docker.com/get-docker/) with Compose v2 | option A, and the test suite (Testcontainers) |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | option B, and running tests |
| PostgreSQL 17 | option B only (or run just the `db` service from compose) |
| `curl`, `python3` | `scripts/demo.sh` |
| A Qloo API key and a Gemini API key ([Google AI Studio](https://aistudio.google.com/apikey)) | live mode only; mock mode needs no keys |

```bash
git clone https://github.com/yusbash03/QlooKompass.git
cd QlooKompass
```

### Option A: Docker, mock mode (no keys, fastest)

```bash
QLOO_MODE=Mock LLM_PROVIDER=Mock docker compose up --build   # db → migrate → api on http://localhost:8080
curl http://localhost:8080/api/v1/health/ready                # expect "status": "Healthy"
scripts/demo.sh                                               # terminal walkthrough: register, generate a plan, read it
```
Mock mode uses hand-written synthetic Qloo fixtures (every entity is named "Synthetic …") and a deterministic
mock LLM. Every plan says `isMock: true`, and its text is prefixed `[MOCK]`.

### Option A': Docker, live mode (real Qloo + Gemini)

```bash
cp .env.example .env    # gitignored. Set QLOO_MODE=Live, QLOO_API_KEY, LLM_PROVIDER=Gemini, LLM_API_KEY,
                        # and JWT_SIGNING_KEY=$(openssl rand -base64 48)
docker compose up --build
scripts/demo.sh
```
A live plan takes about 15–20 s: ~12 Qloo calls and 2 Gemini calls (`gemini-flash-lite-latest`).
Check `GET /api/v1/meta` to confirm which modes are active.

### Option B: .NET SDK, no Docker for the API

`appsettings.Development.json` defaults to **live** Qloo + Gemini, so either set the keys or force mock mode.

```bash
cd backend
# 1. Database (skip if you have Postgres already): docker compose up -d db   (from the repo root)
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=kulture_compass;Username=kc;Password=change-me" --project src/KultureCompass.Api

# 2. Keys, for live mode only
dotnet user-secrets set "Qloo:ApiKey" "<Qloo key>"   --project src/KultureCompass.Api
dotnet user-secrets set "Llm:ApiKey"  "<Gemini key>" --project src/KultureCompass.Api

# 3. Migrate, then run
dotnet run --project src/KultureCompass.Api -- --migrate          # apply EF Core migrations, then exit
dotnet run --project src/KultureCompass.Api                        # live, http://localhost:5206
Qloo__Mode=Mock Llm__Provider=Mock dotnet run --project src/KultureCompass.Api   # or: mock, no keys
```
API docs in Development: `http://localhost:5206/scalar` (UI) and `/openapi/v1.json`.

### Troubleshooting
- **Startup fails with a Qloo/LLM key error:** live mode is on without a key. Add the key or use mock mode.
- **Port 5432 or 8080 already in use:** stop the other service, or change the host port in `docker-compose.yml`.
- **Tests fail to start containers:** Docker must be running; Testcontainers starts its own Postgres.
- **Gemini 429 / quota:** develop in mock mode, or switch to `LLM_PROVIDER=OpenAiCompatible` (Groq/OpenRouter).
The API contract lives in [contracts/openapi.yaml](contracts/openapi.yaml); a drift check keeps the backend
matching it.

Dev tool: `dotnet run --project src/KultureCompass.Api -- research --profile-json $PWD/samples/lagos-streetwear-sample.json`
prints the evidence table for a brief without an LLM or database.

## Running with the PWA (local dev)

```bash
dotnet run --project backend/src/KultureCompass.Api                   # http profile → http://localhost:5206
cd ../../frontend/QlooKompassUI && npm install && npm run dev         # Vite proxies /api → :5206 (same-origin cookie)
```
**Keeping the contract in sync.** The frontend generates its API types (`npm run gen:api`) from its own copy at
`MdFiles/contracts/openapi.yaml`. This repo's `contracts/openapi.yaml` is what the backend is drift-checked against.
Whenever either copy changes, copy it across and run the drift check. To check that the copies match:
`cmp contracts/openapi.yaml ../../frontend/QlooKompassUI/MdFiles/contracts/openapi.yaml`.

## Tests

```bash
dotnet test backend/KultureCompass.sln --filter "Category!=Live"     # 296 tests; needs Docker for Postgres
LLM_API_KEY=... QLOO_API_KEY=... dotnet test backend/KultureCompass.sln --filter "Category=Live"   # real providers
```
CI (`.github/workflows/`) does three things:
- builds in Release with warnings as errors and runs the non-Live tests;
- applies migrations to a fresh Postgres, boots the API and runs `scripts/openapi-drift.py`;
- scans the git history with gitleaks (`.gitleaks.toml`).

## Deploying to Render

`render.yaml` is a Render Blueprint for the whole app: Postgres, the API (Docker) and the PWA (static site from
`yusbash03/QlooKompassUI`). Dashboard → **New → Blueprint** → select this repo, then enter `Qloo__ApiKey` and
`Llm__ApiKey` when prompted.
- The API reads Render's `PORT` and `DATABASE_URL` directly and migrates on startup
  (`Database__MigrateOnStartup=true`). The JWT signing key is generated by Render.
- The UI proxies `/api/*` to the API, so the browser stays on one origin. This is required for the refresh cookie,
  because two `*.onrender.com` hosts count as different sites.
- After the first deploy: open the UI URL in a fresh browser, register, generate a plan, reload the page (the
  session must survive, which proves the refresh cookie works), then check `/api/v1/meta` shows `Live`/`Gemini`.

## Configuration

Every setting is listed in [.env.example](.env.example) with its .NET name. Important ones:

| Setting | Default | Notes |
|---|---|---|
| `Qloo:Mode` | `Mock` | `Live` requires `Qloo:ApiKey` (startup fails otherwise) |
| `Llm:Provider` | `Mock` | `Gemini` (verified) or `OpenAiCompatible` (unverified) |
| `Jwt:SigningKey` | dev placeholder | Production refuses the committed placeholder; use `openssl rand -base64 48` |
| `Features:EvaluationMode` / `SamplePlan` / `ExperimentTracking` | on | each off → that endpoint returns 503 `feature_disabled` |
| `Generation:DailyLimitPerUser` | 10 | plans per user per UTC day |

Secrets never go in appsettings, `VITE_*` variables, logs or Git. Locally they live in user-secrets or the
gitignored `.env`.

## How it works

```
POST /growth-plans ─► plan Queued ─► in-process queue ─► worker (2 at a time)
                                                          │
   InterpretingBrief (LLM call A, falls back deterministically)
 → PlanningResearch (fixed Qloo op menu, ≤12 calls) → RetrievingEvidence (Qloo, live progress)
 → EvaluatingEvidence (deterministic quality labels, min-evidence gate)
 → GeneratingRecommendations (LLM call B, JSON schema) → ValidatingOutput (every evidence key must exist; ≤2 repairs)
 → DesigningExperiments (deterministic) → Saving
GET /growth-plans/{id}/status ◄── stage_log persisted after every stage
```
- Clean Architecture modular monolith: Api → Infrastructure → Application → Domain. No MediatR, no broker.
- ASP.NET Core Identity + self-issued HS256 JWTs (15 min) + rotating HttpOnly refresh cookie with reuse detection.
- Decisions are recorded as ADRs in [docs/adr/](docs/adr/). Specs live in `docs/`.
- Verified provider behaviour: [docs/backend/qloo-verified-contract.md](docs/backend/qloo-verified-contract.md),
  [docs/backend/llm-verified-contract.md](docs/backend/llm-verified-contract.md).
  Security checks: [docs/backend/security-review.md](docs/backend/security-review.md).

## Repository layout

| Path | Contents |
|---|---|
| `backend/` | .NET 10 solution (`src/` Domain, Application, Infrastructure, Api; `tests/` 5 test projects + synthetic fixtures) |
| `contracts/` | `openapi.yaml` (API source of truth), `growth-plan-llm-output.schema.json` (LLM output schema v1.0) |
| `docs/` | Product, API, security, testing and deployment specs; backend design docs; ADRs |
| `scripts/` | `demo.sh`, `openapi-drift.py`, `qloo-probe.*` |
| `design/`, `MdDocs/` | Design prompts and the original spec pack |
| `../../frontend/QlooKompassUI` | the PWA: a separate repository and sibling checkout, not part of this repo |

## Decisions and trade-offs

| Topic | Decision | Why |
|---|---|---|
| Backend | .NET 10, minimal APIs, EF Core 10 + Npgsql, FluentValidation, Serilog, built-in OpenAPI + Scalar | Clean Architecture without MediatR |
| Auth | Self-hosted JWT on ASP.NET Core Identity | No third-party identity dependency; Identity handles hashing/lockout |
| LLM | Gemini behind `ILlmClient`; deterministic mock always available; OpenAI-compatible fallback | Free tier with native JSON-schema output |
| Async generation | DB-persisted status + `Channel<Guid>` worker + polling | No message broker needed |
| Confidence | Deterministic evidence-quality rules, never an LLM-made probability | Explainable and testable |

**The LLM trade-off:** Gemini's free tier is small and isn't private. Send no personal data (we only send
business briefs), develop in mock mode, and move to a paid tier before any real launch. If you hit quota, switch
to `LLM_PROVIDER=OpenAiCompatible` (Groq/OpenRouter).

## Things you still need to do yourself
- Finish the PWA (`QlooKompassUI`) and give it a Dockerfile + nginx config (proxy `/api/` → `api:8080`), then
  `docker compose --profile web up --build`.
- Pick public hosting (see [docs/shared/04-deployment-and-demo.md](docs/shared/04-deployment-and-demo.md)) and serve the
  API same-origin under `/api` so the refresh cookie works.
- **Rotate the Qloo and Gemini keys after the hackathon.** Both were pasted into a chat during development.
- Review Qloo's terms before enabling the response cache (`Qloo:CacheTtlMinutes`, off by default).

## License

Released under the [MIT License](LICENSE). The license covers this code only: Qloo and Gemini data and APIs are
governed by their providers' own terms, and you need your own keys to use them.
