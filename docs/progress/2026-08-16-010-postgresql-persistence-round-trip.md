# Checkpoint: PostgreSQL persistence round trip verified

**Date:** 2026-08-16

## Starting point

Step 3 of [PLAN 001](../plans/001-stage-1-persistence-foundation.md) had committed and reviewed the initial EF Core migration for the `events` table. The existing Testcontainers fixture could start PostgreSQL and expose its connection string, and the EF model mapping had automated coverage, but no test applied the committed migration or exercised persistence against PostgreSQL.

This milestone implements only Step 4 of PLAN 001.

## What changed

- Added a focused `PulseFlowDbContextPersistenceTests` integration-test class using the existing `PostgreSqlFixture` and its Testcontainers PostgreSQL connection string.
- Created `PulseFlowDbContext` options directly in the test with the Npgsql provider.
- Applied the committed EF Core migrations with `Database.MigrateAsync()` before writing data.
- Persisted one representative `EventRecord` and committed it with `SaveChangesAsync()`.
- Disposed the writing context, created a fresh context, and queried the record with no tracking so the assertion cannot be satisfied by EF's change tracker.
- Verified the identifier, event type, source, nested JSON payload, occurrence timestamp, and receipt timestamp after the database round trip.
- Compared parsed JSON trees with `JsonNode.DeepEquals` so the test asserts JSON semantics rather than whitespace or object-property order.
- Kept timestamp inputs in UTC and at PostgreSQL's microsecond precision, then asserted both the expected instant and `DateTimeKind.Utc` after reading.

The PostgreSQL fixture, committed migration, application startup, and persistence model did not require changes.

## Resulting repository state

The integration-test project now has three focused tests:

1. the Testcontainers PostgreSQL instance accepts a database connection;
2. the EF Core model contains the explicit `EventRecord` mapping;
3. the committed migration creates a usable schema and an `EventRecord` survives a real PostgreSQL write/read round trip.

The persistence round-trip test uses `PulseFlowDbContext` directly. It does not introduce a repository, register the context in `Program.cs`, add application connection-string configuration, or implement ingestion behavior.

The successful round trip demonstrates that:

- the committed migration can be applied to a clean Testcontainers PostgreSQL database;
- the application-supplied UUID is retained;
- `type` and `source` text values are retained;
- a nested object-and-array payload is accepted by the `jsonb` column and remains semantically equivalent after reading;
- UTC occurrence and receipt timestamps with microsecond fractions retain their expected instants and are materialized by Npgsql as UTC `DateTime` values.

## Verification

Focused test run from the repository root:

```powershell
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj `
  --filter FullyQualifiedName~PulseFlowDbContextPersistenceTests
```

Result:

- succeeds against the Testcontainers PostgreSQL 18.4 instance: 1 passed, 0 failed, 0 skipped.

Required repository checks:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds: 3 integration tests passed, 0 failed, 0 skipped. `PulseFlow.UnitTests` still reports no discoverable tests.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.

The integration-test runs require access to a running Docker-API-compatible container engine.

## Decisions made

- Reused `PostgreSqlFixture` without extending it because its connection string is sufficient for direct EF Core context creation.
- Kept migration application, write, context replacement, read, and assertions in one focused test so the complete persistence boundary is explicit.
- Used parsed JSON-tree equality because PostgreSQL `jsonb` is not a textual-format preservation contract.
- Used UTC inputs with microsecond-aligned fractions so the test verifies timestamp handling without asserting precision PostgreSQL does not provide.

These are test-scope implementation choices and do not require an ADR.

## Unexpected PostgreSQL or Npgsql behavior

No unexpected behavior was observed. The migration applied without modification, Npgsql accepted the UTC `DateTime` values for `timestamp with time zone`, and it materialized both values with `DateTimeKind.Utc`. The test intentionally does not depend on the textual formatting of the JSON returned from the `jsonb` column.

## Intentionally unresolved

- How event identifiers are generated and which layer assigns them.
- Application database registration, connection-string configuration, and startup migration behavior.
- Repositories or an `IEventChunkStore` boundary.
- All HTTP, NDJSON, ingestion, validation, chunking, retry, idempotency, messaging, and Outbox behavior.
- PostgreSQL retry behavior and transaction isolation choices.

## Next recommended step

Complete only the review-and-record activity in Step 5 of PLAN 001, then define the next smallest Stage 1 task separately. Do not introduce application database wiring or ingestion behavior without a bounded plan that resolves only the decisions required by that next task.
