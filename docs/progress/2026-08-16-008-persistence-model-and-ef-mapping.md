# Checkpoint: Persistence model and explicit EF mapping implemented

**Date:** 2026-08-16

## Starting point

Step 1 of [PLAN 001](../plans/001-stage-1-persistence-foundation.md) had added the EF Core, Npgsql, and Testcontainers dependencies and a PostgreSQL connectivity smoke test. `PulseFlow.Api` did not yet contain a persistence entity, `DbContext`, or entity mapping. No migration or application database configuration existed.

This milestone implements only Step 2 of PLAN 001.

## What changed

- Added `EventRecord` under `PulseFlow.Api/Persistence/Events` as a persistence-only representation with `Guid Id`, string metadata and opaque JSON payload properties, and `DateTime` occurrence and receipt timestamps.
- Added `EventRecordConfiguration` implementing `IEntityTypeConfiguration<EventRecord>` and explicitly mapped the entity to the PostgreSQL `events` table.
- Added `PulseFlowDbContext`, exposed `DbSet<EventRecord> EventRecords`, and applied `EventRecordConfiguration` from `OnModelCreating`.
- Added a focused model-metadata test that constructs the Npgsql EF model and verifies the table, primary key, named columns, PostgreSQL types, requiredness, and disabled key value generation.
- Added a direct `Microsoft.EntityFrameworkCore.Relational` 10.0.10 reference to `PulseFlow.Api`. The new configuration directly uses relational mapping APIs, and pinning the reference aligns the consumer runtime graph with EF Core 10.0.10 instead of allowing the Npgsql provider's lower compatible bound, 10.0.4, to produce an assembly-version conflict warning.

## Resulting repository state

`PulseFlow.Api` now contains an explicit persistence model with this mapping:

| CLR property | Table column | PostgreSQL type | Constraint |
| --- | --- | --- | --- |
| `Id` | `id` | `uuid` | primary key, not null |
| `Type` | `type` | `text` | not null |
| `Source` | `source` | `text` | not null |
| `OccurredAt` | `occurred_at` | `timestamp with time zone` | not null |
| `ReceivedAt` | `received_at` | `timestamp with time zone` | not null |
| `PayloadJson` | `payload` | `jsonb` | not null |

`OccurredAt` and `ReceivedAt` remain `DateTime` values and use Npgsql's PostgreSQL `timestamp with time zone` mapping for UTC instants. `PayloadJson` remains opaque JSON text; no payload-specific POCO was introduced. `Type` and `Source` have no configured maximum length.

Automatic value generation for `Id` is explicitly disabled so that EF Core does not silently adopt its Guid primary-key generator. No database default or alternative key-generation strategy has been selected.

The model is not registered with the application service collection and has no connection-string configuration. There is still no migration, repository, ingestion behavior, or HTTP-model dependency in the persistence types.

## Verification

Run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds. Both integration tests pass: 2 passed, 0 failed, 0 skipped. The model-metadata test proves that EF Core can construct and validate the explicit mapping for Npgsql; the existing PostgreSQL Testcontainers connectivity test also passes. `PulseFlow.UnitTests` still reports no discoverable tests.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.

The integration-test run requires access to a running Docker-API-compatible container engine.

## Decisions made

- Used the requested `DateTime` properties with explicit `timestamp with time zone` columns. No `DateTimeOffset` or value converter was introduced.
- Disabled EF value generation for `Id` to preserve the intentionally unresolved key-generation policy.
- Used `null!` initializers for required persistence strings so the persistence entity remains compatible with EF materialization while nullable-reference analysis matches the non-null column mapping.
- Named the exposed set `EventRecords`; the physical table name remains independently and explicitly mapped to `events`.
- Pinned the directly used EF Core relational package to the existing EF Core 10.0.10 version to remove the runtime graph mismatch described above.

These are bounded persistence-model and dependency-alignment choices. No ADR was required.

## Intentionally unresolved

- How event identifiers are generated and which layer assigns them.
- The initial migration and model snapshot.
- Applying migrations to a clean PostgreSQL database.
- PostgreSQL persistence round-trip coverage for JSON payloads and UTC timestamps.
- Application database registration, connection-string configuration, and startup behavior.
- Repositories or an `IEventChunkStore` boundary.
- All HTTP, NDJSON, ingestion, validation, chunking, retry, idempotency, messaging, and Outbox behavior.

## Next recommended step

Implement only Step 3 of PLAN 001: generate and review the initial EF Core migration and model snapshot. Confirm that they contain only the intended `events` table and explicit columns, and keep application registration and ingestion behavior out of that step.
