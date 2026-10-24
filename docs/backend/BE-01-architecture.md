# BE-01 — Backend Architecture

## 1. Shape
Modular monolith, Clean Architecture, .NET 10 LTS, ASP.NET Core **minimal APIs** (endpoint groups), one deployable.

```
backend/
  KultureCompass.sln
  Directory.Build.props          # Nullable enable, TreatWarningsAsErrors, LangVersion latest, AnalysisLevel latest
  Directory.Packages.props       # Central package management
  src/
    KultureCompass.Domain/          (no package refs)
    KultureCompass.Application/     → Domain           (+ FluentValidation, M.E.Logging.Abstractions, M.E.Options)
    KultureCompass.Infrastructure/  → Application      (+ EF Core, Npgsql, EFCore.NamingConventions, M.E.Http.Resilience, M.AspNetCore.Identity.EntityFrameworkCore, M.IdentityModel.JsonWebTokens)
    KultureCompass.Api/             → Application, Infrastructure (composition root only)
                                       (+ JwtBearer, OpenApi, Scalar.AspNetCore, Serilog.AspNetCore, HealthChecks.NpgSql)
  tests/
    KultureCompass.Domain.Tests/
    KultureCompass.Application.Tests/
    KultureCompass.Infrastructure.Tests/   (+ Testcontainers.PostgreSql, RichardSzalay.MockHttp or custom handler)
    KultureCompass.Api.Tests/              (+ Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql)
    Fixtures/                              (synthetic Qloo + LLM JSON, linked into test projects)
```
Add a `KultureCompass.ArchitectureTests` check (NetArchTest or a reflection test in Domain.Tests) asserting:
Domain references nothing; Application doesn't reference Infrastructure/Api/EF Core/HTTP types.

## 2. Folder layout per project (feature-first)
```
Domain/
  Common/ (Entity base, DomainException, Clock abstraction ISystemClock? → use TimeProvider)
  Profiles/BusinessProfile.cs, BudgetRange.cs
  Plans/GrowthPlan.cs, GenerationStatus.cs, GenerationStage.cs, PlanError.cs
  Evidence/EvidenceItem.cs, EvidenceKey.cs, EvidenceQuality.cs, EvidenceQualityRules.cs, EvidenceStrength.cs
  Recommendations/Recommendation.cs, RecommendationKind.cs
  Experiments/MarketingExperiment.cs, ExperimentStatus.cs
Application/
  Auth/ Register/, Login/, Refresh/, Logout/, Me/ (handlers + validators), AuthResponse.cs
  Abstractions/ ICurrentUser, ITokenService, IRefreshTokenStore, IUserAccounts (wraps UserManager/SignInManager), IAppDbContext*, IQlooClient, ICulturalIntelligenceService, ILlmClient, IGenerationQueue
  Profiles/ CreateProfile/, UpdateProfile/, GetProfile/, ListProfiles/  (Command/Query record + Handler + Validator + Dto)
  Plans/ StartGeneration/, GetPlan/, GetPlanStatus/, ListPlans/, UpdateExperiment/
  Generation/ GrowthPlanPipeline.cs, BriefInterpreter.cs, ResearchPlanner.cs, EvidenceEvaluator.cs,
              RecommendationSynthesizer.cs, OutputValidator.cs, ExperimentDesigner.cs, Prompts/
  Evaluation/ StartEvaluation/, GetEvaluation/, EvaluationScorer.cs
  Common/ Result<T>, AppError, PagedResult<T>, IdempotencyHasher
Infrastructure/
  Identity/ AppUser.cs (IdentityUser<Guid> + DisplayName, CreatedAt), JwtTokenService.cs, JwtOptions.cs, RefreshTokenStore.cs,
            UserAccounts.cs, SecurityStampValidator.cs, RefreshTokenCleanupJob.cs, DemoUserSeeder.cs
  Persistence/ AppDbContext.cs (IdentityUserContext<AppUser, Guid>), Configurations/*.cs, Migrations/, DesignTimeFactory.cs
  Qloo/ QlooClient.cs, QlooOptions.cs, Dtos/*.cs, QlooQueryBuilder.cs, MockQlooClient.cs, QlooRateLimiterHandler.cs, RedactingLoggingHandler.cs
  Llm/ GeminiLlmClient.cs, OpenAiCompatibleLlmClient.cs, MockLlmClient.cs, LlmOptions.cs
  Generation/ ChannelGenerationQueue.cs, GenerationWorker.cs (BackgroundService), StuckPlanRecovery.cs
  DependencyInjection.cs
Api/
  Program.cs, Endpoints/{Auth,Profiles,Plans,Evaluations,Meta,SamplePlan,Health}Endpoints.cs,
  Auth/HttpCurrentUser.cs, ErrorHandling/ProblemDetailsExceptionHandler.cs, Middleware/SecurityHeaders.cs,
  Contracts/ (request/response DTOs if not shared from Application), OpenApi/ transformers
```
\* **`IAppDbContext`** decision: Application handlers use an `IAppDbContext` interface exposing `DbSet<T>`s and
`SaveChangesAsync`. This keeps handlers testable with Testcontainers/SQLite and avoids a generic repository.
It does mean Application references `Microsoft.EntityFrameworkCore` (abstractions only). If you prefer the stricter
rule, use focused interfaces `IBusinessProfileStore` / `IGrowthPlanStore` instead — **pick one in BE-1 and record an ADR**.
Default recommendation: **focused stores** (`IBusinessProfileStore`, `IGrowthPlanStore`, `IEvaluationStore`) — the
analysis explicitly names `IBusinessProfileRepository`/`IGrowthPlanRepository`, and only ~10 queries exist.

