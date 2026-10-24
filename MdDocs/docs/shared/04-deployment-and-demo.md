# 04 — Deployment & Demo

## 1. Local (Docker Compose)
`docker-compose.yml` (created in BE-10):
| Service | Image | Notes |
|---|---|---|
| `db` | `postgres:17-alpine` | volume `pgdata`, healthcheck `pg_isready` |
| `api` | built from `backend/Dockerfile` (multi-stage, `mcr.microsoft.com/dotnet/aspnet:10.0`, non-root) | port 8080, env from `.env`, depends_on db healthy, applies migrations via a **separate** `migrate` one-shot service or `--migrate` CLI flag (never `EnsureCreated`) |
| `web` | built from `frontend/Dockerfile` (Vite build → `nginx:alpine`) | port 5173→80, **proxies `/api/` → `api:8080`** (same-origin for the refresh cookie), SPA fallback, long-cache hashed assets, `no-cache` for `index.html`, `sw.js`, `manifest.webmanifest` |

Mock run with zero keys: `QLOO_MODE=Mock LLM_PROVIDER=Mock docker compose up --build`.

## 2. Free/cheap public hosting (pick one column)
| Layer | Option A (Azure, your usual) | Option B (free tiers) |
|---|---|---|
| Frontend (HTTPS, static) | Azure Static Web Apps (free) | Cloudflare Pages / Vercel / Netlify |
| API container | Azure Container Apps (consumption, scale-to-zero) | Render free web service (cold starts ~50 s — warm it before judging) / Fly.io |
| PostgreSQL | Azure Database for PostgreSQL Flexible (B1ms) | Neon free / Supabase free |
| Secrets | Container Apps secrets | Render/Fly env secrets |

Checklist: serve the API **same-origin** under `/api` (frontend host rewrite/proxy, or `api.` subdomain of the same domain) so the
refresh cookie works; set `App__BaseUrl`, `Jwt__Issuer`, `Jwt__Audience`, a fresh `Jwt__SigningKey`, `Jwt__CookieSecure=true`;
run migrations job; set `Features__EvaluationMode=false` publicly if quota is tight; verify `/api/v1/health/ready`.
**Do not claim deployment is complete until a fresh browser session completes the demo script on the public URL.**

## 3. Judge demo script (≈ 3 minutes)
1. **Landing** — problem in one sentence; "Qloo evidence, AI interpretation, you decide."
2. **Install** the PWA (desktop Chrome install icon / Android prompt / iOS "Add to Home Screen" instructions).
3. **Sample plan** (no sign-in): show *Retrieved from Qloo* vs *AI-generated* colour coding; open an evidence drawer.
4. **Sign in** with the seeded demo account (or register) → new profile: *"Eko Thread Co. — Lagos streetwear, Afrobeats/alté-inspired graphic tees, ₦250k–₦500k
   budget, objective: grow Instagram-driven sales among 18–30s"*, references: two Nigerian artists + one brand.
5. **Generate** → real stage progress (show "7 of 9 Qloo requests").
6. **Plan** → insight → collaboration (partner is an evidence-linked entity) → campaign → experiment with decision rule.
7. **Limitations panel** — show honest coverage gaps for Nigeria.
8. **Evaluation mode** — same brief, LLM-only vs grounded: traceable-claim ratio, unsupported entity mentions.
9. Toggle airplane mode → app shell + saved (offline-enabled) plan still readable; "Generate" disabled with explanation.

## 4. Final engineering report template (`ENGINEERING_REPORT.md`)
1. What was implemented
2. Final architecture & key decisions (link ADRs)
3. Projects/files created or changed
4. Qloo endpoints & request patterns **verified** (date, which key, result) vs unverified
5. LLM provider, model, config
6. DB schema & migrations list
7. Build & test results with exact commands and counts
8. Features verified against real services vs mocks (table)
9. Blockers & known limitations
10. Exact local run commands
11. Next steps before public launch (paid LLM tier, privacy policy, Qloo terms review, team accounts, …)
