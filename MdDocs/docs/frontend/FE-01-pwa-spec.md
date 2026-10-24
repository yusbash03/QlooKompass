# FE-01 — Frontend PWA Specification

## 1. Stack
React 19 + Vite + TypeScript (`strict`, `noUncheckedIndexedAccess`), React Router (data router), TanStack Query,
React Hook Form + Zod, Tailwind CSS (single styling system) + Radix UI primitives (dialog, tabs, tooltip, dropdown) for
accessibility, lucide-react icons, `vite-plugin-pwa` (Workbox `generateSW`), `idb-keyval` (or `idb`) for opt-in offline copies,
`openapi-typescript` + `openapi-fetch` for typed API calls generated from `contracts/openapi.yaml`, our own `AuthProvider` (JWT in memory + refresh cookie),
Vitest + RTL + MSW, Playwright (+ axe). No other state library unless justified (TanStack Query + URL + local component state suffice).

## 2. Folder structure (feature-based)
```
frontend/
  public/ icons/ (192, 512, maskable-512, apple-touch-180), favicon.svg, robots.txt
  src/
    app/          main.tsx, App.tsx, router.tsx, providers.tsx (AuthProvider, QueryClient, Theme), ErrorBoundary.tsx
    shared/
      api/        schema.d.ts (generated), client.ts (openapi-fetch + auth middleware), problem.ts (ProblemDetails → AppError), queryKeys.ts
      ui/         Button, Card, Badge, Tabs, Drawer, Dialog, Field, Select, Textarea, Skeleton, EmptyState, ErrorState, Banner, Tooltip, Stepper, Toast
      lib/        format.ts (dates, NGN), idempotency.ts, useOnlineStatus.ts, usePolling.ts, cn.ts
      layout/     PublicLayout, AppLayout (sidebar desktop / bottom nav mobile), OfflineBanner, MockModeBanner
    features/
      landing/    LandingPage, sections/*
      auth/       AuthProvider.tsx, tokenStore.ts (memory only), authApi.ts, SignInPage, SignUpPage, RequireAuth,
                  useSignOutCleanup, authBroadcast.ts (BroadcastChannel), signInSchema.ts / signUpSchema.ts
      profiles/   ProfileFormPage, ProfileForm, profileSchema.ts, ReferenceEntitiesField, api.ts
      plans/      GeneratePage, ProgressPage, PlanPage (+ tabs), SavedPlansPage, components/*, api.ts
      evidence/   EvidenceChip, EvidenceDrawer, EvidenceTable, QualityPill, ProvenanceLabel, AffinityBar
      experiments/ ExperimentCard, ExperimentUpdateDialog (start / record result)
      evaluation/ EvaluationPage, MetricsTable, SideBySide
      offline/    offlineStore.ts (IndexedDB per user), MakeAvailableOfflineToggle, OfflinePage
      pwa/        InstallPrompt, useInstallPrompt, UpdatePrompt (registerSW onNeedRefresh), IosInstallInstructions
    styles/       index.css (Tailwind layers + CSS variables from design tokens)
    test/         setup.ts, msw/handlers/*, factories/*
  design-exports/ (raw Magic Patterns exports — reference only, not imported directly)
```

## 3. Routes
| Path | Auth | Screen | Notes |
|---|---|---|---|
| `/` | public | Landing | CTA: "Try the sample plan" / "Get started" |
| `/sample` | public | Sample plan (PlanPage in sample mode) | from `/sample-plan`; SW-cached |
| `/sign-in`, `/sign-up` | public | Our own forms (RHF + Zod) | redirect param honoured; already signed in → `/app/plans` |
| `/app` | user | redirect → `/app/plans` | |
| `/app/plans` | user | Saved plans | empty state → create profile |
| `/app/profiles/new` | user | Business brief form (multi-step) | |
| `/app/profiles/:profileId/edit` | user | Edit brief | ETag concurrency |
| `/app/profiles/:profileId/generate` | user | Review brief + Generate | shows quota; disabled offline |
| `/app/plans/:planId/progress` | user | Research progress | polling |
| `/app/plans/:planId` | user | Plan dashboard; tabs via `?tab=overview|insights|partnerships|campaigns|experiments|evidence` (UI label "Partnerships" = API `collaborations`) | |
| `/app/experiments` | user | Experiments hub (Proposed · Running · Concluded across plans) | launch → measure loop |
| `/app/evaluate` | user + flag | Evaluation | hidden unless `meta.features.evaluationMode && VITE_ENABLE_EVALUATION` |
| `/offline` | public | Offline fallback | |
| `*` | public | 404 | |

