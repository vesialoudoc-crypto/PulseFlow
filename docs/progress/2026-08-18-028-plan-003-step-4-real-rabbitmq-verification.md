# Checkpoint: PLAN 003 Step 4 real RabbitMQ verification

**Date:** 2026-08-18

## Starting point

PLAN 003 Steps 1 through 3 had implemented the asynchronous acceptance boundary and
the parser consumer. `PulseFlow.Api` published the raw NDJSON batch to RabbitMQ and
returned HTTP 202 after publisher confirmation. `EventParserConsumer` reused the
existing NDJSON parsing, Event Contract v1 validation, and PostgreSQL persistence path,
but complete-path verification used a RabbitMQ-facing fake and parser-consumer capacity
was fixed at one instance.

## What changed

- Added `Testcontainers.RabbitMq` integration-test infrastructure using a real
  `rabbitmq:4.1-alpine` container alongside the existing PostgreSQL container
  infrastructure.
- Added a real end-to-end integration test that starts PostgreSQL, RabbitMQ, and the
  real application; posts an `application/x-ndjson` batch; verifies HTTP 202; and
  verifies the valid events in PostgreSQL.
- Each real-broker application test receives a generated unique queue name, avoiding
  queue consumption interference between repeated or parallel test runs.
- The PostgreSQL assertion uses bounded polling with a 15-second timeout and a
  100-millisecond polling interval. It has no arbitrary long sleep.
- Added `RabbitMq:ConsumerCount`, with a default of 1 and startup validation requiring
  a value greater than zero.
- Application composition now registers one `EventParserConsumer` per configured
  consumer count. Each consumer owns a separate `RabbitMqConsumerChannel`; all of
  those channels and the publisher channel use the application's single RabbitMQ
  connection.
- Added a focused real-broker structural test proving that `ConsumerCount = 2` results
  in two parser consumers on the same queue and connection with distinct consumer
  channels.
- Updated the PLAN 003 Step 4 status, roadmap, and active ingestion architecture
  documentation to describe verified capacity configuration and real-broker testing.

## Resulting repository state

The verified implemented path is:

```text
POST /api/events
    -> PulseFlow.Api
    -> RabbitMQ
    -> EventParserConsumer
    -> NdjsonRecordReader
    -> IngestEventsHandler
    -> PostgreSQL
```

HTTP 202 still means that RabbitMQ confirmed publication of the exact raw NDJSON batch;
it does not mean that a record has been parsed, validated, or persisted. Parser-consumer
capacity is independently configurable by `RabbitMq:ConsumerCount`. It controls
competing consumer capacity in one application process, not HTTP API instance count.
RabbitMQ distributes deliveries between consumers of the configured queue; this change
does not manually choose a delivery order or make a performance claim.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 104 passed, 0 failed, 0 skipped (68 unit and 36
  integration tests).
- The required documentation-validation script completed successfully through Windows
  PowerShell. The requested `pwsh` executable was unavailable in this environment.

## Decisions made

- `ConsumerCount` is a small bounded capacity configuration for parser consumers. It
  does not select a production routing topology, a connection pool, a channel pool, or
  a scheduler, so no ADR was required.
- The real-broker test uses a generated queue name as test isolation only. The
  production default queue configuration and topology remain unchanged.

## Intentionally unresolved

- Retry, requeue, dead-letter, poison-message, and final acknowledgement policies.
- Outbox, idempotency, deduplication, and delivery guarantees.
- Batch-status/result storage and public status/query APIs.
- Redis, authentication, load testing, performance targets, and deployment topology.
- PLAN 003 Step 5 review remains unimplemented.

## Next recommended step

Implement only PLAN 003 Step 5: review the completed Stage 2 slice against its accepted
contracts and documentation, without expanding it into reliability or load-testing work.
