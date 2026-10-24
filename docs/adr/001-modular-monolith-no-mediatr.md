# ADR 001 — Modular monolith, Clean Architecture, no MediatR

**Decision:** Single deployable ASP.NET Core minimal-API solution (`KultureCompass.Domain` →
`Application` → `Infrastructure` → `Api`), not microservices. Use cases are implemented as plain
`XxxHandler.HandleAsync(cmd, ct) → Result<T>` classes, invoked directly from endpoint delegates — no
MediatR or other in-process mediator/pipeline-behaviour framework.

**Why:** The problem domain (one brand → one plan generation pipeline) does not need independent
scaling or deployment per bounded context. A mediator adds indirection (discovering handlers,
pipeline behaviours for validation/logging) that direct DI-resolved handler classes already give us
for free, with better debuggability (no reflection-based dispatch) and less package surface.

**Consequences:** Endpoints stay thin (bind → handler → `ToHttpResult()`); cross-cutting concerns
(validation, logging, error mapping) are explicit per handler or in a small number of shared
extension methods rather than a generic pipeline.