Route-level code splitting (`lazy`) for app, evaluation, sample.

## 4. Data layer
- `client.ts`: `createClient<paths>({ baseUrl: VITE_API_BASE_URL || '' })` (relative `/api` = same-origin) + middleware: attach
  `Bearer` from `tokenStore`; proactive refresh < 60 s to expiry; on 401 single-flight `POST /auth/refresh` then retry once;
  `/auth/*` calls use `credentials: 'include'` + `X-Requested-With: kc-web`; parse `application/problem+json` into `AppError { status, code, title, detail, fieldErrors, traceId }`.
- Query keys: `['meta']`, `['quota']`, `['profiles', {page}]`, `['profile', id]`, `['plans', filters]`, `['plan', id]`,
  `['planStatus', id]`, `['evaluation', id]`, `['samplePlan']`. **All user-scoped keys are prefixed with `userId`**
  (`['u', userId, …]`) so a different user never sees cached data.
- Defaults: `staleTime` 30 s; `retry` only for network errors/5xx on **GET** (max 2); **mutations never auto-retry**.
- Polling: `usePlanStatus(id)` → `refetchInterval` 2000 ms for first 30 s, then 5000 ms; stops on terminal status; pauses when
  offline or tab hidden; on terminal → invalidate `['plan', id]` and navigate to plan page.
- Generation mutation: `idempotency.ts` creates a UUID **when the user presses Generate** and keeps it in component state +
  `sessionStorage` (keyed by profileId) until a 2xx/4xx response; network failure → "Retry" reuses the same key.
  Never retried automatically.
- ETag handling: profile GET stores `etag` in query data; PUT sends `If-Match`; 412 → dialog "This brief changed elsewhere — reload?"

## 5. Forms (React Hook Form + Zod)
`profileSchema.ts` mirrors `BusinessProfileRequest` limits exactly. Multi-step form:
1. **Brand** — brandName, category (+other), description, brandIdentity
2. **Products & market** — productsDescription, targetMarket, primaryLocation (suggestions: Lagos, Abuja, Port Harcourt, Ibadan, Kano, Enugu — free text allowed)
3. **Goals** — marketingObjective (cards), objectiveNotes, budgetRange (NGN bands)
4. **Cultural references** (optional) — up to 8 `{name, kindHint}` chips; helper: "Brands, artists, places or shows your customers love. We'll look them up in Qloo."
5. **Review** — summary + "Save brief" / "Save & generate"
Only step 1–3 required fields block; partial briefs allowed. Draft autosave in memory only (no localStorage of brief content).
Server `fieldErrors` mapped back onto fields.

## 6. Plan presentation rules (critical)
- Two visual provenance classes everywhere:
  - **Retrieved from Qloo** (teal, database icon): evidence items, names, affinity/popularity values, tags.
  - **AI-generated** (violet, sparkle icon): findings' interpretation (`whyItMatters`), angles, executions, hypotheses.
  - Insight card layout: *Finding* (evidence-backed) block above *Why it matters* (AI) block, each labelled.
