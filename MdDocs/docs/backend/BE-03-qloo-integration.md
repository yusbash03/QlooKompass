# BE-03 — Qloo Integration (the most important component)

Official guide: https://docs.qloo.com/reference/qloo-llm-hackathon-developer-guide
(Read it in your browser before slice BE-0 — automated fetchers are blocked by the site's robots policy, so Claude
Code may not be able to read it. Paste relevant sections into `docs/backend/qloo-docs-notes.md` if needed.)

## 1. Contract status
Legend: ✅ provided by organisers · 🟡 observed by other hackathon teams (public repos), **must be verified in BE-0** · ❓ unknown

| Item | Value | Status |
|---|---|---|
| Base URL | `https://hackathon.api.qloo.com` | ✅ |
| Auth | Header `X-Api-Key: <key>` | 🟡 |
| Rate limits | `x-second-ratelimit-limit: 5`, `x-month-ratelimit-limit: 10000` response headers; bursts → 429 | 🟡 |
| Bad key | `401 {"error":{"reason":"API keys mismatch"}}` | 🟡 |
| No signal/filter | `400` "at least one valid signal or filter is required" | 🟡 |
| Entity search | `GET /search?query=…&types=…&take=…` | 🟡 |
| `types` values | short names (`artist, movie, tv_show, book, podcast, person, brand, place`) and/or URNs, comma-separated | 🟡 verify which form |
| Insights | `GET /v2/insights?filter.type=urn:entity:{artist,brand,movie,book,podcast,tv_show,person,place,destination,video_game}` | 🟡 |
| Signals | `signal.interests.entities=<id,id>`, `signal.interests.tags=<tagId,…>` | 🟡 |
| Tag insights | `filter.type=urn:tag` + `filter.tag.types=urn:tag:keyword:qloo,urn:tag:genre:qloo,…` | 🟡 |
| Location | `filter.location.query=<text>` (places), `filter.location=POINT(lon lat)` + `filter.location.radius=<m>`; resolved locality echoed in `query.localities.filter` | 🟡 |
| `signal.location.query` | **silently ignored if unresolvable** → worldwide results | 🟡 gotcha |
| Misspelled `filter.location.query` | **silently resolves to a wrong place** (e.g. France/Tasmania) | 🟡 gotcha → always check echoed locality |
| Free-text `filter.tags` | 200 with zero results, no error | 🟡 gotcha → only pass tag ids obtained from `/v2/tags` |
| Tags lookup | `GET /v2/tags?filter.query=<text>` | 🟡 |
| Explainability | `feature.explainability=true` → per-input contribution (0–1) + affinity | 🟡 |
| Paging | `take`, `page` | 🟡 |
| Demographics/audiences | `filter.type=urn:demographics`, `/v2/audiences` | ❓ probe; feature off if unsupported |
| Response shape | `results.entities[]` with `entity_id`, `name`, `type`, `subtype`, `properties{…}`, `popularity`, `query{affinity,…}`, `tags[]` | ❓ confirm exact paths |
| Terms | Publishing response data forbidden (reported) | 🟡 → no real responses in Git; cache off by default |
| Nigerian coverage | Unknown — expected thin for places/brands | ❓ measure in BE-0 |

## 2. Slice BE-0 — Probe procedure (run on your machine)
Create `scripts/qloo-probe.sh` (bash + curl + jq) **and** `scripts/qloo-probe.ps1` (PowerShell) that:
1. Read `QLOO_API_KEY` and `QLOO_BASE_URL` from env; abort if missing. Never echo the key.
2. Run the request matrix below, sleeping 300 ms between calls (≤ 4 rps).
3. Save raw responses to `qloo-probe-output/<n>-<name>.json` (**gitignored**) and response headers to `.headers`.
4. Print a summary table: request, HTTP status, result count, top 3 names, rate-limit headers, elapsed ms.

