# backend/CLAUDE.md — Kulture Compass API (.NET 10)

Read root `/CLAUDE.md` first. Specs: `docs/backend/BE-01…05`, `docs/shared/*`, `contracts/*`.

## Rules
- Dependency direction: Api → Infrastructure → Application → Domain. Domain has zero package refs. Application has no
  HTTP/EF Core/Qloo/LLM SDK types (uses focused store interfaces).
- Minimal APIs in endpoint groups; endpoints ≤ ~10 lines: bind → handler → `ToHttpResult()`. No business logic in endpoints.
- Handlers: one class per use case, `HandleAsync(cmd, ct)`, return `Result<T>`. No MediatR, no pipeline behaviours framework.
- Validation: FluentValidation validators mirror `contracts/openapi.yaml` limits exactly.
- Auth: ASP.NET Core Identity for users/passwords (never hash passwords yourself); JWT access tokens via `JsonWebTokenHandler`
  (HS256, validate iss/aud/lifetime/alg); refresh tokens random, stored as SHA-256 hashes, rotated with reuse detection.
  Never log tokens, cookies or passwords.
- Every async method takes and forwards `CancellationToken`.
- `TimeProvider` for time, `Guid.CreateVersion7()` for ids, UTC everywhere.
- External calls only via `IQlooClient` / `ILlmClient`; typed HttpClient + resilience; bounded retries; no key in logs.
- Qloo requests must match `docs/backend/qloo-verified-contract.md`. If a parameter isn't verified, don't use it.
- LLM output is untrusted: always run `OutputValidator`; evidence keys must exist.
- Persist plan `stage_log` after each pipeline stage; progress text comes from real counters.
- EF Core: `AsNoTracking` for reads, projections for lists, `AsSplitQuery` for plan detail; migrations only.
- Tests: xUnit; Testcontainers for Postgres; fake HttpMessageHandler for providers; `[Trait("Category","Live")]` for real-key tests.
- Packages: keep to those listed in BE-01 §1; justify any addition in the slice summary.

## Commands
```bash
dotnet build KultureCompass.sln
dotnet test KultureCompass.sln --filter "Category!=Live"
dotnet ef migrations add <Name> -p src/KultureCompass.Infrastructure -s src/KultureCompass.Api -o Persistence/Migrations
dotnet run --project src/KultureCompass.Api -- --migrate
dotnet user-secrets set "Qloo:ApiKey" "…" --project src/KultureCompass.Api
```
