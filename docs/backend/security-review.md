# Security review — BE-9 (2026-10-25)

Checked against `docs/shared/02-security-and-config.md`. "Automated" means a test runs it in CI; "manual" means
run by hand against the API (Release build, Development env, fresh Postgres, mock providers) and recorded here.

## Authentication (02-security §1)
| Check | Result | How |
|---|---|---|
| Access token is HS256 with claims `sub, jti, iat, nbf, exp, iss, aud, email, sstamp` only | ✅ | manual decode of a real token; `AuthTests.Access_token_carries_the_documented_claims…` |
| Lifetime = 15 min | ✅ `exp − iat = 900 s` | manual + automated |
| `sstamp` is a hash of the security stamp, not the raw stamp | ✅ 43-char base64url SHA-256 | manual |
| `alg=none` token rejected | ✅ 401 | manual curl + automated |
| Token signed with a different key rejected | ✅ 401 | manual curl + automated |
| Tampered payload, expired, wrong issuer, wrong audience, HS512 rejected | ✅ 401 each | automated (`Crafted_tokens_are_only_accepted_when_fully_valid`) |
| Security-stamp change invalidates tokens | ✅ | automated |
| Refresh cookie flags | ✅ `HttpOnly; SameSite=Strict; Path=/api/v1/auth`, 14-day expiry. `Secure` is set when `Jwt:CookieSecure=true` (default, required in Production by a startup guard). Local dev over plain HTTP sets it false. | `Set-Cookie` header inspected via curl. **Not checked in a real browser.** |
| Refresh rotation + reuse detection revokes the family | ✅ | automated |
| CSRF guard (`X-Requested-With: kc-web`) on refresh/logout | ✅ 400 without it, checked before rotation | automated |
| Lockout after 5 failures; same 401 for unknown email / wrong password | ✅ | automated |

## Secrets & logging (02-security §2, §7)
| Check | Result | How |
|---|---|---|
| Passwords, access tokens, refresh tokens, `Bearer` values, JWT signing key never logged | ✅ | `HardeningTests.Passwords_tokens_cookies_and_keys_never_appear_in_logs` captures every Serilog event across auth flows incl. failures and token reuse; manual run: 61 log lines, 0 matches |
| Qloo `X-Api-Key` and query values never logged (incl. retries) | ✅ | `LogRedactionTests` |
| LLM keys never logged (Gemini 5xx/429/401 paths, OpenAI network failure) | ✅ | `LogRedactionTests` |
| No secrets in Git history or working tree | ✅ gitleaks: no leaks (9 commits + tree) | `.gitleaks.toml` (default rules + custom rules for Qloo `hack_` keys and Google `AQ.` keys, which the defaults missed in a canary test); CI `secrets.yml` |
| Keys live only in user-secrets/env | ✅ `Qloo:ApiKey`, `Llm:ApiKey` in user-secrets; appsettings has none | inspection + gitleaks |

## Transport, headers, CORS, input (02-security §3, §4, §6)
| Check | Result | How |
|---|---|---|
| `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`, `CSP: default-src 'none'; frame-ancestors 'none'` | ✅ | manual curl + automated. CSP is skipped only for the Development-only `/scalar` UI. |
| HSTS + HTTP→HTTPS redirect outside Development | ✅ redirect verified; HSTS via `UseHsts()` | automated (redirect). HSTS isn't asserted: ASP.NET excludes localhost from HSTS. |
| Forwarded headers only when explicitly trusted (`App:TrustForwardedHeaders`) | ✅ off by default | config review |
| CORS: only `App:BaseUrl` (+ `AdditionalOrigins`), credentials allowed, no wildcard | ✅ allowed origin echoed; foreign origin gets no ACAO | automated |
| Request body ≤ 64 KB → 413 `payload_too_large` | ✅ | automated (middleware on declared Content-Length; Kestrel `MaxRequestBodySize` covers chunked bodies) |
| FluentValidation on every request DTO; malformed JSON/enum → 400, never 500 | ✅ | automated |

## Startup guards (02-security §1, §8)
Each case fails fast with `OptionsValidationException` (automated, `HardeningTests.Misconfiguration_fails_fast_at_startup`):
Qloo Live without key · LLM provider without key · signing key < 32 bytes · committed placeholder signing key in
Production · `Jwt:CookieSecure=false` in Production · `Llm:LogPrompts=true` in Production.

## Not verified / out of scope
- Refresh cookie behaviour in a real browser (SameSite, `Secure` over HTTPS): to do in FE-slice e2e or the demo.
- The 429 responses of the rate-limit policies themselves (configured; tests raise limits). Quota 429 *is* tested.
- `act` isn't installed. CI was run by executing each workflow's steps manually: Release build with 0 warnings,
  289 non-Live tests green, migrations on a fresh Postgres, readiness, drift check 0 errors, gitleaks clean.
