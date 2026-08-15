# Checkpoint: Chunked PostgreSQL persistence accepted

**Date:** 2026-08-15

## Starting point

Stage 1 had accepted Event Contract v1 records and NDJSON batch framing with independent record parsing semantics. The ingestion endpoint and persistence path were not implemented, and PostgreSQL transaction strategy and durable acceptance boundary remained unresolved.

## What changed

- Added [ADR 0002](../decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md), comparing whole-upload persistence, one transaction per record, and configurable chunked persistence.
- Accepted sequential NDJSON reading with record-level parsing and envelope validation.
- Accepted configurable in-memory chunks persisted one chunk per PostgreSQL transaction.
- Defined successful PostgreSQL commit as the Stage 1 durability and acceptance boundary.
- Updated the ingestion architecture and Stage 1 roadmap while leaving implementation-specific and reliability choices deferred.

## Resulting state

The accepted, but not implemented, Stage 1 pipeline is: NDJSON stream, record parse, record validation, configurable in-memory chunk, PostgreSQL transaction, then commit equals acceptance.

Malformed and invalid records do not enter persistence chunks. Payload contents remain opaque. Database constraints are an additional integrity layer rather than a substitute for boundary validation.

Records in memory are not accepted. Records in a committed chunk are accepted; records in an uncommitted chunk are not. Later failures do not roll back earlier committed chunks.

No endpoint, parser, validation code, database schema, migration, persistence implementation, retry behavior, or concrete chunk size has been added or selected.

## Verification

Run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- the solution builds with no warnings or errors;
- the test command succeeds and reports no discoverable tests in either test project;
- the documentation check completes without warnings;
- the roadmap, architecture document, ADR 0002, and latest checkpoint consistently describe commit-based acceptance and deferred implementation details.

## Decisions made

- NDJSON records are read sequentially and parsed and validated independently.
- Event Contract v1 envelope validation occurs at the ingestion boundary; opaque payload contents are not interpreted.
- Only valid records enter configurable in-memory persistence chunks.
- Each chunk is persisted in one PostgreSQL transaction.
- A record is accepted only after the transaction containing its chunk commits successfully.
- Earlier committed chunks remain accepted if a later chunk fails.
- Database constraints supplement but do not replace ingestion validation.
- Chunk size is configurable and external-contract-neutral; no concrete value is accepted.

See [ADR 0002](../decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md) for alternatives and consequences.

## Still unresolved

- Concrete chunk size and its configuration source.
- HTTP response and status code for database failure during an upload.
- PostgreSQL retry strategy, EF Core execution strategy, and transaction isolation level.
- Database schema, data-access implementation, and migration strategy.
- Validation library or framework.
- Idempotency, deduplication, and client retry policy.
- RabbitMQ, Redis, polling, and queue or stream technology.
- Compression, upload limits, authentication, and authorization.

## Next recommended step

Define only the remaining HTTP and database contract details needed to implement and test the smallest Stage 1 NDJSON ingestion slice. Do not infer retry, idempotency, messaging, isolation, or concrete chunk-size choices from the accepted persistence boundary.
