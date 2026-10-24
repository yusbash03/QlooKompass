# frontend/CLAUDE.md — Kulture Compass PWA

Read root `/CLAUDE.md` first. Specs: `docs/frontend/FE-01-pwa-spec.md`, `FE-02-build-plan.md`, `design/magic-patterns.md`,
`contracts/openapi.yaml` (regenerate types with `npm run gen:api` whenever it changes — never hand-edit `schema.d.ts`).

## Rules
- TypeScript strict; no `any` (use `unknown` + Zod). Components are function components; feature-first folders.
- Server state = TanStack Query only. User-scoped query keys start with `['u', userId]`. Mutations never auto-retry.
- Generation requests always carry an `Idempotency-Key` created on user action and reused only on manual retry.
- Auth: access JWT lives in memory only (`tokenStore`); refresh token is an HttpOnly cookie we never touch from JS. All API calls are
  same-origin relative `/api/...`. Never put secrets in `VITE_*`. Never store tokens or brief content in localStorage/sessionStorage. IndexedDB only for opt-in offline plan copies, per user.
- Service worker: `/api/**` is NetworkOnly except `/api/v1/sample-plan` and `/api/v1/meta`.
- Provenance UI: "Retrieved from Qloo" (teal + database icon) vs "AI-generated" (violet + sparkle icon) — always with text labels.
- Never render invented numbers; charts only from returned evidence values. Progress UI only shows backend stages/details.
- Mock/sample data always bannered.
- Accessibility: semantic HTML, Radix primitives, labelled inputs, visible focus, aria-live for async status, contrast ≥ 4.5:1.
- Styling: Tailwind + CSS variables tokens only. Don't add CSS-in-JS or another UI kit. Magic Patterns exports are references, re-implemented.
- Tests: Vitest + RTL + MSW per feature; Playwright e2e + axe.

## Commands
```bash
npm run dev | npm run build | npm run preview
npm run typecheck | npm run lint | npm run test | npm run test:e2e
npm run gen:api
```
