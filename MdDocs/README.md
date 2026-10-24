# Kulture Compass — Specification Pack

> **Kulture Compass** (working name; formerly *CulturePilot*) turns a small business's brief into an
> evidence-grounded **Cultural Growth Plan** using the Qloo Taste AI API as the cultural-intelligence
> foundation and an LLM as a bounded reasoning/explanation layer.
>
> Built for the Qloo hackathon. First customer segment: Nigerian fashion, streetwear and lifestyle SMEs.

This folder is a **build pack for Claude Code** (VS Code). Drop it into the root of an empty Git
repository and let Claude Code implement the backend and the PWA slice by slice.

---

## 1. What's in the pack

| Path | Purpose | Read by |
|---|---|---|
| `CLAUDE.md` | Root rules Claude Code loads automatically: non-negotiables, conventions, build order | Claude Code (always) |
| `backend/CLAUDE.md` | Backend-only rules (copy stays in `backend/` once the solution is scaffolded) | Claude Code in `backend/` |
| `frontend/CLAUDE.md` | Frontend-only rules | Claude Code in `frontend/` |
| `.env.example` | Every configuration variable, placeholders only | You + Claude Code |
| `docs/shared/00-product-brief.md` | Vision, scope, personas, glossary, naming | Everyone |
| `docs/shared/01-api-contract.md` | Human-readable REST contract, error codes, polling, idempotency | BE + FE |
| `docs/shared/02-security-and-config.md` | JWT auth (Identity + refresh rotation), secrets, CORS, rate limits, logging redaction | BE + FE |
| `docs/shared/03-testing-strategy.md` | Test pyramid, what to test per layer, fixtures policy | BE + FE |
| `docs/shared/04-deployment-and-demo.md` | Docker, free hosting, judge demo script, final report template | BE + FE |
| `docs/shared/05-user-stories-and-flows.md` (+ `flows/*.png`) | 33 user stories with acceptance criteria; journey, pipeline, auth, state and offline flow charts | Everyone |
| `docs/backend/BE-01-architecture.md` | Clean Architecture solution layout, dependency rules, ADRs | BE |
| `docs/backend/BE-02-domain-and-data-model.md` | Entities, state machine, PostgreSQL schema, indexes | BE |
| `docs/backend/BE-03-qloo-integration.md` | Qloo contract (known + to-verify), probe procedure, client spec, research planner, evidence-quality rules | BE |
| `docs/backend/BE-04-ai-orchestration.md` | 8-step generation pipeline, LLM abstraction (Gemini + mock), prompts, validation/repair, evaluation mode | BE |
| `docs/backend/BE-05-build-plan.md` | Ordered slices with copy-paste Claude Code prompts and done-criteria | BE |
| `docs/frontend/FE-01-pwa-spec.md` | Routes, folders, state, JWT session handling, PWA/service-worker policy, offline, components | FE |
| `docs/frontend/FE-02-build-plan.md` | Ordered slices with Claude Code prompts and done-criteria | FE |
| `contracts/openapi.yaml` | **Source-of-truth** API contract (OpenAPI 3.1) | BE + FE |
| `contracts/growth-plan-llm-output.schema.json` | JSON Schema the LLM must satisfy (v1) | BE |
| `design/magic-patterns.md` | Design system + screen-by-screen prompts for Magic Patterns | You (design) → FE |

## 2. Decisions already made

| Topic | Decision | Why |
|---|---|---|
| Name | **Kulture Compass** (`KultureCompass.*` namespaces, short name *Kompass*) | "Navigate culture" metaphor; the K nods to Nigerian/Afro-pop spelling. Rename = find/replace `KultureCompass`/`Kulture Compass`. |
| Backend | .NET 10 LTS, ASP.NET Core minimal APIs, EF Core 10 + Npgsql, FluentValidation, Serilog, built-in OpenAPI + Scalar UI | Matches your Clean Architecture habits; no MediatR |
| Frontend | React + Vite + TS strict, React Router, TanStack Query, React Hook Form + Zod, Tailwind, `vite-plugin-pwa` | As per analysis (PWA variant) |
| Auth | **Self-hosted JWT**: ASP.NET Core Identity (users, password hashing, lockout) + 15-min JWT access tokens (memory only) + rotating HttpOnly refresh-token cookie; `sub` = owner id | Your choice; no third-party identity dependency. Identity handles the crypto-sensitive parts so nothing is home-grown |
| LLM | **Google Gemini (free tier via Google AI Studio)** behind `ILlmClient`; deterministic `MockLlmClient` always available; optional OpenAI-compatible adapter (Groq/OpenRouter) as fallback | Only major provider with a usable free API tier + native JSON-schema output. See caveats below. |
| Async generation | DB-persisted status + in-process background worker (`Channel<Guid>`) + polling | No message broker needed |
| Qloo | `https://hackathon.api.qloo.com`, header `X-Api-Key`, typed `HttpClient` + standard resilience, ≤ 5 req/s | From your message + community-observed behaviour; **verified in Slice BE-0** |