| # | Request | Purpose |
|---|---|---|
| 1 | `/search?query=Burna%20Boy&types=artist&take=3` | short-form types |
| 2 | `/search?query=Burna%20Boy&types=urn:entity:artist&take=3` | URN-form types |
| 3 | `/search?query=Nike&types=brand&take=3` | brand search |
| 4 | `/search?query=Wizkid&take=5` | untyped mixing |
| 5 | `/search?query=Orange%20Culture&types=brand&take=5` | Nigerian fashion brand coverage |
| 6 | `/search?query=Lagos&types=destination,place&take=5` | place/destination coverage |
| 7 | `/v2/tags?filter.query=streetwear` | tag lookup |
| 8 | `/v2/tags?filter.query=afrobeats` | tag lookup |
| 9 | `/v2/insights?filter.type=urn:entity:brand&signal.interests.entities=<#1 id>&take=10&feature.explainability=true` | brand affinities from artist seed |
| 10 | `/v2/insights?filter.type=urn:entity:artist&signal.interests.entities=<#3 id>&take=10` | artist affinities from brand seed |
| 11 | `/v2/insights?filter.type=urn:tag&filter.tag.types=urn:tag:keyword:qloo,urn:tag:genre:qloo&signal.interests.entities=<#1 id>&take=15` | cultural themes |
| 12 | `/v2/insights?filter.type=urn:entity:place&filter.location.query=Lagos,%20Nigeria&signal.interests.entities=<#1 id>&take=10` | local places + check `query.localities` |
| 13 | `/v2/insights?filter.type=urn:entity:brand&signal.interests.tags=<#7 tag id>&take=10` | tag-only signal |
| 14 | `/v2/insights?filter.type=urn:demographics&signal.interests.entities=<#1 id>` | demographics support? |
| 15 | `/v2/insights?filter.type=urn:entity:brand` (no signal) | error shape 400 |
| 16 | any call with wrong key | error shape 401 |
| 17 | `/v2/insights?filter.type=urn:entity:podcast&signal.interests.entities=<#1 id>&take=5` and `tv_show` | media coverage |

### Output of BE-0 (committed)
`docs/backend/qloo-verified-contract.md` containing, per request: verified ✅/❌, exact param spelling, JSON paths for
id/name/type/subtype/affinity/popularity/tags/properties/explainability/localities, error shapes, rate-limit headers,
and **Nigeria coverage notes**. Then update §1 statuses and adjust §4/§5 below if anything differs. Then hand-write
synthetic fixtures mirroring the verified shapes.

## 3. Client design
```csharp
public interface IQlooClient   // Application/Abstractions — provider-neutral models only
{
    Task<QlooResult<IReadOnlyList<QlooEntity>>> SearchEntitiesAsync(EntitySearchRequest req, CancellationToken ct);
    Task<QlooResult<IReadOnlyList<QlooTag>>>    SearchTagsAsync(string query, int take, CancellationToken ct);
    Task<QlooResult<InsightsResponse>>          GetInsightsAsync(InsightsRequest req, CancellationToken ct);
}
// QlooResult<T> = Success(T, meta: duration, rateLimitRemaining?) | Failure(QlooErrorKind, safeMessage)
// QlooErrorKind: BadRequest, AuthFailed, RateLimited, Unavailable, Timeout, InvalidResponse
```
- `InsightsRequest` is a **typed builder**, not a dictionary: `FilterType` (enum `QlooEntityType`/`Tag`/`Demographics`),
  `SeedEntityIds`, `SeedTagIds`, `TagTypes`, `LocationQuery`, `Take (1–25)`, `Explainability`. `QlooQueryBuilder`
  turns it into the verified query string; it rejects requests with no signal/filter (prevents the 400).
- Registration:
  ```csharp
  services.AddHttpClient<IQlooClient, QlooClient>(c => { c.BaseAddress = new(opts.BaseUrl); c.Timeout = Timeout.InfiniteTimeSpan; })
      .AddHttpMessageHandler<QlooApiKeyHandler>()          // adds X-Api-Key from IOptions — never logged
      .AddHttpMessageHandler<QlooThrottleHandler>()        // shared TokenBucket/SlidingWindow limiter: MaxRequestsPerSecond
      .AddHttpMessageHandler<RedactingLoggingHandler>()    // method, path, status, ms, attempt — no headers/query values with ids? (ids are fine; keys are not)
      .AddStandardResilienceHandler(o => {
          o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
          o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds * 3);
          o.Retry.MaxRetryAttempts = 2; o.Retry.UseJitter = true; o.Retry.BackoffType = DelayBackoffType.Exponential;
          o.Retry.ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)); // 408, 429, 5xx, HttpRequestException, timeout — NOT 400/401/403/404
          o.Retry.DelayGenerator = RetryAfterAware(maxDelay: TimeSpan.FromSeconds(10)); // honour Retry-After, cap it
          o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30); });
  ```
- Validate `Content-Type` is JSON; deserialize with `System.Text.Json` into `Infrastructure/Qloo/Dtos` (snake_case policy);
  map to Application models; unknown/missing fields → nulls, never throw on optional fields.
- `MockQlooClient` (`Qloo:Mode=Mock`) serves synthetic fixtures deterministically keyed by request; every entity name
  starts with `Synthetic ` and plans get `isMock=true`.
- Optional cache (`Qloo:CacheTtlMinutes>0`, default 0): `IMemoryCache` keyed by normalized request string, size-limited
  (300 entries), only for `/search` and `/v2/tags`. Enable only after confirming Qloo terms.

