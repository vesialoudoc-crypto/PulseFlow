# Checkpoint: Initial EF Core migration generated and reviewed

**Date:** 2026-08-16

## Starting point

Step 2 of [PLAN 001](../plans/001-stage-1-persistence-foundation.md) had added `EventRecord`, `PulseFlowDbContext`, and the explicit Npgsql mapping for the `events` table. The EF model could be constructed and validated, but the repository contained no migration or model snapshot.

This milestone implements only Step 3 of PLAN 001. The separate user-requested constructor-style rule was also recorded while completing the step.

## What changed

- Added a project working rule that prohibits C# primary constructors and requires traditional constructors declared explicitly inside the type body.
- Replaced the existing primary constructors in `PulseFlowDbContext` and `PostgreSqlInfrastructureTests` with behaviorally equivalent traditional constructors.
- Added `PulseFlowDbContextFactory` for EF design-time discovery. It selects the Npgsql provider without embedding or loading a connection string and does not register the context in `Program.cs`.
- Generated the `InitialCreate` migration with `dotnet-ef` 10.0.10 into `PulseFlow.Api/Persistence/Migrations`.
- Committed the generated migration code, migration designer metadata, and `PulseFlowDbContextModelSnapshot` without manually editing the generated schema.
- Reviewed the generated migration, snapshot, pending-model state, and SQL script for unexpected tables, columns, indexes, defaults, or key generation.

## Resulting repository state

`InitialCreate.Up()` creates only the mapped `events` table:

| Column | PostgreSQL type | Nullability / constraint |
| --- | --- | --- |
| `id` | `uuid` | not null, primary key |
| `type` | `text` | not null |
| `source` | `text` | not null |
| `occurred_at` | `timestamp with time zone` | not null |
| `received_at` | `timestamp with time zone` | not null |
| `payload` | `jsonb` | not null |

The primary-key constraint receives EF's generated name `PK_events`. The migration contains no default values, database UUID functions, indexes, foreign keys, sequences, or additional application tables. `InitialCreate.Down()` only drops `events`.

The generated migration designer and model snapshot describe the same entity, table, columns, types, requiredness, and primary key. EF reports no pending model changes after generation.

When EF renders the migration as SQL, it also creates and updates its standard `__EFMigrationsHistory` infrastructure table. That table is managed by EF migration execution and is not created by `InitialCreate.Up()` or represented as a PulseFlow application entity.

`PulseFlowDbContext` remains unregistered in `Program.cs`, and the repository still contains no application connection-string configuration or persistence round-trip test.

## Verification

Migration generation and review commands run from the repository root:

```powershell
dotnet-ef migrations add InitialCreate `
  --project src/PulseFlow.Api/PulseFlow.Api.csproj `
  --startup-project src/PulseFlow.Api/PulseFlow.Api.csproj `
  --context PulseFlowDbContext `
  --output-dir Persistence/Migrations

dotnet-ef migrations has-pending-model-changes `
  --project src/PulseFlow.Api/PulseFlow.Api.csproj `
  --startup-project src/PulseFlow.Api/PulseFlow.Api.csproj `
  --context PulseFlowDbContext `
  --no-build

dotnet-ef migrations script 0 InitialCreate `
  --project src/PulseFlow.Api/PulseFlow.Api.csproj `
  --startup-project src/PulseFlow.Api/PulseFlow.Api.csproj `
  --context PulseFlowDbContext `
  --no-build
```

Results:

- Migration generation succeeds and creates `InitialCreate`, its designer metadata, and `PulseFlowDbContextModelSnapshot`.
- `migrations has-pending-model-changes` reports that no model changes have been made since the migration.
- The reviewed SQL contains the standard EF migration-history setup and exactly one application table, `events`, with the expected six columns and primary key.

Required repository checks:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds. Both integration tests pass: 2 passed, 0 failed, 0 skipped. `PulseFlow.UnitTests` still reports no discoverable tests.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.

The integration-test run requires access to a running Docker-API-compatible container engine.

## Decisions made

- Kept the generated migration unchanged because its application schema exactly matches the accepted EF mapping.
- Kept the provider-generated `UseIdentityByDefaultColumns` model annotation in the designer and snapshot. It is Npgsql's model-level default for compatible generated numeric columns; `EventRecord.Id` is a `Guid` configured with `ValueGeneratedNever`, so the annotation produces no identity column, default, or UUID generation in this migration.
- Kept EF's generated primary-key constraint name `PK_events`; the accepted mapping requires the primary key but does not require a custom constraint name.
- Added a connectionless Npgsql design-time factory so migration generation remains reproducible before application database registration is introduced.
- Applied the new constructor-style rule to both existing primary-constructor usages so project rules and current code do not contradict each other.

These are bounded tooling and code-style choices. No ADR was required.

## Intentionally unresolved

- How event identifiers are generated and which layer assigns them.
- Applying the committed migration through automated persistence-test setup.
- PostgreSQL persistence round-trip coverage for JSON payloads and UTC timestamps.
- Application database registration, connection-string configuration, and startup behavior.
- Repositories or an `IEventChunkStore` boundary.
- All HTTP, NDJSON, ingestion, validation, chunking, retry, idempotency, messaging, and Outbox behavior.

## Next recommended step

Implement only Step 4 of PLAN 001: apply the committed migration to a clean PostgreSQL Testcontainers database and add focused `PulseFlowDbContext` persistence round-trip coverage for an opaque nested `jsonb` payload and UTC occurrence and receipt timestamps.