### LLM choice — the honest trade-off
* **Gemini free tier is free but small and not private.** As of late 2026 the free tier is Flash/Flash-Lite only;
  community-reported quotas are roughly ~20 requests/day for Flash and ~500/day for Flash-Lite (check your own
  limits in Google AI Studio). Free-tier prompts may be used by Google to improve products, so **send no
  personal data** (we don't — only business briefs). Google's terms also restrict free-tier use for apps serving
  users in the EEA/UK/Switzerland — fine for a Nigeria-focused hackathon demo; move to the paid tier before any
  real launch.
* Each plan costs **2–4 LLM calls** → develop on **mock mode**, use **Flash-Lite** for routine live tests, and
  switch `LLM_MODEL` to Flash for the recorded demo/judging.
* If you hit quota, set `LLM_PROVIDER=OpenAiCompatible` and point it at Groq or OpenRouter free models.

## 3. How to use this pack with Claude Code

1. `git init kulture-compass && cd kulture-compass` and copy this whole folder in (keep the paths).
2. Add your Qloo key **only** to backend user-secrets (never to a file in Git):
   ```bash
   cd backend/src/KultureCompass.Api   # after scaffolding in slice BE-1
   dotnet user-secrets set "Qloo:ApiKey" "<your hackathon key>"
   dotnet user-secrets set "Llm:ApiKey" "<your Gemini key>"
   dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
   ```
   Before scaffolding, export them in your shell for the probe script (slice BE-0).
3. Open VS Code → Claude Code and run slices **in order**. Each slice in the build plans has a ready prompt:
   * Backend: `docs/backend/BE-05-build-plan.md` (BE-0 … BE-10)
   * Frontend: `docs/frontend/FE-02-build-plan.md` (FE-0 … FE-9)
   * Suggested interleave: BE-0 → BE-1 → BE-2 → FE-0 → BE-3 → FE-1 → BE-4 … (see §4).
4. After each slice: build, run tests, commit. Claude Code is instructed to stop and report if a slice's
   done-criteria fail.
5. Design track (parallel): use `design/magic-patterns.md` in Magic Patterns, export React+Tailwind, drop
   exports into `frontend/design-exports/`, then run slice FE-3 to adapt them.

## 4. Recommended overall order

```
BE-0 Qloo probe & contract notes ──► BE-1 Solution scaffold ──► BE-2 Domain + persistence
        │                                                            │
        ▼                                                            ▼
FE-0 PWA scaffold ──► FE-1 Shell/auth/API client      BE-3 Qloo client ──► BE-4 Cultural intelligence
                                                                     │
                       FE-2 Profiles ◄── BE-5 Profiles API           ▼
                       FE-3 Design adoption               BE-6 LLM + pipeline ──► BE-7 Plans API + worker
                       FE-4 Generate + progress ◄──────────────────────────────────┘
                       FE-5 Plan dashboard/evidence     BE-8 Experiments + evaluation
                       FE-6 Saved plans/offline         BE-9 Hardening (rate limit, headers, health)
                       FE-7 PWA install/update          BE-10 Docker, seed demo, README
                       FE-8 Evaluation view
                       FE-9 Hardening + a11y + e2e
```

## 5. Things you still need to do yourself
- Generate a JWT signing key (`openssl rand -base64 48`) and store it with `dotnet user-secrets set "Jwt:SigningKey" "…"`.
- Plan to serve the API and PWA from the **same site** (e.g. `/api` proxied by the frontend host) so the refresh cookie works — see 02-security §1.
- Create a Gemini API key at Google AI Studio.
- **Rotate your Qloo hackathon key if it has been pasted anywhere public** (including chat logs you share).
- Pick hosting (see `docs/shared/04-deployment-and-demo.md`).
- Read Qloo's hackathon terms on caching/publishing response data — the pack defaults to *no committed real
  Qloo responses* and a short in-memory cache only.
