# Checkpoint: PostgreSQL Testcontainers foundation implemented

**Date:** 2026-08-16

## Starting point

Stage 1 had accepted NDJSON framing and chunked PostgreSQL commit semantics, and [PLAN 001](../plans/001-stage-1-persistence-foundation.md) defined a smaller persistence-foundation sequence. No EF Core, Npgsql, or Testcontainers dependencies were referenced, and neither test project contained discoverable tests.

This milestone implements only Step 1 of PLAN 001. No persistence model, database schema, or ingestion behavior existed at the starting point.

## What changed

- Added EF Core 10.0.10, EF Core design-time tooling 10.0.10, and the Npgsql EF Core provider 10.0.3 to `PulseFlow.Api`.
- Kept the EF Core design-time package private so it will not flow into published application assets.
- Added Npgsql 10.0.3 and `Testcontainers.PostgreSql` 4.14.0 to `PulseFlow.IntegrationTests`.
- Added a reusable xUnit fixture that starts a pinned `postgres:18.4-alpine` container, exposes its generated connection string, and asynchronously disposes the container.
- Added one infrastructure smoke test that opens a real Npgsql connection to the container and verifies that the connection reaches the open state.

## Resulting repository state

`PulseFlow.Api` now has only the dependencies required for the planned EF Core/Npgsql persistence implementation and later migration tooling. It still has no `EventRecord`, `PulseFlowDbContext`, EF mapping, or migration.

`PulseFlow.IntegrationTests` can provision an isolated real PostgreSQL instance through Testcontainers and verify connectivity over the generated host connection string. The fixture does not create a PulseFlow schema, apply migrations, or depend on ingestion behavior.

PLAN 001 remains a plan and is not marked completed. Its Steps 2 through 5 remain unimplemented.

## Verification

Run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with no warnings or errors.
- `dotnet test PulseFlow.slnx` succeeds. The PostgreSQL infrastructure smoke test passes: 1 passed, 0 failed, 0 skipped. `PulseFlow.UnitTests` still reports no discoverable tests.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.

The integration test requires a running Docker-API-compatible container engine. The first run may pull the pinned PostgreSQL and Testcontainers resource-reaper images.

## Decisions made

- Package versions were pinned to current stable .NET 10-compatible releases rather than using floating versions.
- The PostgreSQL test image was pinned to `postgres:18.4-alpine` for repeatable test behavior. This test-only choice does not select a production PostgreSQL version or deployment topology.
- `PulseFlow.IntegrationTests` references Npgsql directly because the smoke test opens a database connection; it does not rely on the API project's transitive dependency graph for a type it uses directly.
- The fixture implements xUnit's existing `IAsyncLifetime` contract directly. No additional Testcontainers xUnit integration package was introduced because one small shared class fixture is sufficient.

These are bounded dependency and test-infrastructure choices. No new ADR was required.

## Intentionally unresolved

- `EventRecord` properties and concrete persistence types.
- `PulseFlowDbContext` and EF mapping.
- The `events` schema, key generation, constraints, and initial migration.
- Persistence round-trip tests for payload and timestamps.
- Application database configuration and startup behavior.
- All HTTP, NDJSON, ingestion, validation, chunking, retry, idempotency, messaging, and Outbox implementation.

## Next recommended step

Implement only Step 2 of PLAN 001: add `EventRecord`, `PulseFlowDbContext`, and explicit EF mapping inside `PulseFlow.Api`. Keep the persistence model separate from the external Event Contract model, and do not create the initial migration until the mapping has been reviewed.
