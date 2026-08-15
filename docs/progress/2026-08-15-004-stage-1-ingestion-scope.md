# Checkpoint: Stage 1 ingestion scope clarified

**Date:** 2026-08-15

## Starting point

Stage 1 was in progress, and the v1 event envelope was accepted in `docs/contracts/event-ingestion-v1.md`. The API still had no PulseFlow endpoints or persistence implementation.

The roadmap required a minimal way to see the persisted result and referred to accepting an event or a small batch. Those statements left room to infer a CRUD-style `GET /api/events/{id}` endpoint or accepted batch semantics even though neither had been decided.

## What changed

- Clarified the Stage 1 roadmap without changing the Event Contract v1 envelope.
- Defined direct PostgreSQL inspection from an integration test as an acceptable way to verify persistence.
- Made explicit that a technical persistence identifier does not create a public retrieval use case.
- Recorded that operator or consumer retrieval APIs, downstream processing transfer, and batch ingestion semantics remain unresolved.
- Expanded the Stage 1 guardrails against prematurely selecting messaging, caching, polling, delivery, idempotency, or query-model details.

## Resulting state

Stage 1 remains in progress and is scoped as the first persistence-backed ingestion vertical slice of a production-style event ingestion and processing system. It is not scoped as CRUD for stored events.

The accepted envelope still consists only of `type`, `source`, `occurredAt`, and an opaque arbitrary JSON object `payload`. No endpoint, request model, persistence model, OpenAPI operation, retrieval API, batch behavior, processing-transfer mechanism, or infrastructure technology has been implemented or selected by this clarification.

## Verification

Run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- the solution builds with no warnings or errors;
- the test command succeeds and reports no discoverable tests in either test project;
- the documentation check completes without warnings;
- the roadmap and latest checkpoint consistently distinguish persistence verification from a public retrieval API.

## Decisions made

- PulseFlow is treated as a production-style event ingestion and processing system, not as an event CRUD API.
- A technical event identifier may be used by persistence, but `GET /api/events/{id}` is not a primary retrieval flow and is not required solely to verify storage.
- Stage 1 persistence may be verified by an integration test that queries PostgreSQL directly.
- The existing Event Contract v1 envelope and opaque object semantics of `payload` remain unchanged.

No ADR was created because this clarification constrains the current stage and explicitly avoids selecting application architecture or infrastructure.

## Still unresolved

- The HTTP method, route, success response, and validation-error representation for ingestion.
- Whether the first ingestion endpoint accepts one event, a batch, or both.
- Field-length, payload-size, and nesting limits, plus handling of unknown envelope properties.
- Authentication and authorization for the ingestion surface.
- The PostgreSQL schema, technical identifier shape, data-access approach, and migration strategy.
- Any operator or consumer query and retrieval API.
- The mechanism for transferring persisted events to later processing.
- RabbitMQ, Redis, polling, queue or stream technology, delivery guarantees, idempotency semantics, and API query model.
- The request model, matching OpenAPI operation, and automated contract and persistence tests.

## Next recommended step

Accept only the remaining HTTP behavior and persistence details required for the smallest Stage 1 ingestion vertical slice, then implement and verify that slice without adding a public retrieval endpoint or downstream processing technology by inference.
