# Checkpoint: HTTP ingestion boundary implemented

**Date:** 2026-08-17

## Starting point

[PLAN 002 Steps 1 through 4](2026-08-17-015-ingestion-handler.md) had implemented
NDJSON framing and parsing, Event Contract v1 validation, ordered chunk persistence,
and non-HTTP ingestion orchestration. The application still had no public ingestion
endpoint, runtime PostgreSQL registration, validated chunk configuration, centralized
HTTP error handling, or OpenAPI document.

The existing Step 4 code used `Total`, `Accepted`, and derived `Rejected`, while some
Step 4 documentation incorrectly described only accepted and separately maintained
rejected values. That documentation required a factual correction before documenting
the HTTP response.

## What changed

- Corrected ADR 0006, PLAN 002, ingestion architecture, the roadmap clarification,
  and the Step 4 checkpoint to record the implemented accounting invariant:
  `Total` is every processed input record, `Accepted` advances only after successful
  chunk storage, and `Rejected` is derived as `Total - Accepted`. Persistence failure
  returns no result, so valid-but-uncommitted records are not misclassified.
- Added [ADR 0007](../decisions/0007-expose-controller-based-ndjson-ingestion-api.md),
  accepting a controller-based `POST /api/events` endpoint consuming
  `application/x-ndjson`, normal HTTP 200 accounting, HTTP 415 for unsupported media,
  and safe centralized HTTP 500 Problem Details for persistence or unhandled failure.
- Added a thin `EventsController`. It passes `Request.Body` through
  `NdjsonRecordReader` to `IngestEventsHandler`, supplies request cancellation to
  both, and returns `Ok(result)` without parsing, validation, chunking, database
  access, persistence logic, or persistence exception handling.
- Added `GlobalExceptionHandler`, which logs unexpected exceptions and writes safe
  HTTP 500 Problem Details with `HttpContext.TraceIdentifier` as `traceId`. It does
  not expose exception messages, stack traces, SQL details, or partial accounting.
- Added `IngestionOptions` using the standard Options pattern only at composition.
  `Ingestion:ChunkCapacity` is validated as positive on startup. The scoped handler
  still receives the resulting capacity as an integer.
- Added the temporary initial `ChunkCapacity` value 100. It is operational
  configuration, not a tuned performance conclusion.
- Required `ConnectionStrings:PulseFlow` during startup configuration and registered
  `PulseFlowDbContext` with Npgsql. Deployments can use the
  `ConnectionStrings__PulseFlow` environment variable; no credential was committed.
- Registered controllers, Problem Details, centralized exception handling,
  status-code pages, and the ingestion dependency graph with the accepted lifetimes.
  Startup does not call `Database.Migrate` or otherwise apply migrations.
- Added first-party ASP.NET Core OpenAPI generation and Development-only Swagger UI.
  Because the controller intentionally reads the raw stream instead of binding a body
  model, a narrowly scoped operation transformer documents the optional NDJSON body.
- Updated Event Contract v1, PLAN 002, ingestion architecture, and the roadmap. Added
  a concise roadmap section identifying deferred portfolio and production concerns as
  future intent without selecting their technologies prematurely.

## Resulting repository state

The implemented request path is:

```text
HTTP
    ↓
EventsController
    ↓
NdjsonRecordReader (singleton)
    ↓
IngestEventsHandler (scoped)
    ↓
IEventChunkStore / EfCoreEventChunkStore (scoped)
    ↓
PulseFlowDbContext (scoped)
    ↓
PostgreSQL
```

`EventEnvelopeValidator` and `NdjsonRecordReader` are singletons because they retain
no request or database state. The handler, store, and context are scoped to the HTTP
request. The handler factory reads the startup-validated `IngestionOptions` value and
passes only the integer capacity to the handler.

Normal completion returns HTTP 200 JSON with `total`, `accepted`, and `rejected`.
Empty input returns `0/0/0`, and malformed or contract-invalid records contribute to
rejected without changing the response to an error. Unsupported content types return
415 Problem Details. Persistence and other unexpected failures return safe 500
Problem Details through middleware; the response makes no partial-accounting claim,
and earlier committed chunks can remain durable.

Development exposes `/openapi/v1.json` and Swagger UI at `/swagger`. The generated
operation documents `POST /api/events`, the `application/x-ndjson` request body, and
200, 415, and 500 responses.

## Verification

Required commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds with 64 unit tests and 29 integration tests
  passed, 0 failed, and 0 skipped. No large HTTP test suite was added; Step 6 retains
  ownership of full HTTP-to-PostgreSQL verification.
- `pwsh ./scripts/check-project-docs.ps1` cannot start because `pwsh` is unavailable
  in the current environment. The same script succeeds without warnings through the
  available Windows PowerShell host as `& .\scripts\check-project-docs.ps1`.
- A focused Development runtime check confirms that OpenAPI and Swagger UI return
  HTTP 200; OpenAPI includes the NDJSON request body and 200/415/500 responses; empty
  NDJSON returns HTTP 200 with `0/0/0`; unsupported media returns 415 Problem Details;
  and a forced persistence connection failure returns safe 500 Problem Details with
  `traceId` and no exception internals.
- A startup check overriding `Ingestion__ChunkCapacity` to zero fails immediately
  with `OptionsValidationException`, confirming startup validation of the positive
  capacity invariant.

## Decisions made

- Accepted [ADR 0007](../decisions/0007-expose-controller-based-ndjson-ingestion-api.md).
- No additional architectural decision became unavoidable. The OpenAPI operation
  transformer is limited to describing the already accepted raw streaming body; it
  does not introduce a new application or transport abstraction.

## Intentionally unresolved

- Full HTTP-to-PostgreSQL behavior, including committed row verification and a
  deterministic later-chunk failure scenario, remains for PLAN 002 Step 6.
- Idempotency, deduplication, retry, and uncertain client outcomes after partial
  persistence.
- A measured and tuned chunk capacity; 100 is only an initial operational value.
- Record, upload, and request limits; authentication and authorization; rate limiting;
  health checks; correlation IDs; broader observability; and graceful shutdown work.
- Production migration execution, production secrets management, deployment, CI/CD,
  asynchronous processing, messaging, caching, load testing, and multi-instance
  behavior.
- Unknown top-level Event Contract property behavior.

## Next recommended step

Implement only PLAN 002 Step 6: add focused end-to-end tests that host the application
with a real PostgreSQL Testcontainers database, apply committed migrations in test
setup, exercise the accepted NDJSON HTTP contract, and verify committed rows directly.
Do not add the deferred production, reliability, asynchronous-processing, deployment,
or observability concerns to that step.
