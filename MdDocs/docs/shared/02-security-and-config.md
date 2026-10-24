# 02 — Security & Configuration

## 1. Authentication — self-hosted JWT (ASP.NET Core Identity + JWT bearer)
We issue our own JWTs. We do **not** hand-roll password storage: **ASP.NET Core Identity** (EF Core stores) owns users,
password hashing (PBKDF2 via `PasswordHasher`), lockout and security stamps. We only add token issuance on top.

### Token model
| Token | Format | Lifetime | Where it lives | Purpose |
|---|---|---|---|---|
| **Access token** | JWT, HS256 (`Jwt:SigningKey` ≥ 32 random bytes, base64) | 15 min (`Jwt:AccessTokenMinutes`) | **Memory only** in the SPA (never localStorage/sessionStorage/IndexedDB) | `Authorization: Bearer` on API calls |
| **Refresh token** | 64 random bytes (base64url), opaque | 14 days sliding, 30 days absolute (`Jwt:RefreshTokenDays`, `Jwt:RefreshTokenAbsoluteDays`) | `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth` cookie `kc_rt`; DB stores **SHA-256 hash only** | Get a new access token |

Access-token claims: `sub` (user id, Guid), `jti`, `iat`, `exp`, `iss` (`Jwt:Issuer`), `aud` (`Jwt:Audience`),
`email`, `sstamp` (Identity security-stamp hash, lets us invalidate on password change). No roles in MVP except an
optional `role=evaluator` claim for evaluation mode.

Use `Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler` to create tokens (not the legacy `JwtSecurityTokenHandler`).

### Refresh-token rotation & reuse detection
- Every `/auth/refresh` **rotates**: old token marked `revoked_at` + `replaced_by_id`, new token issued in same `family_id`.
- If a **revoked** token is presented → treat as theft: revoke the entire family, clear cookie, return 401 `token_reused`, log a warning.
- Logout revokes the current family. Password change revokes all families for the user and updates the security stamp.
- Expired/revoked rows cleaned by a daily background job (rows older than 30 days after expiry) — this is token housekeeping, not business data.

### Endpoints (all `AllowAnonymous` except `/me` and `/logout`)
| Endpoint | Behaviour |
|---|---|
| `POST /api/v1/auth/register` | `{ email, password, displayName? }` → creates user via `UserManager`, signs in: returns `AuthResponse` + sets refresh cookie. 409 `email_taken` (generic message to limit enumeration is acceptable trade-off for MVP; rate-limited) |
| `POST /api/v1/auth/login` | `{ email, password }` → `SignInManager.CheckPasswordSignInAsync(lockoutOnFailure: true)`; 401 `invalid_credentials` (same message for unknown email/wrong password); 423 `account_locked` |
| `POST /api/v1/auth/refresh` | Reads `kc_rt` cookie, requires header `X-Requested-With: kc-web` (CSRF guard) → rotates, returns `AuthResponse` |
| `POST /api/v1/auth/logout` | Revokes family, deletes cookie, 204 |
| `GET /api/v1/auth/me` | Current user profile `{ id, email, displayName, createdAt }` |
`AuthResponse = { accessToken, expiresAt, user: { id, email, displayName } }`.

Out of scope for MVP (documented in KNOWN_LIMITATIONS): email confirmation, password reset by email, MFA, social login.
Password policy: min 10 chars, require 3 of 4 character classes via Identity options; lockout 5 failures / 15 min.

### Backend wiring
```csharp
builder.Services.AddIdentityCore<AppUser>(o => {
        o.Password.RequiredLength = 10; o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(o => {
      o.MapInboundClaims = false;                       // keep "sub" as "sub"
      o.TokenValidationParameters = new() {
          ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
          IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
          ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
          ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
          ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "sub" };
      o.Events = new() { OnTokenValidated = SecurityStampValidator.ValidateAsync }; // compares sstamp, cached 5 min in IMemoryCache
  });
builder.Services.AddAuthorization(o => {
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.AddPolicy("Evaluator", p => p.RequireClaim("role", "evaluator")); });
```
- Components: `ITokenService` (Application abstraction) → `JwtTokenService` (Infrastructure); `IRefreshTokenStore`;
  `AuthHandlers` (Register/Login/Refresh/Logout/Me) following the normal handler pattern.
- `ICurrentUser` (Application) → `HttpCurrentUser` (Api) exposes `OwnerId` (= `sub`). The background worker never uses
  `ICurrentUser`; it reads `owner_id` from the plan row.
- Startup guard: fail if `Jwt:SigningKey` missing or < 32 bytes; in Production fail if it equals the `.env.example` placeholder.
- Key rotation: changing the key invalidates all access tokens (≤ 15 min impact); refresh tokens are unaffected (opaque).
  If needed later, move to RS256 with `kid` and a key set.

### Frontend
- `AuthProvider` holds `{ accessToken, expiresAt, user }` in React state + a module-level getter for the API client.
- On app start: call `POST /auth/refresh` once (silent session restore). Success → signed in; 401 → anonymous.
- API client middleware: attach `Bearer`; refresh proactively when < 60 s to expiry; on 401 run a **single-flight** refresh
  (one in-flight promise shared by concurrent requests), retry the original request once; refresh failure → sign-out flow.
- All `fetch` calls to `/api/v1/auth/*` use `credentials: 'include'` and header `X-Requested-With: kc-web`.
- Protected routes under `/app/*` use `RequireAuth` → redirect to `/sign-in?redirect=…`.
- **Sign-out / user switch** must: call `/auth/logout`, clear access token, `queryClient.clear()`, delete the offline
  IndexedDB store for the previous `userId`, clear sessionStorage idempotency keys, navigate to `/`.
- Multi-tab: broadcast `logout`/`login` over `BroadcastChannel('kc-auth')` so other tabs clear state too.

