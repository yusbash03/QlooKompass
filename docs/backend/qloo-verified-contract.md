# Qloo contract — status

**Verified against a real hackathon API key** (BE-0 live probe, run via `scripts/qloo-probe.ps1` plus targeted
follow-up calls to resolve the chained-id requests the probe matrix needs). Base URL
`https://hackathon.api.qloo.com`. No raw entity/tag names or full response bodies from the real account are
reproduced below or anywhere else in this repo — only shapes, param spellings, and structural findings, per the
"no real Qloo responses in Git" rule. `QlooClient`, `QlooQueryBuilder`, the DTOs, and the synthetic fixtures have
all been updated to match what's described here.

## 1. Confirmed request shapes

| Item | Finding |
|---|---|
| Auth | Header `X-Api-Key: <key>`. Wrong key → `401`. ✅ |
| `/search?query=&types=&take=` | **`types` must be URN-form** (`urn:entity:artist`), comma-joined for multiple. Short form (`artist`) → `400` with a body listing every allowed value. ✅ corrects BE-03 §1, which guessed short-form might work. |
| Allowed `/search` `types` values (from the 400 body) | `urn:entity:{actor, album, artist, author, book, brand, destination, director, locality, movie, person, place, podcast, tv_show, videogame}`, plus `urn:demographics`, `urn:tag`. Note **`videogame` has no underscore** — `QlooQueryBuilder` previously emitted `video_game`; fixed. |
| `/v2/tags?filter.query=&take=` | Confirmed `results.tags[].{id, name, type, popularity, parents, properties}` — matches the original assumption. ✅ |
| `/v2/insights?filter.type=&signal.interests.entities=&...` | `filter.type` also takes URN-form. Tag-only signal (`signal.interests.tags=`), entity-signal, and the combined brand/artist/person/place filter types all return `200` with real data. `urn:entity:person` and `filter.location.query` + `signal.interests.*` (R7) both work end-to-end. ✅ |
| No signal/filter on `/v2/insights` | `400`, confirming the documented "at least one valid signal or filter is required" case. ✅ |
| `/v2/demographics` (`filter.type=urn:demographics`) | Responds `200`, but its shape is **not** entity-shaped — see §3 below. Left feature-gated off. |
| Rate-limit headers | `X-Second-RateLimit-{Limit,Remaining,Reset}` and `X-Month-RateLimit-{Limit,Remaining,Reset}` all present and correctly cased as assumed. Observed limits on the hackathon key: 10 req/s, 50,000 req/month. ✅ |

## 2. Confirmed response shapes (this is the big correction)

**`/search` and `/v2/insights` do NOT share the same envelope or entity shape** — BE-03 §1's single assumed shape
(`results.entities[].{...type,subtype...}`) was wrong for `/search` and only half-right for `/v2/insights`.

- **`/search`**: `results` is a **flat array** of entities directly (no `.entities` nesting). Each entity has
  `entity_id`, `name`, **`types` (a plural array of URN strings, not a singular `type`)**, `popularity`,
  `disambiguation`, `properties`, `tags[]`. No `subtype` field, no `query`/affinity (search has no signal to score
  against). Embedded `tags[]` entries use **`tag_id`**, not `id` (different from the top-level `/v2/tags` shape).
- **`/v2/insights`**: `results.entities[]` nesting is correct as originally assumed. But each entity's **`type` is
  always the generic literal `"urn:entity"`; the real type lives in `subtype`** (e.g. `"urn:entity:brand"|"urn:entity:place"`).
  Also has `query.affinity`, `query.measurements.audience_growth`, `query.explainability.*`, `disambiguation`,
  `external.{twitter,wikidata,instagram,google_place,...}`, and for places a top-level `location.{lat,lon}`.
- **Location echo (R7)**: `query.localities.filter[]` has **no `country` field** — only `disambiguation`
  (e.g. a "City, Country"-shaped string). `QlooMapper.ParseCountryFromDisambiguation` takes the last
  comma-separated segment as the country, the same heuristic `BriefInterpretation.DeriveDeterministically` already
  used for the brief's own `PrimaryLocation`.

`QlooMapper.ResolveType` now picks `subtype` when present (insights), else the first of `types[]` (search), else
falls back to the raw `type` field. `QlooEntity.Subtype` is left unset — what Qloo calls "subtype" is the real
type itself, not a finer-grained classification, so duplicating it there would be redundant/confusing.

## 3. Demographics — responds, but isn't entity-shaped

`filter.type=urn:demographics` returns `200` with `results.demographics[].{entity_id, query.{age: {bucket: score,
...}, gender: {male: score, female: score}}}` — age/gender affinity *score buckets* for the seed entity, nothing
like the `results.entities[]` every other insights call returns. `CulturalIntelligenceService` has no path to turn
that into an `EvidenceItem` yet (it would need a dedicated demographic-hypothesis type, not evidence rows).
`Qloo:DemographicsVerified` stays `false` — R8 needs real design work, not just a flag flip, before it's safe to
turn on.

## 4. What changed in code as a result

