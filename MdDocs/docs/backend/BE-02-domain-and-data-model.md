# BE-02 — Domain & Data Model

## 1. Aggregates
| Aggregate | Owns | Notes |
|---|---|---|
| `BusinessProfile` | — | Owner-scoped brief; optimistic concurrency |
| `GrowthPlan` | `EvidenceItem[]`, `Recommendation[]`, `MarketingExperiment[]` | One generation run; state machine; immutable content after terminal state (except experiment tracking) |
| `EvaluationRun` | — | Feature-flagged comparison; references a grounded `GrowthPlan` |
| `AppUser` (Identity) | `RefreshToken[]` | Infrastructure concern — ASP.NET Core Identity user; Domain only sees `OwnerId` strings |

## 2. BusinessProfile
| Field | Type | Rules |
|---|---|---|
| `Id` | Guid v7 | |
| `OwnerId` | string(64) | JWT `sub` = `users.id` (Guid as string) |
| `BrandName` | string 2–100 | required |
| `Description` | string ≤ 1000 | optional |
| `Category` | enum `Streetwear, Fashion, Lifestyle, Beauty, FootwearAccessories, Other` | required |
| `CategoryOther` | string ≤ 60 | required if `Other` |
| `ProductsDescription` | string 10–1000 | required |
| `TargetMarket` | string ≤ 200 | e.g. "Lagos & Abuja, 18–30, students and young professionals" |
| `PrimaryLocation` | string ≤ 100 | e.g. "Lagos, Nigeria" (used for Qloo location filter **only if verified**) |
| `MarketingObjective` | enum `Awareness, Engagement, Sales, Launch, Community, Partnerships` + `ObjectiveNotes ≤ 300` | required |
| `BrandIdentity` | string ≤ 500 | tone, aesthetic, values |
| `ReferenceEntities` | list ≤ 8 of `{ name ≤ 100, kindHint: Brand/Artist/Person/Place/Media/Unknown }` | optional; resolved via Qloo search |
| `BudgetRange` | enum `Under100k, From100kTo500k, From500kTo2m, Over2m, NotSure` (NGN) | optional |
| `CreatedAt/UpdatedAt` | UTC | |
| `Version` | uint (`xmin`) | concurrency token → ETag |

## 3. GrowthPlan state machine
```
Queued ──start──► Running ──succeed──► Completed
                    │  └──succeedWithLimitations──► CompletedWithLimitations
                    └──fail(code,msg)──► Failed
Queued ──fail(timeout on recovery)──► Failed
```
- Methods: `Start(now)`, `AdvanceTo(stage, detail?, now)` (stages strictly forward), `AttachEvidence(items)`,
  `AttachResults(summary, recs, experiments, limitations, mentions)`, `Complete(now)`, `Fail(PlanError, now)`.
- `Complete` chooses `CompletedWithLimitations` when any limitation exists **or** any requested section came back
  empty **or** < 50 % of evidence is `Strong|Moderate`.
- Invariants: every `Recommendation.EvidenceKeys` ⊆ plan evidence keys; `Collaboration.PartnerEvidenceKey` ∈ evidence;
  experiments link to an existing recommendation id; terminal plans reject content changes.
- `StageLog`: list of `{ stage, state, startedAt, completedAt, detail }` persisted as jsonb for the status endpoint.

## 4. Evidence
`EvidenceItem { Key ("ev_001"…), QlooEntityId, Name, EntityType (urn), Subtype?, SourceOperation (Search|Insights|Tags|Demographics),
SourceSignal (sanitised params summary e.g. "insights: type=artist; seeds=ev_001,ev_002; location=Lagos"), Affinity?, Popularity?,
Tags[] (≤ 10 names), Metadata (whitelisted props: shortDescription, imageUrl, externalUrl, releaseYear, city/country),
Quality, QualityReasons[], RetrievedAt }`

Dedup key: `(QlooEntityId)`; when an entity appears from several operations, keep one item, merge `SourceSignal`s,
keep **max** affinity, record `AppearedInOperations` count (feeds quality).

## 5. Recommendation & Experiment
`Recommendation { Id ("ins_1","col_1","cam_1"), Kind (Insight|Collaboration|Campaign), Ordinal, Title, EvidenceKeys[],
EvidenceStrength (derived), Payload (jsonb — kind-specific fields per schema) }`
`MarketingExperiment { Id ("exp_1"), RecommendationId, Hypothesis, Segment, ProposedCampaign, PrimaryMetric,
MeasurementPeriodDays (1–90), SuccessThreshold, ThresholdIsProposedDefault, IfSuccess, IfFailure,
Status (Proposed|Running|Concluded|Abandoned), ObservedResult?, ResultNotes?, Version }`