## 3. Handler pattern (no MediatR)
```csharp
public sealed record CreateProfileCommand(CreateBusinessProfileRequest Body);
public sealed class CreateProfileHandler(IBusinessProfileStore store, ICurrentUser user,
    IValidator<CreateBusinessProfileRequest> validator, TimeProvider clock)
{
    public async Task<Result<BusinessProfileDto>> HandleAsync(CreateProfileCommand cmd, CancellationToken ct) { … }
}
// Registration: services.AddScoped<CreateProfileHandler>();  (or Scrutor scan of *Handler — optional)
// Endpoint:   group.MapPost("/", async (CreateBusinessProfileRequest body, CreateProfileHandler h, CancellationToken ct)
//                 => (await h.HandleAsync(new(body), ct)).ToHttpResult(created: dto => $"/api/v1/business-profiles/{dto.Id}"));
```
`Result<T>` carries `AppError(Code, Message, Kind)`; `Kind ∈ Validation | NotFound | Conflict | PreconditionFailed |
PreconditionRequired | RateLimited | Unavailable`. One `ToHttpResult()` extension maps to Problem Details — endpoints
stay thin.

## 4. Generation runtime
```
POST /growth-plans ─► StartGenerationHandler
                        ├─ validate, quota, idempotency lookup
                        ├─ insert GrowthPlan(Queued, brief snapshot)
                        └─ IGenerationQueue.EnqueueAsync(planId)   ──►  Channel<Guid> (bounded 100)
                                                                         │
GenerationWorker (BackgroundService, MaxConcurrency=2 via SemaphoreSlim) ◄┘
   └─ new DI scope per plan → GrowthPlanPipeline.RunAsync(planId, ct)
        stages persisted after each step (status endpoint reads them)
Startup: StuckPlanRecovery → Queued plans re-enqueued; Running plans older than timeout → Failed(timeout)
```
Overall per-plan timeout via linked `CancellationTokenSource(Generation:TimeoutSeconds)`.

## 5. Cross-cutting
- **Time**: inject `TimeProvider` (testable), store UTC `timestamptz`.
- **IDs**: `Guid.CreateVersion7()` for all primary keys (sortable).
- **Logging**: Serilog; `LogContext.PushProperty("PlanId", id)` inside pipeline; provider handlers log duration/status/retries.
- **OpenAPI**: `builder.Services.AddOpenApi()`; map `/openapi/v1.json`; Scalar UI at `/scalar` in Development only.
  Schema transformers add enum string values and `Idempotency-Key`/`If-Match` header params to match `contracts/openapi.yaml`.
- **JSON**: `JsonStringEnumConverter`, camelCase, `DefaultIgnoreCondition = WhenWritingNull` **off** for plan payloads
  (explicit nulls help the FE).
- **Health**: `/api/v1/health` (self), `/api/v1/health/ready` (Npgsql check + options sanity). No live provider calls.

## 6. Architecture Decision Records (create `docs/adr/` in BE-1)
| ADR | Decision |
|---|---|
| 001 | Modular monolith + Clean Architecture; minimal APIs; no MediatR (direct handlers) |
| 002 | Focused stores over EF Core instead of generic repository |
| 003 | Async generation with DB status + in-process `Channel` worker + polling (no broker) |
| 004 | Self-hosted auth: ASP.NET Core Identity + JWT access tokens (HS256, 15 min) + rotating hashed refresh tokens in HttpOnly cookie; owner = JWT `sub`; 404 for non-owned resources |
| 005 | Gemini free tier as primary LLM behind `ILlmClient`; Mock + OpenAI-compatible adapters |
| 006 | Plan content stored as versioned JSONB + relational evidence/recommendation/experiment tables |
| 007 | Evidence quality is deterministic; no LLM confidence scores |
| 008 | No committed real Qloo data; synthetic fixtures only; cache disabled by default |
