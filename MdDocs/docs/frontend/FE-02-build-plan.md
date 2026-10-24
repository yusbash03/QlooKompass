# FE-02 — Frontend Build Plan (Claude Code slices)

Each slice: implement → `npm run typecheck && npm run lint && npm run test` → summary → stop for commit.

## FE-0 — Scaffold
**Prompt:**
> Implement FE-0 from docs/frontend/FE-02-build-plan.md: create `frontend/` with Vite React TS (strict + noUncheckedIndexedAccess),
> Tailwind, ESLint (typescript-eslint, react-hooks, jsx-a11y), Prettier, Vitest + RTL + MSW setup, Playwright config, path alias `@/`,
> folder structure per FE-01 §2, `npm run gen:api` using openapi-typescript on ../contracts/openapi.yaml → src/shared/api/schema.d.ts,
> the VITE_* secret-name guard from FE-01 §9, and design tokens as CSS variables from design/magic-patterns.md §2. Copy frontend/CLAUDE.md in.
**Done:** dev server runs; one smoke test passes; `gen:api` produces types.

## FE-1 — Shell, auth, API client
**Prompt:**
> Implement FE-1: Vite dev proxy `/api` → VITE_DEV_API_PROXY_TARGET; AuthProvider per docs/shared/02-security §1 Frontend (access token in
> memory only, silent refresh on start, single-flight refresh on 401, proactive refresh, BroadcastChannel sync); SignIn/SignUp pages with
> RHF + Zod (password rules mirror backend), error mapping (invalid_credentials, account_locked, email_taken); providers (AuthProvider,
> QueryClient with defaults from FE-01 §4, theme), router with all routes from FE-01 §3 (lazy
> placeholders), PublicLayout/AppLayout (sidebar desktop, bottom nav mobile), RequireAuth, sign-out cleanup hook, typed API client with
> auth middleware + 401 refresh-once + ProblemDetails parsing, user-scoped query keys, `useMeta` with MockModeBanner, OfflineBanner,
> ErrorBoundary, 404. Tests: RequireAuth redirect, sign-in/sign-up validation + errors, silent restore, concurrent 401s trigger ONE refresh, refresh failure
> signs out, token never written to any Web Storage, problem parsing, sign-out clears cache + IndexedDB.
**Done:** can register, sign in, reload (session restored via refresh cookie), sign out, and see empty `/app/plans`.

## FE-2 — Business brief
**Prompt:**
> Implement FE-2: multi-step ProfileForm per FE-01 §5 with Zod schema mirroring openapi limits, ReferenceEntitiesField, create & edit
> (ETag/If-Match, 412 dialog), server field-error mapping. Tests: validation, partial brief allowed, server errors mapped, 412 flow.
**Done:** profile create/edit works against BE-5.

## FE-3 — Design adoption
**Prompt:**
> Implement FE-3: review exports in frontend/design-exports/ (from Magic Patterns). Re-implement them as our shared/ui components and
> feature components using our tokens, Radix primitives and accessibility rules — do NOT import exports directly or add their deps.
> Build a /dev/ui route (dev only) showing all components in light/dark. Add vitest-axe checks for each component.
**Done:** component gallery passes axe; no new styling system introduced.

## FE-4 — Generate + progress
**Prompt:**
> Implement FE-4: GeneratePage (brief summary, quota from /me/quota, options, Generate disabled offline), idempotency key handling
> per FE-01 §4 (never auto-retry; manual retry reuses key), 409 plan_in_progress → link to running plan, 429 → quota message with reset
> time; ProgressPage polling /status with backoff, Stepper rendering ONLY backend stages/detail, terminal redirect, Failed state with
> user-safe message + "Edit brief"/"Try again" (new key). Tests for all of the above with MSW.
**Done:** end-to-end generation works in backend mock mode.

## FE-5 — Plan dashboard & evidence
**Prompt:**
> Implement FE-5: PlanPage with tabs (overview, insights, collaborations, campaigns, experiments, evidence) per FE-01 §6 presentation
> rules: provenance colours/labels, EvidenceChip/Drawer, QualityPill tooltip, AffinityBar only with real values, unlinked mentions box,
> limitations panel, CompletedWithLimitations banner, mock/sample banners, Evidence tab table (sort/filter by type & quality) + real-data
> chart. Sample route `/sample` reuses PlanPage. Tests: provenance separation, chip→drawer, missing key chip, banners, empty sections.
**Done:** sample plan and a mock plan render fully on mobile and desktop.

## FE-6 — Saved plans & offline copies
**Prompt:**
> Implement FE-6: SavedPlansPage (paged list, status chips, filter by profile, empty state), "Make available offline" toggle with
> IndexedDB store per userId, offline listing of copies, removal on sign-out/user change, settings "Remove offline copies". Tests incl.
> user switch never shows previous user's copies.
**Done:** plan readable offline after toggling; sign-out wipes it.

## FE-6b — Experiments (launch → measure)
**Prompt:**
> Implement FE-6b: ExperimentCard actions "Start experiment" and "Record result" (PATCH with If-Match; 412 → reload dialog),
> concluded card shows observed result next to threshold and highlights the matching decision rule, `/app/experiments` hub
> (grouped Proposed/Running/Concluded, days remaining for Running) per design §4.10. Tests for transitions and 412.
**Done:** an experiment can be started and concluded from both the plan and the hub.

## FE-7 — PWA
**Prompt:**
> Implement FE-7: vite-plugin-pwa with manifest and Workbox policy exactly per FE-01 §7 (NetworkOnly for /api except sample-plan/meta),
> UpdatePrompt, InstallPrompt (+ iOS instructions), OfflinePage, generated icons (create simple placeholder icons from the logo mark in
> design/magic-patterns.md; replace later). Unit-test the generated workbox config; Playwright: manifest valid, SW registers, offline
> navigation shows shell + offline state, sample plan available offline after first visit.
**Done:** Lighthouse PWA installability checks pass on `npm run build && npm run preview`.

## FE-8 — Evaluation view (flagged)
**Prompt:**
> Implement FE-8: EvaluationPage — choose profile → start (idempotency key) → poll → MetricsTable (grounded vs LLM-only, neutral
> styling, no "winner" badge) + side-by-side outputs, notes/caveats, reviewer notes textarea (local state only). Hidden unless flags on.
**Done:** works in mock mode; tests green.

## FE-9 — Hardening & e2e
**Prompt:**
> Implement FE-9: full a11y pass (axe in Playwright on landing, form, plan, saved), keyboard walkthrough, reduced motion, dark mode
> contrast check, error/empty/loading/partial states audit for every page, Playwright e2e from docs/shared/03-testing-strategy §4
> (register a fresh user per run via the API), frontend Dockerfile (nginx with cache headers from 04-deployment §1), GitHub Actions frontend.yml, README section.
**Done:** e2e green in mock mode; build served over HTTPS in deployment documented.