## 6. PostgreSQL schema (snake_case; EF Core migrations; no `EnsureCreated`)
```sql
-- Identity (AddIdentityCore + IdentityUserContext<AppUser, Guid>; tables renamed to snake_case)
users(id uuid pk, email varchar(256), normalized_email varchar(256) unique, user_name varchar(256), normalized_user_name varchar(256) unique,
  password_hash text, security_stamp text, concurrency_stamp text, lockout_end timestamptz, lockout_enabled bool, access_failed_count int,
  display_name varchar(80), created_at timestamptz not null, … remaining IdentityUser columns)
user_claims, user_logins, user_tokens  -- created by Identity; unused in MVP except claims (role=evaluator)

refresh_tokens(
  id uuid pk, user_id uuid not null fk → users(id) on delete cascade, family_id uuid not null,
  token_hash char(64) not null,                -- SHA-256 hex of the raw token; raw token never stored
  created_at timestamptz not null, expires_at timestamptz not null, absolute_expires_at timestamptz not null,
  revoked_at timestamptz, revoked_reason varchar(30),   -- Rotated | Logout | Reuse | PasswordChanged
  replaced_by_id uuid, created_by_ip_hash char(64), user_agent varchar(200))
  unique ux_refresh_tokens_hash (token_hash)
  index ix_refresh_tokens_user_family (user_id, family_id)
  index ix_refresh_tokens_expires (expires_at)          -- cleanup job

business_profiles(
  id uuid pk, owner_id varchar(64) not null, brand_name varchar(100) not null, description varchar(1000),
  category varchar(32) not null, category_other varchar(60), products_description varchar(1000) not null,
  target_market varchar(200), primary_location varchar(100), marketing_objective varchar(32) not null,
  objective_notes varchar(300), brand_identity varchar(500), reference_entities jsonb not null default '[]',
  budget_range varchar(32), created_at timestamptz not null, updated_at timestamptz not null)   -- xmin as concurrency token
  index ix_business_profiles_owner_created (owner_id, created_at desc)

growth_plans(
  id uuid pk, owner_id varchar(64) not null, business_profile_id uuid not null fk → business_profiles(id) on delete restrict,
  status varchar(32) not null, stage varchar(40) not null, stage_log jsonb not null default '[]',
  idempotency_key varchar(64) not null, request_hash char(64) not null,
  brief_snapshot jsonb not null, interpretation jsonb, research_plan jsonb,
  content jsonb,                 -- { summary, limitations[], unlinkedMentions[] } versioned by schema_version
  schema_version varchar(10) not null, prompt_version varchar(20) not null,
  qloo_mode varchar(10) not null, llm_provider varchar(30) not null, llm_model varchar(80), is_mock boolean not null,
  qloo_call_count int not null default 0, llm_call_count int not null default 0, llm_usage jsonb,
  error_code varchar(40), error_message varchar(300),
  created_at timestamptz not null, started_at timestamptz, completed_at timestamptz, updated_at timestamptz not null)
  unique ux_growth_plans_owner_idem (owner_id, idempotency_key)
  index ix_growth_plans_owner_created (owner_id, created_at desc)
  index ix_growth_plans_profile_created (business_profile_id, created_at desc)
  index ix_growth_plans_status_partial (status) where status in ('Queued','Running')   -- recovery + one-at-a-time check

growth_plan_evidence(
  id uuid pk, growth_plan_id uuid fk cascade, evidence_key varchar(10) not null, qloo_entity_id varchar(100) not null,
  name varchar(200) not null, entity_type varchar(60) not null, subtype varchar(80), source_operation varchar(20) not null,
  source_signals jsonb not null, affinity numeric(6,5), popularity numeric(6,5), tags jsonb not null default '[]',
  metadata jsonb not null default '{}', quality varchar(10) not null, quality_reasons jsonb not null default '[]',
  appeared_in_operations int not null default 1, retrieved_at timestamptz not null)
  unique (growth_plan_id, evidence_key); unique (growth_plan_id, qloo_entity_id)

growth_plan_recommendations(
  id uuid pk, growth_plan_id uuid fk cascade, rec_id varchar(10) not null, kind varchar(20) not null, ordinal int not null,
  title varchar(200) not null, evidence_keys text[] not null, evidence_strength varchar(10) not null, payload jsonb not null)
  unique (growth_plan_id, rec_id); index (growth_plan_id, kind, ordinal)

marketing_experiments(
  id uuid pk, growth_plan_id uuid fk cascade, exp_id varchar(10) not null, recommendation_rec_id varchar(10) not null,
  hypothesis varchar(500) not null, segment varchar(300) not null, proposed_campaign varchar(500) not null,
  primary_metric varchar(200) not null, measurement_period_days int not null check (between 1 and 90),
  success_threshold varchar(200) not null, threshold_is_proposed_default boolean not null,
  if_success varchar(300) not null, if_failure varchar(300) not null,
  status varchar(20) not null default 'Proposed', observed_result varchar(200), result_notes varchar(1000),
  started_at timestamptz, concluded_at timestamptz,
  created_at timestamptz not null, updated_at timestamptz not null)     -- xmin concurrency
  unique (growth_plan_id, exp_id)
  index ix_experiments_status (status)   -- experiments hub; owner filter via join to growth_plans(owner_id)

evaluation_runs(
  id uuid pk, owner_id varchar(64) not null, business_profile_id uuid fk, grounded_plan_id uuid fk → growth_plans,
  status varchar(32) not null, llm_only_output jsonb, metrics jsonb, error_code varchar(40),
  created_at timestamptz not null, completed_at timestamptz)
  index (owner_id, created_at desc)
```
Daily quota query: `count(*) from growth_plans where owner_id=@o and created_at >= @utcMidnight` (covered by owner index).

## 7. Loading strategy
- `GET /growth-plans/{id}`: one query for plan + `AsSplitQuery()` includes for evidence, recommendations, experiments
  (`AsNoTracking`). No N+1.
- List endpoints project to slim DTOs (`id, brandName, status, createdAt, completedAt, counts`) — never load jsonb content.

## 8. Data retention
No automatic deletion. No soft-delete columns. (A user-initiated delete endpoint can be added later with explicit rules.)
