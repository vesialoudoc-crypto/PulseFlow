# PLAN 001: Stage 1 Persistence Foundation

**Document type:** PLAN  
**Status:** Completed on 2026-08-16; verified in the [final progress checkpoint](../progress/2026-08-16-011-persistence-foundation-completed.md)

**ADR status:** This document is not an ADR and does not replace ADR 0001 or ADR 0002.

## Purpose

Define the smallest persistence-only vertical slice for Stage 1. This slice establishes and verifies the PostgreSQL persistence foundation before HTTP ingestion, NDJSON parsing, validation, or chunk orchestration is implemented.

This document records the implementation scope and order that were followed. The resulting repository state and final verification are recorded in the [completion checkpoint](../progress/2026-08-16-011-persistence-foundation-completed.md); this plan does not define architecture or application behavior beyond the completed persistence-foundation slice.

## Accepted implementation intentions

- Keep the first implementation inside `PulseFlow.Api`.
- Do not introduce Domain, Application, or Infrastructure assemblies yet.
- Keep persistence responsibilities cohesive and separable so that they can move into assemblies naturally later if real boundaries emerge. Separate assemblies are not a goal of this slice.
- Keep the persistence model separate from the external [Event Contract v1](../contracts/event-ingestion-v1.md) model.
- Name the persistence entity `EventRecord`; do not reuse an HTTP request or contract model as the EF Core entity.
- Use PostgreSQL as the real persistence engine.
- Use EF Core with the Npgsql provider initially.
- Commit EF Core migrations to the repository.
- Store `payload` as PostgreSQL `jsonb`. The persistence round trip preserves the JSON value and does not promise byte-for-byte preservation of source formatting or property order, consistent with Event Contract v1.
- Use a real PostgreSQL instance supplied by Testcontainers for automated persistence integration tests. Do not substitute SQLite, an in-memory provider, or mocked EF behavior.
- Keep Docker Compose outside this slice. It may be added separately later as a convenience for manual local development, but it is not the database used to prove the automated tests in this plan.

The initial table is conceptually:

```text
events
- id
- type
- source
- occurred_at
- received_at
- payload (jsonb)
```

`received_at` is persistence data rather than a field added to the external Event Contract v1 envelope. Concrete CLR types, PostgreSQL types other than the required `jsonb`, key generation, lengths, and constraints should be chosen explicitly while creating the mapping and reviewed before the initial migration is accepted. This plan does not silently fix those details.

## Boundaries for the first slice

The slice proves only that:

1. The PostgreSQL schema can be created from the committed EF Core migration against an empty database.
2. An `EventRecord` can be persisted to PostgreSQL and read back.
3. A representative `jsonb` payload, including nested objects and arrays, survives a semantic round trip.
4. `occurredAt` and `receivedAt` UTC timestamp values survive persistence correctly.

The focused tests may use `PulseFlowDbContext` directly. No repository abstraction is needed to prove this boundary.

## Incremental implementation order

### 1. Add only the required dependencies and test infrastructure

- Add only the EF Core/Npgsql dependencies needed by `PulseFlow.Api` for runtime mapping and design-time migrations.
- Add only the PostgreSQL Testcontainers dependency and supporting test infrastructure needed by `PulseFlow.IntegrationTests`.
- Provide an integration-test fixture that starts an isolated PostgreSQL container, supplies its connection string, and disposes the container reliably.
- Keep database setup migration-driven so the tests exercise the same committed schema evolution mechanism intended for the application.

Verification for this step: the solution restores and builds, and the test infrastructure can start and connect to its PostgreSQL container.

### 2. Add `EventRecord`, `PulseFlowDbContext`, and EF mapping

- Add `EventRecord` inside `PulseFlow.Api` as the persistence representation of an accepted event.
- Add `PulseFlowDbContext` and an explicit mapping from `EventRecord` to the `events` table and its named columns.
- Map `payload` to PostgreSQL `jsonb` without introducing sender-specific payload models.
- Keep the persistence types grouped by responsibility and free of HTTP-model dependencies so they remain straightforward to extract if a genuine assembly boundary appears later.

Verification for this step: the solution builds and EF Core can construct and validate the model for Npgsql.

### 3. Create and commit the initial migration

- Generate the initial EF Core migration from `PulseFlowDbContext`.
- Review the generated migration and model snapshot to confirm that they create only the intended `events` table, columns, key, and `jsonb` payload mapping.
- Commit the migration and model snapshot with the implementation.

Verification for this step: applying migrations to a new PostgreSQL database succeeds without hand-created schema.

### 4. Add focused PostgreSQL integration tests

- Start a clean PostgreSQL instance through Testcontainers.
- Apply the committed EF Core migrations.
- Persist an `EventRecord` through `PulseFlowDbContext`, then read it back from PostgreSQL.
- Assert the schema creation, entity persistence, semantic `jsonb` payload round trip, and correct UTC handling of `occurredAt` and `receivedAt`.
- Keep these tests focused on persistence; do not route them through an HTTP endpoint or ingestion orchestration.

Verification for this step: the integration tests pass against the containerized PostgreSQL engine.

### 5. Review and record the resulting state

- Run the full repository verification from the root:

  ```powershell
  dotnet build PulseFlow.slnx
  dotnet test PulseFlow.slnx
  pwsh ./scripts/check-project-docs.ps1
  ```

- Review the implementation for accidental coupling or abstractions that the slice does not need.
- Create a new progress checkpoint describing the implementation, migration, PostgreSQL integration-test results, decisions, unresolved items, and next recommended step. Do not rewrite an existing checkpoint except to correct a factual error.
- Update the roadmap only if the Stage 1 status or expected result actually changed. Update architecture or decision documentation only if the implementation accepts architecture beyond this plan.

## Explicit non-goals

This slice does not:

- add `Repository<T>`, `UnitOfWork`, base repositories, or Clean Architecture projects;
- add `IEventChunkStore`; introduce that boundary only when ingestion orchestration has a concrete need for it;
- implement NDJSON parsing, streaming, validation, chunk formation, or ingestion HTTP behavior;
- introduce Command/Handler patterns;
- introduce RabbitMQ, Redis, idempotency, deduplication, or an Outbox;
- choose retry behavior, transaction isolation, or EF Core execution strategy;
- add Docker Compose for automated tests or manual development.

ADR 0001 continues to define later NDJSON batch framing, and ADR 0002 continues to define later chunked PostgreSQL commit semantics. This persistence foundation deliberately stops before either concern needs orchestration code.