- `QlooQueryBuilder`: `/search` `types=` now emits URN-form; `videogame` type name fixed (no underscore).
- `Infrastructure/Qloo/Dtos/QlooDtos.cs`: `SearchEnvelopeDto.Results` is now a flat `IReadOnlyList<QlooEntityDto>`;
  `QlooEntityDto` gained a `Types` array field; `QlooTagDto` resolves `Id ?? TagId`; `QlooLocalityDto` gained
  `Disambiguation`.
- `QlooMapper`: `ResolveType` (subtype-then-types[0]-then-type fallback) and `ParseCountryFromDisambiguation`.
- `Qloo:LocationInsightsVerified` and `Qloo:PersonInsightsVerified` now default `true`; `Qloo:DemographicsVerified`
  stays `false` (§3).
- Synthetic fixtures under `backend/tests/Fixtures/qloo/` rewritten to match the confirmed shapes (still
  `"_synthetic": true`, still every name prefixed `Synthetic `, still no real values).

## 5. Live validation performed

- `QlooLiveTests` (`[Trait("Category","Live")]`, previously never run): both tests pass against the real key.
- BE-4's dev console command (`dotnet run -- research --profile-json backend/samples/lagos-streetwear-sample.json`)
  run end-to-end in **both** `Qloo:Mode=Mock` and `Qloo:Mode=Live`. In Live mode: all 3 user-provided reference
  seeds resolved with high-confidence name+type matches, brand/artist/person/podcast/local-place insights all
  returned real results (R7's location guard correctly verified Nigeria and kept the results), 50 evidence items
  assembled with deterministic `ev_001..ev_050` keys, all classified Strong quality, well above the 5-item minimum.
  Zero limitations. (R5 cultural-themes, `filter.type=urn:tag`, returned no usable items in this run — not yet
  investigated further; not blocking since it degrades to a limitation-free empty result rather than an error.)
- Nigerian coverage: strong for artists/brands/people/places in this one real run (Lagos hotels resolved
  correctly via R7); not a systematic measurement across many queries.

## 6. Cross-check against the official hackathon developer guide (2026-10-25)

Source: https://docs.qloo.com/reference/qloo-llm-hackathon-developer-guide. Each point that differed from our
contract was re-tested live; **live behaviour wins** where the two disagree.

| Guide says | Live result | Action |
|---|---|---|
| Only `https://hackathon.api.qloo.com` works with hackathon keys; auth via `X-Api-Key` only (no Bearer, no query param) | Matches §1 | none |
| Endpoints: `/v2/insights`, `/search`, `/v2/tags` (all GET). `/recommendations`, `/recs`, `/v2/insights/search` unsupported | Matches — we use only these three | none |
| `filter.type` allowed list includes `urn:entity:video_game` | **`urn:entity:video_game` → 400** "not supported for filter.type" on insights and `/search`; `urn:entity:videogame` → 200 | keep `videogame` (no underscore); guide is wrong here |
| `urn:tag` / `urn:demographics` not in the guide's `filter.type` list | Both → 200 with a seed entity signal | none (demographics still gated, §3) |
| Unsupported `filter.type` → 403 | Unsupported spelling observed as 400 (above); 403 not reproduced | `QlooClient` maps both to non-retried failures already |
| Invalid params are **silently dropped**; empty results can mean bad params *or* no data | — | treat empty results as a limitation, never as "no cultural signal" |
| Private server-side caching allowed, no time limit; never store responses in a public repo | — | `Qloo:CacheTtlMinutes>0` is permitted; default stays 0, no-real-responses-in-Git rule unchanged |
| No numeric rate limits published | Headers show 10 rps / 50k per month on this key (§1) | keep `MaxRequestsPerSecond=4` |

**R5 fix (found during this cross-check):** `filter.type=urn:tag` insights return items under
**`results.tags[]`** (not `results.entities[]`), each keyed by `tag_id` with the tag type in `subtype`
(e.g. `urn:tag:keyword:qloo`) plus `types[]`, `popularity`, `query.affinity`, `tag_value`. The client only read
`entities[]`, which is why R5 was empty in §5. `InsightsResultsDto` now also reads `tags[]` and
`QlooMapper.ToInsightsItems` merges both. Re-run of the live Lagos research after the fix: 11/12 calls,
60 evidence items (cap) — 10 each of artist/brand/person/place/podcast plus 10 `urn:tag:keyword:qloo` themes,
all Strong, no limitations.

## 7. Remaining open items

- Error response **bodies** beyond status code (e.g. the exact 401/400 JSON shape) are known from this probe but
  not parsed by `QlooClient` — it still maps on HTTP status only, which is sufficient for `QlooErrorKind` but
  discards the server's specific message.
- `MockQlooClient` (synthetic fixtures, `Qloo:Mode=Mock`) remains reliable for development/demo/tests.
  `QlooClient` (`Qloo:Mode=Live`) is now verified end-to-end for search, tags, and insights (brand/artist/person/
  place), with demographics explicitly out of scope until R8 gets dedicated handling.