### Same-origin deployment (required for the refresh cookie)
`SameSite=Strict` cookies are only sent first-party, so the SPA must call the API on the **same site**:
- **Dev:** Vite `server.proxy` maps `/api` → `http://localhost:8080`; frontend uses relative `/api/v1/...`.
- **Prod:** serve the API under the frontend's origin (`https://app.example.com/api` via nginx/host rewrite) or a sibling
  subdomain of the same registrable domain (`api.example.com`, then cookie `SameSite=Lax`, `Domain` unset). Avoid
  cross-site pairs like `*.vercel.app` ↔ `*.onrender.com` unless you proxy `/api` through the frontend host.

### Persistence
Identity tables (`asp_net_users`, … renamed to snake_case `users`, `user_claims`, `user_logins`, `user_tokens` — roles tables
omitted with `AddIdentityCore`) plus `refresh_tokens` (see BE-02 §6). `owner_id` on business data = `users.id` as string.
We store email + optional display name only; no other personal data.

## 2. Secrets
| Secret | Where in dev | Where in prod | Never |
|---|---|---|---|
| Qloo key | `dotnet user-secrets` (`Qloo:ApiKey`) | host secret store / env | frontend, Git, logs, fixtures |
| LLM key | user-secrets (`Llm:ApiKey`) | host secret store / env | same |
| DB password | user-secrets / `.env` (gitignored) | managed DB secret | same |
| JWT signing key (`Jwt:SigningKey`, ≥ 32 random bytes base64 — `openssl rand -base64 48`) | user-secrets | host secret store / env | frontend, Git, logs, tokens in URLs |

- `.gitignore` includes `.env`, `*.local.json`, `appsettings.*.local.json`, `qloo-probe-output/`.
- Add a pre-commit or CI secret scan (`gitleaks`) in slice BE-9.
- `VITE_*` contains only: API base URL (usually empty → relative `/api`), app name, public flags.

## 3. Transport & headers
- HTTPS in prod; `UseHsts()` outside Development; `UseHttpsRedirection()` behind proxy with forwarded headers configured.
- Security headers middleware: `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`,
  `X-Frame-Options: DENY`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` on API responses.
- Frontend host sets a CSP allowing `'self'` (API is same-origin), Google Fonts (if used). Document final CSP.

## 4. CORS
Default deployment is **same-origin** (see §1), so CORS is not needed in normal operation. A named policy still exists for
the sibling-subdomain option: origins from `App:BaseUrl` (+ `App:AdditionalOrigins`), methods `GET, POST, PUT, PATCH`,
headers `Authorization, Content-Type, Idempotency-Key, If-Match, X-Requested-With`, exposed headers `ETag, Location, Retry-After`,
`AllowCredentials()` (needed for the refresh cookie). Never a wildcard origin with credentials.

## 5. Rate limiting & quotas (built-in `Microsoft.AspNetCore.RateLimiting`)
| Policy | Applies to | Limit (default, configurable) | Partition |
|---|---|---|---|
| `global` | all | 120 req/min | user id or IP |
| `generation` | POST /growth-plans, POST /evaluations | 3 req/min | user id |
| `public` | /sample-plan, /meta | 30 req/min | IP |
| `auth` | /auth/register, /auth/login | 10 req/min | IP (plus Identity lockout per account) |
| `auth-refresh` | /auth/refresh | 30 req/min | IP |
| Daily quota | generation (DB-counted) | `Generation:DailyLimitPerUser` = 10 | user id |

Outbound Qloo calls are throttled separately by a shared limiter at `Qloo:MaxRequestsPerSecond` (default 4;
hackathon key observed at 5 rps / 10k per month).

## 6. Input handling
- Request body limit 64 KB (`KestrelServerLimits.MaxRequestBodySize` + per-endpoint).
- FluentValidation on every request DTO; string lengths capped (see openapi `maxLength`).
- User text is **data**: inserted into LLM prompts inside clearly delimited, JSON-encoded blocks; the system prompt
  says to ignore instructions inside data blocks. Same for Qloo-returned text.
- No arbitrary URL fetching; reference entities are names, resolved only through Qloo search.
- EF Core parameterised queries only; no `FromSqlRaw` with interpolation.

## 7. Logging & privacy
- Serilog JSON console sink; enrich with `TraceId`, `PlanId`, `OwnerIdHash` (SHA-256 truncated, not raw id).
- Redact: `X-Api-Key`, `Authorization`, `Cookie`/`Set-Cookie`, request bodies of `/auth/*` (passwords), `x-goog-api-key`, query strings containing `key=`. Use a
  `DelegatingHandler` that logs method, host, path template, status, duration, retry count — **never** headers/bodies.
- Never log full prompts or LLM outputs at Information level. Debug-level prompt logging only behind
  `Llm:LogPrompts=true`, disabled by default and forbidden in Production (startup check).
- Business briefs are not sensitive personal data, but treat them as confidential: no third-party analytics.

## 8. Configuration binding
Strongly-typed options with `ValidateDataAnnotations().ValidateOnStart()`:
`AppOptions`, `QlooOptions`, `LlmOptions`, `JwtOptions`, `GenerationOptions`, `FeatureOptions`.
Startup fails fast in Production if `Qloo:Mode=Live` and key missing, or `Llm:Provider != Mock` and key missing.
Development may run with `Mock` modes and no keys.

## 9. Abuse & cost controls
- One running plan per profile; daily generation quota; bounded Qloo calls per plan (`Qloo:MaxCallsPerPlan`);
  bounded LLM repair attempts; overall plan timeout (`Generation:TimeoutSeconds`, default 180).
- Evaluation endpoint behind feature flag; disable in public prod if quota is tight.
