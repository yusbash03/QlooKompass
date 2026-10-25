# LLM contract — status

Verified **2026-10-25** against a real Gemini API key (BE-6). No prompts, outputs or real entity names from these
runs are committed; only shapes, field names and measurements.

## Gemini (`Llm:Provider=Gemini`) — verified

| Item | Finding |
|---|---|
| Endpoint | `POST {Llm:BaseUrl}/models/{Llm:Model}:generateContent`, `BaseUrl = https://generativelanguage.googleapis.com/v1beta`. ✅ |
| Auth | Header `x-goog-api-key`. The key is never put in the query string or logs. ✅ |
| Models | `GET /models` with the key listed 46 `generateContent` models, including `gemini-flash-lite-latest`, `gemini-flash-latest` and `gemini-2.5-flash`. `gemini-flash-lite-latest` currently resolves to `modelVersion: gemini-3.5-flash-lite`. ✅ |
| Request | `systemInstruction.parts[0].text`, `contents[0] = {role:"user", parts:[{text}]}`, `generationConfig = {responseMimeType:"application/json", responseJsonSchema, maxOutputTokens, temperature}`. ✅ |
| `responseJsonSchema` | Accepts JSON-Schema keywords incl. `additionalProperties:false`, `enum`, `pattern`, `minLength/maxLength`, `minimum/maximum`, `type:["string","null"]`. ✅ |
| **Schema size limit** | The full `growth-plan-llm-output.schema.json` is **rejected** with `400 INVALID_ARGUMENT` ("Request contains an invalid argument"). Bisected: it is cumulative constraint complexity, not one keyword. Removing any single top-level array's `maxItems` still fails; removing **all array `minItems`/`maxItems`** is accepted. |
| → What the client does | Send the full schema; on 400, retry **once** with array bounds stripped; cache that per schema name so later calls skip the failing attempt. `OutputValidator` enforces the counts server-side (BE-04 §1 rule 6). |
| Response | `candidates[0].content.parts[*].text`. Parts flagged `thought: true` are excluded. `finishReason` values include `STOP`, `MAX_TOKENS` (→ `Truncated`), `SAFETY`/`RECITATION`/`BLOCKLIST`/`PROHIBITED_CONTENT`/`SPII` (→ `Refused`). No candidates plus `promptFeedback.blockReason` → `Refused`. |
| Usage | `usageMetadata.{promptTokenCount, candidatesTokenCount, thoughtsTokenCount, totalTokenCount}`. `thoughtsTokenCount` was absent on `flash-lite` in these runs. |
| 429 | Treated as `QuotaExceeded` (no retry) when the body names a per-day quota id; otherwise `RateLimited`, retried once honouring `retryDelay` / `Retry-After` (cap `Llm:MaxRetryDelaySeconds`, default 20). **Not reproduced live**; covered by fake-handler tests only. |

### Live measurements (model `gemini-flash-lite-latest`)
| Run | Result | Tokens (prompt / output) |
|---|---|---|
| Tiny schema `{ok:boolean}` | valid JSON | 10 / 5 |
| Growth-plan schema, 8 synthetic evidence items | passed `OutputValidator`, 0 repairs | 987 / 1,426 |
| Full pipeline, live Qloo + Gemini, Lagos streetwear brief (run 1) | `CompletedWithLimitations`, 2 LLM calls, 12 Qloo calls, 50 evidence, 3 insights / 1 collaboration / 1 campaign / 1 experiment, 0 repairs, ~15 s | 4,373 / 2,532 |
| Same, run 2 | `CompletedWithLimitations`, 60 evidence, 3 / 2 / 1 / 1, 0 repairs, ~18 s | 5,025 / 2,168 |
| LLM-only schema (evidence-key fields removed, BE-8 evaluation baseline) | accepted; 3 insights / 2 collaborations / 1 campaign; 7 entities declared, all `evidenceKey: null`, no `ev_` keys | 375 / 1,396 |

`CompletedWithLimitations` in both pipeline runs came from genuine research limitations (one tag term not found in Qloo;
13 planned operations against the 12-call budget), not from validation.

## OpenAI-compatible (`Llm:Provider=OpenAiCompatible`) — UNVERIFIED
Implemented per BE-04 §2 (`/chat/completions`, Bearer, `response_format: json_schema strict` → fallback
`json_object` on 400). Covered by fake-handler tests only. No live provider key was available.
