# Known limitations

Backend slices BE-0 to BE-10. Each item says what is missing and what happens instead.

## Project scope
- **No frontend yet.** The PWA (FE slices) hasn't been built, so `docker compose up` serves the API only. The
  `web` service is behind the `web` profile until `frontend/Dockerfile` exists. The judge demo runs as a
  terminal script (`scripts/demo.sh`), and the PWA-specific demo steps (install, offline, evidence drawer,
  colour coding) can't be shown.
- **Not deployed publicly.** The local Docker stack is verified (mock and live); there's no public URL.

## Testing
- **Unreproduced test-infrastructure flake.** Twice, a `dotnet test` on the whole solution *that also compiled*
  failed every `KultureCompass.Api.Tests` test within ~4 s (the test fixture failing to start). Every rerun
  passed: more than 8 full runs, including 4 with forced rebuilds. The error text wasn't captured, so the cause
  is unknown; Testcontainers/Docker startup contention is the leading guess. CI builds first and then runs
  `dotnet test --no-build`, the path that has never failed. If it recurs, the fixture's exception message is in
  the test log.

## Auth (BE-5)
- **Not in the MVP:** email confirmation, password reset by email, MFA, social login (02-security §1).
- **No endpoint to change a password yet.** Changing the security stamp does invalidate access tokens (tested),
  but nothing in the API triggers that today.
- **Security-stamp check is cached for 5 minutes per user**, so a revoked access token can still work for up to
  5 minutes. Access tokens expire after 15 minutes anyway.
- **No cleanup job for refresh tokens yet** (02-security §1 "daily background job"). Expired or revoked rows
  pile up in `refresh_tokens`. They're never accepted, so this costs storage only.
- **Rate limiting:** all 02-security §5 policies exist (`global`, `generation`, `public`, `auth`, `auth-refresh`).
  Behind a reverse proxy, set `App:TrustForwardedHeaders=true` so per-IP limits see the real client IP. Otherwise
  all anonymous proxied traffic shares the proxy's IP bucket. Signed-in traffic is partitioned per user.
- **HTTPS redirect needs a known port** (`ASPNETCORE_HTTPS_PORT`). Without one, ASP.NET logs a warning and doesn't
  redirect. Set `App:EnforceHttps=false` when TLS terminates at a proxy that already redirects.
- **The 429 rate-limit response is unverified by tests.** It's configured, but the tests raise the limits so
  they don't trip it.

## LLM / generation (BE-6)
- **Gemini can't take the full plan schema.** It rejects our complete v1.0 schema as too complex, so after one
  failed call it retries with the array size limits removed and remembers that. Gemini then isn't held to item
  counts; `OutputValidator` enforces them, and repair/drop applies if they're wrong. See
  `docs/backend/llm-verified-contract.md`.
- **The OpenAI-compatible provider is unverified**: no live key was available, so only fake-handler tests cover it.
- **Gemini 429 handling hasn't been seen against the real API.** Daily-quota detection works by spotting
  "PerDay" in the error body; it's tested only with synthetic bodies.
- **Restated limitations can still repeat.** When the model repeats a research limitation word for word (ignoring
  punctuation and numbers), the duplicate is dropped. A rewording that adds detail is kept, so near-duplicates can
  still appear.
- **Retrieval progress says "complete" for skipped requests.** When a request is skipped because the call budget
  ran out (e.g. 13 planned, 12-call budget), the detail still reads "13 of 13 Qloo requests complete".
- **Docker builds need `contracts/`.** The Application project embeds `contracts/growth-plan-llm-output.schema.json`,
  so the Docker build context must include `contracts/` (BE-10).

## Growth plans / worker (BE-7)
- **The queue is in-process (a bounded channel) and assumes a single API instance.** A second instance would not
  see the first one's queue, and its startup recovery would fail the first instance's running plans. Scaling out
  would need a DB-backed claim, which the architecture deliberately leaves out (no broker).
- **A restart fails Running plans immediately** with `timeout` ("interrupted by a server restart"), rather than
  only those older than the timeout (BE-01 §4 has the note). Queued plans are re-enqueued.
- **Failed plans count toward the daily quota**: `usedToday` counts every plan created today.

## Evaluation mode, sample plan, experiments (BE-8)
- **An evaluation uses one plan from the daily quota** (its grounded run is a normal plan) plus one extra LLM call
  for the LLM-only baseline. Quota and "one active plan per profile" both apply.
- **Rare race: the LLM-only call can run twice for one evaluation.** If the worker finishes the grounded plan at
  the same moment the evaluation row is saved, both may complete it. The last write wins and both results are valid.
- **Entity metrics are not NER.** They count declared names plus exact matches of retrieved evidence names. This
  caveat ships in every evaluation's `notes`.
- **The LLM-only output isn't run through `OutputValidator`** (it has no evidence to validate against). It is parsed
  and stored as-is for side-by-side review.
- **`Features:ExperimentTracking` now defaults to `true`** in appsettings.json, since the PATCH endpoint exists.

## Mock mode
- **Mock search returns the user's own query as its top hit** (with a synthetic id), so seed resolution works for
  any brief. The fixtures behind it are always "Synthetic …"; mock-mode evidence never contains a name the user
  didn't type.

## Qloo
- See `docs/backend/qloo-verified-contract.md` §3 and §7. Demographics (R8) stays off; error bodies aren't parsed.