- `EvidenceChip` (`ev_004 · Artist name`) → opens `EvidenceDrawer` with all fields, quality pill + reasons, source signals,
  retrieved time. Unknown key (shouldn't happen) renders a red "missing evidence" chip and logs to console.
- `QualityPill` tooltip: "Heuristic based on how this was retrieved — not a probability."
- `AffinityBar` only renders when `affinity != null`; shows the numeric value; no invented scales.
- `unlinkedMentions` shown in a separate "AI suggestions not found in Qloo" box.
- Experiments show "Proposed default" badge when `thresholdIsProposedDefault`.
- `limitations` panel always visible on Overview when non-empty; `CompletedWithLimitations` shows an amber banner.
- `isMock` → persistent "MOCK DATA — not real Qloo intelligence" banner; `isSample` → "Sample plan (synthetic)".
- Charts: only the Evidence tab may show a bar chart of returned affinity values (top 10) and counts by quality/type — real data only.

## 7. PWA
### Manifest (`vite-plugin-pwa` `manifest`)
```ts
{ name: 'Kulture Compass', short_name: 'Kompass', description: 'Cultural growth plans for brands, grounded in Qloo evidence.',
  start_url: '/app/plans?source=pwa', scope: '/', display: 'standalone', orientation: 'portrait-primary',
  theme_color: '#14122B', background_color: '#FAF7F2', categories: ['business','productivity'],
  icons: [ {src:'/icons/icon-192.png',sizes:'192x192',type:'image/png'}, {src:'/icons/icon-512.png',sizes:'512x512',type:'image/png'},
           {src:'/icons/maskable-512.png',sizes:'512x512',type:'image/png',purpose:'maskable'} ],
  shortcuts: [ {name:'New brief', url:'/app/profiles/new'}, {name:'Saved plans', url:'/app/plans'} ] }
```
### Service worker policy (`workbox` options)
- `registerType: 'prompt'` → `UpdatePrompt` toast "New version available — Reload".
- Precache: app shell (JS/CSS/HTML/fonts/icons) via `globPatterns`.
- `navigateFallback: '/index.html'`, `navigateFallbackDenylist: [/^\/api\//]`.
- `runtimeCaching`:
  - `GET /api/v1/sample-plan` and `/api/v1/meta` → `StaleWhileRevalidate` (public, non-user data), max 2 entries, 7 days.
  - Google Fonts (if used) → `CacheFirst`, 1 year.
  - **Everything else under `/api/` → `NetworkOnly`.** Authenticated responses are never put in the SW cache.
- Offline navigation to app routes → shell loads; data views show `OfflineState` unless an offline copy exists.

### Offline copies of saved plans (deliberate, opt-in)
- Toggle "Make available offline" on a completed plan → stores the `GrowthPlanDto` in IndexedDB store `kc-offline-<userId>`.
- Saved Plans page offline: lists offline copies only, labelled "Offline copy · saved <date>".
- Sign-out / user change: delete `kc-offline-<previousUserId>`, `queryClient.clear()`, clear sessionStorage idempotency keys.
- Settings menu: "Remove offline copies" button. Copy never includes tokens.

### Online/offline UX
- `useOnlineStatus` (navigator.onLine + failed-fetch heuristic). Offline banner: "You're offline. Saved offline plans are available;
  generating a new plan needs a connection to our research service."
- Generate & Save buttons disabled offline with tooltip. Never queue generation for background sync.
- API timeout (20 s for GETs) → `ErrorState` with Retry; generation POST timeout → "We couldn't confirm your request. Retry safely" (same key).

### Install prompt
- Capture `beforeinstallprompt`; show `InstallPrompt` card only after the user has viewed a plan (sample or real) and not dismissed in
  the last 14 days (dismissal timestamp in localStorage — non-sensitive).
- iOS Safari (no event): show `IosInstallInstructions` sheet from the account menu ("Share → Add to Home Screen").
- Hide when `display-mode: standalone`.

## 8. Accessibility & responsiveness
- Semantic landmarks; one `h1` per page; skip link; visible focus ring (2px offset, token `--focus`); colour contrast ≥ 4.5:1
  (provenance colours tested on both surfaces); never colour-only meaning (icons + text labels).
- Keyboard: tabs (Radix), drawers trap focus and return it; stepper progress uses `aria-live="polite"`.
- Breakpoints: mobile ≤ 640 (bottom nav, stacked cards), tablet 641–1024 (collapsible sidebar), desktop > 1024 (sidebar + 2-column plan).
- Respect `prefers-reduced-motion`; dark mode via `prefers-color-scheme` + manual toggle.

## 9. Environment
`VITE_API_BASE_URL` (normally empty → same-origin), `VITE_APP_NAME`, `VITE_ENABLE_EVALUATION`; dev-only `VITE_DEV_API_PROXY_TARGET` for the Vite `/api` proxy. Nothing secret. A build-time check
fails the build if any `VITE_*` var name contains `SECRET`, `API_KEY`, `TOKEN`, `JWT`, `QLOO`, or `LLM`.
