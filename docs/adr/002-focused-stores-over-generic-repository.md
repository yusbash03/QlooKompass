# ADR 002 — Focused stores over a generic repository / `IAppDbContext`

**Decision:** Application defines narrow, purpose-built store interfaces
(`IBusinessProfileStore`, `IGrowthPlanStore`, `IEvaluationStore`) with the ~10 queries the use cases
actually need, implemented against EF Core in Infrastructure. Application does **not** reference
`Microsoft.EntityFrameworkCore` or expose a generic `IRepository<T>`/`IAppDbContext`.

**Why:** The analysis identified a small, fixed set of queries per aggregate. A generic repository
or a leaked `DbSet<T>` abstraction would give Application the illusion of arbitrary querying power
while still coupling it to EF Core's shape, without adding real flexibility for this project's size.

**Consequences:** Adding a new query means adding a method to the relevant store interface — a
one-line, intention-revealing change — rather than writing ad hoc LINQ against an injected context
in a handler.
