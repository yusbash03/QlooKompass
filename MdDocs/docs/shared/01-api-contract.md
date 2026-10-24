# 01 — API Contract (human-readable)

Machine-readable source of truth: **`contracts/openapi.yaml`**. If this doc and the YAML disagree, the YAML
wins — fix the doc. The backend's generated OpenAPI document (`/openapi/v1.json`) must be diff-checked against
it in CI (slice BE-9).

## 1. Conventions
- Base path `/api/v1`. JSON, camelCase, enums as strings, timestamps ISO-8601 UTC (`2026-10-24T10:15:00Z`).
- Auth: `Authorization: Bearer <access JWT>` on every endpoint except those marked **public**/**auth**. Access tokens come from
  `/auth/login`, `/auth/register` or `/auth/refresh`; the refresh token is an HttpOnly cookie scoped to `/api/v1/auth` (see 02-security §1).
- Ownership: every profile/plan/evaluation belongs to `ownerId = JWT sub`. Accessing another owner's resource
  returns **404** (not 403) to avoid leaking existence.
- Errors: RFC 9457 **Problem Details** (`application/problem+json`) with extension members `code` and
  `traceId`; validation errors add `errors: { field: [messages] }`.
- Pagination: `?page=1&pageSize=20` (max 50) → `{ items, page, pageSize, totalCount }`.
- Concurrency: resources with `version` return `ETag: "<version>"`; updates require `If-Match`. Mismatch → 412.
- Cancellation: all handlers accept the request `CancellationToken`.

## 2. Endpoint summary
| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/health` | public | Liveness (no dependencies) |
| GET | `/health/ready` | public | Readiness: DB reachable; Qloo/LLM *config present* (no secrets, no live calls) |
| GET | `/meta` | public | App name, version, `qlooMode`, `llmProvider`, `isMockMode`, feature flags |
| GET | `/sample-plan` | public | Synthetic, clearly-labelled sample plan (flag `Features:SamplePlan`) |
| POST | `/auth/register` | auth | Create account → `AuthResponse` + refresh cookie |
| POST | `/auth/login` | auth | Sign in → `AuthResponse` + refresh cookie |
| POST | `/auth/refresh` | auth (cookie + `X-Requested-With: kc-web`) | Rotate refresh token → new `AuthResponse` |
| POST | `/auth/logout` | user | Revoke refresh family, clear cookie → 204 |
| GET | `/auth/me` | user | Current user |
| GET | `/me/quota` | user | Generations used/remaining today |
| POST | `/business-profiles` | user | Create profile |
| GET | `/business-profiles` | user | List own profiles (paged) |
| GET | `/business-profiles/{id}` | user | Get profile (+ ETag) |
| PUT | `/business-profiles/{id}` | user | Replace profile (`If-Match` required) |
| POST | `/growth-plans` | user | Start generation (`Idempotency-Key` required) → **202** |
| GET | `/growth-plans` | user | List own plans (paged, filter `businessProfileId`, `status`) |
| GET | `/growth-plans/{id}` | user | Full plan (content `null` until terminal state) |
| GET | `/growth-plans/{id}/status` | user | Lightweight polling payload |
| PATCH | `/growth-plans/{id}/experiments/{experimentId}` | user | Record experiment status/result — Launch → Measure (`If-Match`) |
| GET | `/experiments` | user | Experiments hub across all plans (paged, filter `status`) |
| POST | `/evaluations` | user + flag | Start LLM-only vs grounded comparison → **202** |
| GET | `/evaluations/{id}` | user + flag | Evaluation result |

## 3. Key flows

### 3.1 Generate a plan
```
POST /api/v1/growth-plans
Idempotency-Key: 3f6c1d0e-7b0a-4c51-9a0b-2b7f8e0c9a11
{ "businessProfileId": "8a1e…", "options": { "includeCollaborations": true, "includeCampaigns": true, "maxCampaigns": 3 } }

202 Accepted
Location: /api/v1/growth-plans/5b2c…
{ "id": "5b2c…", "status": "Queued", "stage": "Queued", "statusUrl": "/api/v1/growth-plans/5b2c…/status", "createdAt": "…" }
```
Idempotency rules:
- Key is a client-generated UUID, **one per user intent** (the FE stores it with the submit attempt and reuses
  it on network retry).
- Same owner + same key + same body hash → returns the **original** plan (202 if still running, 200 if terminal).
- Same key + different body → **409** `idempotency_key_reused`.
- Keys are unique per owner forever (stored on `growth_plans`).
- Daily quota exceeded → **429** `generation_quota_exceeded` with `Retry-After` (seconds to UTC midnight).
- Global rate limit hit → **429** `rate_limited`.

### 3.2 Poll status
```
GET /api/v1/growth-plans/5b2c…/status
200 { "id": "5b2c…", "status": "Running", "stage": "RetrievingEvidence",
      "stages": [ {"stage":"InterpretingBrief","state":"Done","completedAt":"…"},
                  {"stage":"PlanningResearch","state":"Done","completedAt":"…"},
                  {"stage":"RetrievingEvidence","state":"Active","detail":"4 of 9 Qloo requests complete"},
                  {"stage":"EvaluatingEvidence","state":"Pending"}, … ],
      "updatedAt": "…", "error": null }
```
`detail` strings are produced by the backend from **real** counters; the FE never invents progress.
Polling: every 2 s for 30 s, then every 5 s; stop on terminal status (`Completed`, `CompletedWithLimitations`, `Failed`).

### 3.3 Plan payload (abridged — see `GrowthPlanDto` in YAML)
```jsonc
{
  "id": "5b2c…", "businessProfileId": "8a1e…", "status": "CompletedWithLimitations",
  "isMock": false, "qlooMode": "Live", "llmProvider": "Gemini", "llmModel": "gemini-flash-latest",
  "schemaVersion": "1.0", "promptVersion": "synth-v1",
  "briefSnapshot": { … }, 
  "summary": { "headline": "…", "overview": "…" },
  "evidence": [ { "key": "ev_003", "qlooEntityId": "…", "name": "…", "entityType": "urn:entity:artist",
                  "sourceOperation": "insights", "affinity": 0.87, "popularity": 0.91, "tags": ["…"],
                  "quality": "Strong", "qualityReasons": ["resolved seed signal","affinity returned"],
                  "retrievedAt": "…" } ],
  "insights":       [ { "id": "ins_1", "title": "…", "finding": "…", "whyItMatters": "…", "evidenceKeys": ["ev_003"],
                        "evidenceStrength": "Strong", "assumptions": ["…"], "limitations": [] } ],
  "collaborations": [ { "id": "col_1", "partnerEvidenceKey": "ev_003", "partnerName": "…", "culturalConnection": "…",
                        "format": "…", "fitRationale": "…", "testIdea": "…", "evidenceKeys": ["ev_003","ev_007"], … } ],
  "campaigns":      [ { "id": "cam_1", "objective": "…", "targetHypothesis": "…", "culturalInsight": "…", "angle": "…",
                        "execution": ["…"], "successMetric": "…", "effortCategory": "Low", "evidenceKeys": [...], … } ],
  "experiments":    [ { "id": "exp_1", "linkedRecommendationId": "cam_1", "hypothesis": "…", "segment": "…",
                        "proposedCampaign": "…", "primaryMetric": "…", "measurementPeriodDays": 14,
                        "successThreshold": "…", "thresholdIsProposedDefault": true,
                        "nextDecision": { "ifSuccess": "…", "ifFailure": "…" }, "status": "Proposed", "version": "…" } ],
  "unlinkedMentions": [ { "name": "…", "note": "AI suggestion — not retrieved from Qloo" } ],
  "limitations": [ "Qloo returned no place results for Lagos with these signals." ],
  "createdAt": "…", "completedAt": "…"
}
```

## 4. Status & stage enums
`GenerationStatus`: `Queued` → `Running` → `Completed` | `CompletedWithLimitations` | `Failed`
`GenerationStage`: `Queued`, `InterpretingBrief`, `PlanningResearch`, `RetrievingEvidence`, `EvaluatingEvidence`,
`GeneratingRecommendations`, `ValidatingOutput`, `DesigningExperiments`, `Saving`, `Done`

## 5. Error codes (`code` member)
| HTTP | code | When |
|---|---|---|
| 400 | `validation_failed` | Body/query invalid (with `errors`) |
| 400 | `idempotency_key_missing` | POST /growth-plans without header |
| 401 | `unauthenticated` | Missing/invalid/expired access token |
| 401 | `invalid_credentials` | Login failed (same response for unknown email and wrong password) |
| 401 | `refresh_invalid` / `token_reused` | Refresh cookie missing/expired/revoked; reuse revokes the whole token family |
| 409 | `email_taken` | Register with an existing email |
| 423 | `account_locked` | Too many failed logins (Identity lockout) |
| 404 | `not_found` | Missing or not owned |
| 409 | `idempotency_key_reused` | Same key, different body |
| 409 | `plan_in_progress` | A plan for this profile is already Queued/Running (one at a time per profile) |
| 412 | `version_mismatch` | `If-Match` stale |
| 428 | `precondition_required` | `If-Match` missing on PUT/PATCH |
| 413 | `payload_too_large` | > 64 KB body |
| 429 | `rate_limited` / `generation_quota_exceeded` | With `Retry-After` |
| 503 | `feature_disabled` | Flagged feature off |
| 500 | `internal_error` | Unexpected; message is generic, `traceId` included |

Plan-level failures are **not** HTTP errors: the plan becomes `Failed` with `error: { code, message }` where code ∈
`qloo_unavailable`, `qloo_auth_failed`, `qloo_no_evidence`, `llm_unavailable`, `llm_quota_exceeded`,
`llm_output_invalid`, `timeout`, `internal_error`. Messages are user-safe.
