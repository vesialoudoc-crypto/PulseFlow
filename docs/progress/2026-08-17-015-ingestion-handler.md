# Checkpoint: Ingestion handler implemented

**Date:** 2026-08-17

## Starting point

[PLAN 002 Steps 1 through 3](2026-08-17-014-event-chunk-store.md) had
implemented the valid `EventEnvelope` boundary, asynchronous NDJSON record reader,
and durable `IEventChunkStore` boundary with its EF Core implementation. There was no
orchestration connecting those boundaries, no chunk formation, and no accepted or
rejected aggregate accounting.

PLAN 002 Step 4 still proposed partial failure accounting or a custom use-case
exception when a later chunk failed. Before implementation, a simpler behavior was
accepted: persistence failures propagate normally and produce no handler result, even
though earlier chunks may already be durable.

## What changed

- Added [ADR 0006](../decisions/0006-keep-ingestion-handler-failure-propagation-simple.md),
  accepting an `Accepted`/`Rejected` normal-completion result and unchanged
  persistence-exception propagation without a partial result or custom exception.
- Added `IngestEventsHandler` with this constructor and public method:

  ```csharp
  public IngestEventsHandler(
      EventEnvelopeValidator validator,
      IEventChunkStore eventChunkStore,
      int chunkCapacity)

  public Task<IngestEventsResult> HandleAsync(
      IAsyncEnumerable<NdjsonRecordResult> records,
      CancellationToken cancellationToken = default)
  ```

- Required `chunkCapacity > 0` at construction and rejected non-positive values with
  `ArgumentOutOfRangeException`.
- Added the non-HTTP result model with exactly two public get-only properties:

  ```csharp
  public sealed class IngestEventsResult
  {
      public int Accepted { get; }
      public int Rejected { get; }
  }
  ```

- Added sequential validation and bounded chunk formation. Malformed and
  contract-invalid records increment one rejected total. Valid envelopes are stored
  in ordered full chunks, followed by one non-empty final partial chunk. Accepted is
  incremented only after each store call completes successfully.
- Added focused handler unit tests with a recording fake `IEventChunkStore`. They
  cover mixed malformed/contract-invalid/valid input, ordered chunks at capacities
  two and three, final partial flush, accepted/rejected totals, later store failure,
  processing stop, unchanged exception propagation, earlier successful chunks, and
  invalid capacities.
- Updated PLAN 002, the ingestion architecture, and the Stage 1 roadmap clarification
  to describe the implemented Step 4 behavior. PLAN 002 and Stage 1 remain in
  progress; their completion criteria and the roadmap stage status did not change.

## Resulting repository state

The implemented orchestration is:

```text
IAsyncEnumerable<NdjsonRecordResult>
    ↓
IngestEventsHandler
    ├── EventEnvelopeValidator
    ├── List<EventEnvelope> current chunk
    └── IEventChunkStore
    ↓
IngestEventsResult on normal completion
```

The handler depends only on the validator, chunk store, and direct integer capacity.
It does not own or construct `NdjsonRecordReader`, and it has no dependency on HTTP,
EF Core, `PulseFlowDbContext`, options, configuration binding, or a generic result
framework. It retains only the current record, current chunk, and counters rather
than materializing the complete input sequence.

On a successful store call, the complete supplied chunk is counted as accepted. If a
later call throws, accepted accounting is not advanced for that failing chunk, the
same exception propagates, enumeration stops, and no `IngestEventsResult` is returned.
Earlier successful chunks remain durable. This is intentionally a partially committed
overall failure and not request-level atomicity.

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
  passed, 0 failed, and 0 skipped. The integration suite used the existing real
  PostgreSQL Testcontainers fixture and local Docker engine.
- `pwsh ./scripts/check-project-docs.ps1` cannot start because `pwsh` is not installed
  in the current environment. Running the same script with the available Windows
  PowerShell host as `& .\scripts\check-project-docs.ps1` succeeds without warnings.

## Decisions made

- Accepted [ADR 0006](../decisions/0006-keep-ingestion-handler-failure-propagation-simple.md).
  Normal completion has only accepted and rejected totals. Persistence failures and
  cancellation propagate without a result; earlier committed chunks remain durable.
- No additional architectural decision became unavoidable. In particular, this step
  introduced no options class, configuration binding, chunk-size value object,
  command bus, mediator, pipeline, factory, strategy, custom persistence exception,
  partial-outcome hierarchy, retry, or idempotency mechanism.

## Intentionally unresolved

- The HTTP method, route, media type, response contract, and exact HTTP behavior when
  persistence fails after zero or more earlier chunks have committed.
- The production chunk capacity and its configuration binding.
- Retry, idempotency, deduplication, and uncertain client outcomes after partial
  persistence.
- Application database configuration, DI registration, and production migration
  execution.
- PostgreSQL retry behavior and EF Core execution strategy.
- Unknown top-level Event Contract property behavior and record/upload limits.

## Next recommended step

Implement only PLAN 002 Step 5: accept the remaining HTTP contract decision, add the
thin NDJSON ingestion endpoint, and wire the already implemented reader, validator,
handler, chunk store, and database context through application configuration and DI.
Do not add end-to-end tests assigned to Step 6, database retry, idempotency,
deduplication, messaging, or other later-stage infrastructure in that step.