## 4. Cultural Intelligence Service (deterministic research planner)
`ICulturalIntelligenceService.RetrieveAsync(BriefInterpretation brief, ResearchBudget budget, IProgress<string> progress, ct)`
→ `EvidenceSet { Items, Limitations, CallsMade, Operations[] }`

The LLM **never chooses endpoints**. It only proposes *search terms* (BE-04 step 1). The planner maps the brief to a
fixed menu of operations:

| Op | When | Request | Feeds |
|---|---|---|---|
| R1 Resolve seeds | each user reference (≤ 5) + ≤ 3 AI-proposed seed names | `/search` typed by `kindHint` (fallback untyped), take 3 | seed ids |
| R2 Resolve tags | ≤ 3 keywords (category + aesthetic, e.g. "streetwear", "afrobeats", "alté") | `/v2/tags?filter.query=` | tag ids |
| R3 Brand affinities | seeds or tags exist | insights `filter.type=brand` | collaborations, positioning |
| R4 Artist/person affinities | seeds or tags exist | insights `filter.type=artist` (+ `person` if verified) | collabs, campaign culture |
| R5 Cultural themes | seeds exist | insights `filter.type=urn:tag`, keyword+genre tag types | insights, campaign angles |
| R6 Media affinities | seeds exist & budget left | insights `podcast` or `tv_show` (one, by objective) | content/sponsorship ideas |
| R7 Local places | `PrimaryLocation` set **and** location verified in BE-0 | insights `place` + `filter.location.query` | pop-up/event venues |
| R8 Demographics | only if verified in BE-0 | insights `urn:demographics` | audience hypothesis (labelled) |

Rules
- Seed matching: accept a search hit only if normalized-name similarity ≥ 0.85 (Jaro-Winkler or token-set) **and** type
  matches hint (when hint ≠ Unknown). Otherwise record limitation `"Could not confidently match '<name>' in Qloo."`
  Ambiguous (two hits ≥ 0.85 of different types) → take the hinted type or skip + limitation.
- AI-proposed seeds are marked `SeedOrigin=AiSuggested` and their evidence quality is capped at `Moderate`.
- Location guard (R7): read the echoed locality; if its country ≠ the brief's country (or absent), **discard** results and add
  limitation `"Qloo could not resolve '<location>' reliably; local venue suggestions omitted."`
- Budget: hard cap `Qloo:MaxCallsPerPlan` (default 12); concurrency ≤ 3 via `Parallel.ForEachAsync`/`SemaphoreSlim`, still
  under the global rps limiter. Dependent ops (R3–R8 need R1/R2) run after R1/R2.
- Partial failure: an op failing with `Unavailable/Timeout/RateLimited` → limitation + continue. `AuthFailed` → abort plan
  (`qloo_auth_failed`). If total usable evidence < `Generation:MinEvidenceItems` (default 5) → plan `Failed(qloo_no_evidence)`
  with a helpful message ("Add 1–3 reference brands or artists your customers love").
- Progress: after each op call `progress.Report($"{done} of {planned} Qloo requests complete")`.
- Normalize → dedupe by entity id (merge signals, max affinity) → assign keys `ev_001…` in deterministic order
  (operation order, then affinity desc, then name) → cap at `Generation:MaxEvidenceItems` (default 60).

## 5. Evidence quality (deterministic, documented — shown to users as "Evidence strength")
| Quality | Rule (first match wins) |
|---|---|
| **Strong** | From insights with a **user-provided, confidently matched** seed signal **and** (`affinity ≥ StrongAffinity` (default 0.80) **or** appeared in ≥ 2 independent operations) |
| **Moderate** | From insights with affinity present; or from AI-suggested seeds; or tag-only signal with affinity ≥ 0.80 |
| **Weak** | Tag-only signal without affinity; no affinity returned; location unverified; seed matched below threshold but kept for context |

`QualityReasons` lists the rule clauses that fired (e.g. `"user seed: Burna Boy"`, `"affinity 0.91 ≥ 0.80"`,
`"seen in 2 operations"`). Thresholds live in config and the UI tooltip says: *"Heuristic based on how the item was retrieved —
not a probability."*

Recommendation `EvidenceStrength` = `Strong` if ≥ 1 Strong ref and ≥ 2 refs total; `Weak` if all refs Weak; else `Moderate`.

## 6. Observability
Per call: op name, filter type, status, duration, attempts, result count, `x-second-ratelimit-remaining` if present.
Per plan: `qloo_call_count`, ops succeeded/failed, evidence by quality. No API keys, no full URLs with keys (keys aren't in URLs).
