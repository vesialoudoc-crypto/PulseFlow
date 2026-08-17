# Checkpoint: Event chunk store implemented

**Date:** 2026-08-17

## Starting point

[PLAN 002 Steps 1 and 2](2026-08-17-013-ndjson-record-reader.md) had implemented
the valid `EventEnvelope` boundary and the asynchronous NDJSON record reader. The
repository already contained the separate `EventRecord`, explicit EF Core mapping,
committed migration, `PulseFlowDbContext`, and real PostgreSQL Testcontainers
infrastructure from PLAN 001.

There was no persistence boundary accepting valid envelopes, no translation from
`EventEnvelope` to `EventRecord`, and no accepted policy for generating the
persistence-only identifier and receipt time. This milestone implements only
[PLAN 002 Step 3](../plans/002-stage-1-ingestion-pipeline.md).

## What changed

- Added [ADR 0005](../decisions/0005-generate-event-persistence-metadata-in-application.md),
  accepting direct application generation of persistence-only IDs with
  `Guid.NewGuid()` and receipt times with `DateTime.UtcNow` during translation.
- Added the narrow `IEventChunkStore` ingestion persistence boundary:

  ```csharp
  Task StoreAsync(
      IReadOnlyCollection<EventEnvelope> events,
      CancellationToken cancellationToken = default);
  ```

- Added `EfCoreEventChunkStore`, which depends only on `PulseFlowDbContext`, translates
  the complete supplied collection directly to `EventRecord` values, adds them to the
  context, and invokes `SaveChangesAsync` once.
- Added one focused integration test using the existing real PostgreSQL
  Testcontainers infrastructure and committed migration. It supplies two valid
  envelopes created through the existing validator and queries the stored rows with a
  fresh `PulseFlowDbContext`.
- Updated PLAN 002 and the accepted ingestion architecture to mark Step 3 implemented
  and describe the exact boundary and mapping. PLAN 002 and Stage 1 remain in
  progress; the roadmap status and expected result did not change.

## Resulting repository state

The implemented persistence boundary is:

```text
IEventChunkStore
    ↓
EfCoreEventChunkStore
    ↓
PulseFlowDbContext
    ↓
PostgreSQL
```

`IEventChunkStore` accepts one already-formed `IReadOnlyCollection<EventEnvelope>` and
optional cancellation. It does not expose `EventRecord`, `DbContext`, EF Core entity
APIs, transactions, or HTTP types. The caller remains responsible for forming chunks;
the store owns translation and persistence of exactly the supplied collection.

The direct mapping is:

```text
EventRecord.Id          <- Guid.NewGuid()
EventRecord.Type        <- EventEnvelope.Type
EventRecord.Source      <- EventEnvelope.Source
EventRecord.OccurredAt  <- EventEnvelope.OccurredAt
EventRecord.ReceivedAt  <- DateTime.UtcNow
EventRecord.PayloadJson <- EventEnvelope.Payload JSON via GetRawText()
```

The existing PostgreSQL `jsonb` mapping preserves payload JSON semantics rather than
its original formatting or property order. A successful `SaveChangesAsync` completion
is the durability boundary for the supplied chunk. Cancellation and persistence
failures propagate without retry or HTTP interpretation.

The integration test verifies one logical persistence result: both rows exist; their
generated IDs are non-empty and distinct; contract fields map correctly; nested
payload JSON is semantically preserved; and each `ReceivedAt` is UTC within the
interval around the store operation.

There is still no `IngestEventsHandler`, chunk formation, accepted/rejected
accounting, application database wiring, DI registration, HTTP endpoint, or public
ingestion response.

## Verification

Required commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds with 58 unit tests and 29 integration tests
  passed, 0 failed, and 0 skipped. The integration suite used the existing real
  PostgreSQL Testcontainers fixture and local Docker engine.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.
- The sandbox-restricted initial restore and build attempts could not reach NuGet;
  the exact build and test commands succeeded after approved network access was
  granted. No package or project dependency changed.

## Decisions made

- Accepted [ADR 0005](../decisions/0005-generate-event-persistence-metadata-in-application.md).
  Persistence-only IDs and receipt times are generated directly by the application
  during translation. Deterministic control can be introduced later if it becomes a
  real requirement.
- No additional architectural decision became unavoidable. In particular, this step
  introduced no clock, ID generator, mapper, factory, generic repository, unit of
  work, retry policy, execution strategy, or custom transaction abstraction.

## Intentionally unresolved

- Chunk capacity and chunk formation.
- Accepted, rejected, and not-committed accounting.
- The application-level outcome when chunk persistence fails.
- The HTTP method, route, media type, response contract, and persistence-failure
  behavior.
- PostgreSQL retry behavior and EF Core execution strategy.
- Idempotency and deduplication.
- DI registration, application database configuration, and production migration
  execution.
- Unknown top-level Event Contract property behavior and record/upload limits.

## Next recommended step

Implement only PLAN 002 Step 4: add `IngestEventsHandler`, validated configurable
chunk formation, and non-HTTP accounting. Before completing that step, define how a
chunk persistence failure preserves earlier accepted counts and represents the
failing valid-but-not-committed chunk. Do not add the HTTP endpoint, application DI
wiring, retry, or idempotency in that step.
