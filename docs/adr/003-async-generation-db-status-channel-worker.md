# ADR 003 — Async generation via DB-persisted status + in-process `Channel` worker

**Decision:** `POST /growth-plans` inserts a `Queued` plan row and enqueues its id onto a bounded
in-process `Channel<Guid>`. A single `BackgroundService` (`GenerationWorker`, max concurrency 2 via
`SemaphoreSlim`) dequeues ids, opens a fresh DI scope per plan, and runs the pipeline, persisting
`stage_log` after every stage. Clients poll `GET /growth-plans/{id}/status`. No message broker
(RabbitMQ/Azure Service Bus/etc.) and no Redis.

**Why:** A single-process hackathon-scale deployment doesn't need distributed queuing. The DB row is
already the durable source of truth (status, stage, stage_log), so a broker would duplicate state
without adding reliability we need; `StuckPlanRecovery` on startup covers the "process restarted
mid-run" case a broker would otherwise handle.

**Consequences:** Horizontal scaling of generation workers is not supported without revisiting this
decision (the channel and semaphore are process-local). Acceptable for the stated scope.
