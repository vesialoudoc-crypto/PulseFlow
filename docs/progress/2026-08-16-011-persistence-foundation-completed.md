# Checkpoint: PLAN 001 persistence foundation completed

**Date:** 2026-08-16

## Starting point

Step 4 of [PLAN 001](../plans/001-stage-1-persistence-foundation.md) had applied the committed `InitialCreate` migration to a clean Testcontainers PostgreSQL database and verified an `EventRecord` write/read round trip, semantic `jsonb` preservation, and UTC timestamp preservation. PLAN 001 still had its original proposed-and-unimplemented status, and its Step 5 review had not been recorded.

This milestone completes only Step 5 of PLAN 001. It reviews and records the persistence-foundation slice without adding application functionality.

## What changed

- Reviewed the dependencies, PostgreSQL Testcontainers fixture, persistence model, explicit EF Core mapping, design-time context factory, committed migration artifacts, application startup, configuration files, and focused integration tests against PLAN 001 Steps 1–4.
- Confirmed that the migration, designer metadata, and model snapshot are tracked and describe only the intended `events` table and its six required columns.
- Confirmed that the real PostgreSQL persistence test applies the committed migration before using separate contexts for the write and read.
- Confirmed that the representative payload covers nested objects and arrays and is compared as a JSON value rather than as source text.
- Confirmed that both occurrence and receipt timestamps are written as UTC values at PostgreSQL-supported microsecond precision and are read back with the same instants and `DateTimeKind.Utc`.
- Reviewed source and test projects for prohibited concerns and found no HTTP ingestion, NDJSON parsing, application `DbContext` registration, connection-string configuration, repository or `IEventChunkStore`, chunking, RabbitMQ, Redis, idempotency, Outbox, or retry implementation.
- Marked PLAN 001 as completed and replaced its obsolete pre-implementation disclaimer with a link to this checkpoint.

No source code, tests, migration artifacts, product boundaries, architecture documents, ADRs, or roadmap entries changed during Step 5.

## Resulting repository state

PLAN 001 is fully satisfied:

1. `PulseFlow.Api` references EF Core, EF Core design-time and relational support, and the Npgsql EF Core provider. `PulseFlow.IntegrationTests` directly references Npgsql and `Testcontainers.PostgreSql` for the APIs it uses.
2. `PostgreSqlFixture` starts an isolated pinned PostgreSQL container, exposes its generated connection string, and disposes the container asynchronously. A smoke test proves that the database accepts a real Npgsql connection.
3. `EventRecord` is a persistence-only type under `PulseFlow.Api.Persistence.Events`; it is separate from the documented external Event Contract v1 and has no HTTP-model dependency.
4. `PulseFlowDbContext` applies `EventRecordConfiguration`, which explicitly maps the entity to `events` and maps `id`, `type`, `source`, `occurred_at`, `received_at`, and `payload`. The payload column is required `jsonb`; both timestamps are required `timestamp with time zone` columns.
5. The tracked `InitialCreate` migration, designer, and model snapshot create only the intended application table and columns. The primary key is application-supplied, and the migration introduces no defaults, indexes, foreign keys, sequences, or unrelated application tables.
6. `PulseFlowDbContextPersistenceTests` uses the Testcontainers connection string and `Database.MigrateAsync()` to apply the committed migration to a clean real PostgreSQL database.
7. The same test persists an `EventRecord`, disposes the writing context, and reads the row with a new no-tracking context.
8. The test verifies a nested object-and-array payload with semantic JSON-tree equality, consistent with PostgreSQL `jsonb` and Event Contract v1 semantics.
9. The test verifies exact supported UTC instants and `DateTimeKind.Utc` for both `OccurredAt` and `ReceivedAt` after the PostgreSQL round trip.

The supporting separation is proportionate to the slice. `EventRecordConfiguration` provides the required explicit mapping, `PulseFlowDbContextFactory` provides connectionless EF design-time discovery for committed migrations, and `PostgreSqlFixture` owns the container lifecycle. No unused application-layer abstraction or speculative infrastructure was found.

Stage 1 remains **In progress** because the Stage 1 HTTP ingestion, validation, application persistence wiring, and clean-environment application scenario do not yet exist. Its roadmap status and expected result therefore did not change. The Source of Truth also did not change because completing this technical foundation did not alter product boundaries or mandatory capabilities.

## Verification

Required commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds: 3 integration tests passed, 0 failed, 0 skipped. `PulseFlow.UnitTests` still reports no discoverable tests.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.

The integration-test run requires access to a running Docker-API-compatible container engine. The successful test run used a real PostgreSQL 18.4 Testcontainers instance.

## Decisions made

- Accepted PLAN 001 as completed because every Step 1–4 artifact and Step 5 review criterion is present and the required verification succeeds.
- Kept Stage 1 marked **In progress** because completing this persistence-only foundation does not complete the Stage 1 API result.
- Made no architecture or ADR change. The review accepted no decision beyond the bounded choices already recorded while implementing PLAN 001.

## Intentionally unresolved

- How event identifiers are generated and which layer assigns them.
- Application `DbContext` registration, connection-string configuration, and startup migration behavior.
- Whether and where a repository or `IEventChunkStore` boundary becomes useful for ingestion orchestration.
- The remaining HTTP contract, validation, response, and batch-limit decisions.
- NDJSON reading, parsing, validation, chunk formation, ingestion orchestration, and partial-success HTTP behavior.
- PostgreSQL retry behavior, execution strategy, and transaction isolation.
- Idempotency, deduplication, messaging, Outbox, Redis, and asynchronous processing concerns assigned to later slices or stages.

## Next recommended step

In a separate future task, select and define the next smallest Stage 1 slice needed to move from the verified persistence foundation toward the Stage 1 vertical result. Do not infer application database wiring or ingestion behavior from PLAN 001; resolve only the decisions required by that next bounded slice before implementing it.
