# Checkpoint: PLAN 002 / Stage 1 ingestion pipeline completed

**Date:** 2026-08-18

## Starting point

[PLAN 002](../plans/002-stage-1-ingestion-pipeline.md) Steps 1 through 6 were
implemented and verified. The most recent checkpoint verified the final Step 6
scenario: a deterministic later persistence failure after an earlier real PostgreSQL
chunk commit. Step 7 remained to review the complete vertical slice, reconcile active
documentation, and decide whether the plan and Roadmap Stage 1 could be completed.

## What changed

- Reviewed the actual Stage 1 implementation, the focused unit and integration tests,
  and ADRs 0001 through 0007 against the PLAN 002 completion criteria.
- Reconciled the Event Contract v1 document with the implemented HTTP endpoint and
  the verified real-PostgreSQL path.
- Reconciled the ingestion architecture document with the fully implemented Stage 1
  slice and recorded that Step 6 verifies the real hosted HTTP-to-PostgreSQL path.
- Marked Roadmap Stage 1 and PLAN 002 completed after the review and required
  verification passed.
- Reconciled the Roadmap with the configured initial `Ingestion:ChunkCapacity` value
  of 100 while preserving that it is configurable, validated, and not tuned.

## Final implemented Stage 1 flow

```text
HTTP
    ↓
EventsController
    ↓
NdjsonRecordReader
    ↓
EventEnvelopeValidator
    ↓
IngestEventsHandler
    ↓
IEventChunkStore
    ↓
EfCoreEventChunkStore
    ↓
PulseFlowDbContext
    ↓
PostgreSQL
```

`POST /api/events` consumes `application/x-ndjson`. The reader parses one completed
record at a time with a fixed-size I/O buffer and buffers only the current record; the
handler retains only the current configurable persistence chunk. Malformed and
contract-invalid records are independently rejected without rejecting later valid
records. `EventEnvelope` is the validated ingestion-contract type and remains
separate from the EF Core `EventRecord` persistence type.

Valid records are stored in ordered configurable chunks. A PostgreSQL commit is the
acceptance boundary, so `Accepted` advances only after `IEventChunkStore.StoreAsync`
completes successfully. A later store failure propagates to the centralized safe HTTP
500 Problem Details response without partial accounting; earlier committed chunks can
remain durable.

## Important accepted decisions

- [ADR 0001](../decisions/0001-use-ndjson-for-batch-ingestion.md) selects independent
  NDJSON record handling.
- [ADR 0002](../decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md)
  defines configurable chunking and PostgreSQL commit as acceptance.
- [ADR 0003](../decisions/0003-construct-event-envelope-after-contract-validation.md)
  keeps untrusted input, `EventEnvelope`, and `EventRecord` separate.
- [ADR 0004](../decisions/0004-define-ndjson-record-framing.md) defines framing and
  cancellation semantics.
- [ADR 0005](../decisions/0005-generate-event-persistence-metadata-in-application.md)
  defines application-generated persistence metadata.
- [ADR 0006](../decisions/0006-keep-ingestion-handler-failure-propagation-simple.md)
  keeps failure propagation non-HTTP and does not return partial handler results.
- [ADR 0007](../decisions/0007-expose-controller-based-ndjson-ingestion-api.md)
  defines the controller boundary and safe HTTP failure response.

## E2E guarantees verified

- Real `WebApplicationFactory` HTTP requests use a real PostgreSQL Testcontainers
  database after applying the committed migrations in controlled test setup.
- Valid-event response accounting and persistence are verified.
- Mixed valid, malformed, and contract-invalid records are independently accounted
  for, while valid records persist.
- A test chunk capacity of two verifies one full chunk and a final partial chunk.
- A deterministic test-only second store call failure produces HTTP 500 while the
  earlier chunk, committed through the real EF Core/PostgreSQL store, remains durable
  and the failing chunk is absent.

The review also confirmed no full-upload buffering, no HTTP or EF Core dependency in
`IngestEventsHandler`, no production automatic migration call, no public GET endpoint
added for verification, no retry/idempotency semantics, no test-only failure
infrastructure in production code, and no C# primary constructors.

## Documentation reconciled

- `docs/contracts/event-ingestion-v1.md`
- `docs/architecture/ingestion.md`
- `docs/02_ROADMAP.md`
- `docs/plans/002-stage-1-ingestion-pipeline.md`

## Resulting repository state

PLAN 002 is **Completed** as of 2026-08-18. Roadmap Stage 1: Basic Data Ingestion is
**Completed**. Stage 2 remains **Not started**. No new ADR was required because the
review introduced no new architectural decision.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
& .\scripts\check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeded with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` passed: 64 unit tests and 34 integration tests, with
  0 failed and 0 skipped.
- `pwsh ./scripts/check-project-docs.ps1` could not start because PowerShell 7 is not
  installed in this environment.
- The same documentation script completed successfully with the available Windows
  PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Intentionally unresolved

- Idempotency, deduplication, retries, and behavior after an uncertain client retry.
- Request, record, payload, and nesting limits; compression; and unknown top-level
  Event Contract property behavior.
- Authentication and authorization.
- Production migration execution, health/readiness, metrics, tracing, CI/CD, AWS,
  load testing, horizontal scaling, graceful shutdown hardening, and a tuned chunk
  capacity.
- Asynchronous processing, RabbitMQ, Redis, Outbox, and all other Stage 2 work.

## Next recommended step

Start a separately scoped Stage 2 planning/design task only when ready. Define the
first asynchronous-processing requirement and decision criteria before selecting a
work-transfer technology; do not infer a queue, stream, RabbitMQ, Redis, or Outbox
choice from Stage 1.
