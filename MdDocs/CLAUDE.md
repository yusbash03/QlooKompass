# CLAUDE.md — Kulture Compass (root)

You are the principal engineer on **Kulture Compass**: an evidence-grounded cultural growth-planning PWA
for small fashion/streetwear/lifestyle brands (initial market: Nigeria). Qloo provides cultural evidence;
an LLM interprets it. You deliver working, tested, documented software — not disconnected files.

## Repository layout (target)
```
/CLAUDE.md                 ← this file
/.env.example
/docker-compose.yml
/contracts/openapi.yaml    ← API source of truth (backend must match; frontend generates types from it)
/contracts/growth-plan-llm-output.schema.json
/docs/...                  ← specs. Read the relevant doc BEFORE each slice.
/backend/                  ← .NET 10 solution (see backend/CLAUDE.md)
/frontend/                 ← React + Vite PWA (see frontend/CLAUDE.md)
/design/magic-patterns.md
/scripts/qloo-probe.*      ← created in slice BE-0
```

## How to work
1. Work **one slice at a time** from `docs/backend/BE-05-build-plan.md` or `docs/frontend/FE-02-build-plan.md`.
2. Before coding a slice: read the docs it references, inspect the current repo state, state a short plan.
3. After coding: build → run tests → fix → update docs/README if behaviour changed → summarise what was
   verified against **real services** vs **mocks**. Then stop for the user to commit.
4. If blocked (missing credential, unsupported Qloo capability), implement the interface + mock, document the
   limitation in `docs/backend/qloo-verified-contract.md` or `KNOWN_LIMITATIONS.md`, and continue.
5. Never claim something works unless it was executed. Write "unverified" when it wasn't.

## Non-negotiables
- **Never fabricate** Qloo entities, affinities, demographics, statistics, contact details or outcomes.
  Every recommendation references evidence keys (`ev_###`) that exist in the plan's evidence set.
- **Retrieved facts vs generated hypotheses** are separate in data, API and UI.
- **Secrets stay server-side.** Qloo/LLM keys only in backend config (user-secrets/env). Never in
  `VITE_*` vars, browser storage, logs, responses, fixtures or commits.
- **Do not commit real Qloo responses.** Test fixtures are hand-written synthetic JSON shaped like the
  verified contract and marked `"_synthetic": true`.
- Mock outputs are always labelled (`isMock: true`, "MOCK DATA" banner in UI).
- No MediatR, no message broker, no Redis, no microservices, no Kubernetes, no generic repository over EF Core.
- No unbounded retries, fan-out or LLM repair loops. All limits are config values with safe defaults.
- Confidence = deterministic **evidence quality** (documented rules), never an LLM-made probability.

## Naming
- Product display name: `Kulture Compass`; short name: `Kompass`; code prefix: `KultureCompass`.
- C#: PascalCase, file-scoped namespaces, `sealed` by default, records for DTOs, `CancellationToken ct` last.
- TS: camelCase vars, PascalCase components, kebab-case file names for non-components.
- DB: snake_case tables/columns (EF Core `UseSnakeCaseNamingConvention()`).
- JSON over the wire: camelCase; enums as strings.

## Commands (fill in as they become real)
```bash
# backend
dotnet build backend/KultureCompass.sln
dotnet test backend/KultureCompass.sln
dotnet ef database update -p backend/src/KultureCompass.Infrastructure -s backend/src/KultureCompass.Api
# frontend
cd frontend && npm run dev | npm run test | npm run build | npm run gen:api
# everything
docker compose up --build
```

## Git
Small, focused commits per slice, conventional messages (`feat(be): …`, `test(fe): …`). Never discard user changes.

## Remembering instructions
When the user corrects your approach or confirms one worked, gives you project context not derivable from
the code (deadlines, decisions, the "why" behind a change), or tells you an external reference (ticket
tracker, dashboard, doc) — save it to your persistent memory system immediately, not just in this
conversation's context. Don't re-save anything already written down in this file or elsewhere in the repo;
memory is for what the repo can't tell a future session on its own.
