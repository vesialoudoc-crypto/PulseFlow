# Checkpoint: Stage 2 messaging boundary refactor and review

**Date:** 2026-08-19

## Starting point

PLAN 003 Steps 1 through 4 had established and verified the asynchronous ingestion
path. However, `Program.cs` created and disposed RabbitMQ.Client connections and
channels, registered those primitives directly in DI, and constructed one hosted
consumer per `RabbitMq:ConsumerCount`. `EventParserConsumer` also depended on a
RabbitMQ-shaped callback abstraction that exposed delivery tags and acknowledgement
operations.

## What changed

- Added `AddIngestionMessaging`, leaving `Program.cs` with one messaging registration.
- Added `RabbitMqConnectionManager`, which owns the single application connection,
  declares the durable queue during hosted-service startup, creates channels, and
  disposes the connection.
- Moved RabbitMQ.Client connection, channel, callback, delivery-tag, and basic
  consume/acknowledgement code into `Ingestion/Messaging/RabbitMq`.
- Changed the application-facing consumer boundary to
  `IIngestionBatchConsumerFactory`, `IIngestionBatchConsumer`, and
  `IAsyncEnumerable<IngestionBatchDelivery>`. The RabbitMQ adapter translates its
  callback into that async stream through `System.Threading.Channels`.
- Changed the publisher to own its confirmation-enabled channel through the connection
  manager. Its channel-access serialization remains an internal detail.
- Changed one hosted `EventParserConsumer` to start `ConsumerCount` workers. Each
  worker receives a separately created consumer and therefore a separate RabbitMQ
  channel; all use the one application connection.
- Updated focused consumer unit/integration tests and the real-broker structural test
  for the new boundary.
- Completed PLAN 003 Step 5 review and updated the roadmap and active ingestion
  architecture documentation to reflect the implemented composition.

## Resulting repository state

The verified path remains:

```text
POST /api/events
    -> PulseFlow.Api
    -> RabbitMQ
    -> EventParserConsumer workers
    -> NdjsonRecordReader
    -> IngestEventsHandler
    -> PostgreSQL
```

The resource arrangement is now:

```text
one application
    -> one RabbitMQ connection
        -> one publisher channel
        -> one consumer channel per configured worker
```

`HTTP 202 Accepted` still means that RabbitMQ confirmed publication of the exact raw
NDJSON batch. It does not mean that any record has been parsed, validated, or
persisted. A successful delivery acknowledgement still occurs only after the existing
handler completes successfully.

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
- Documentation validation completed successfully through Windows PowerShell. The
  requested `pwsh` executable is unavailable in this environment.

## Decisions made

- RabbitMQ.Client resource ownership is an infrastructure concern. This refactor
  makes no new delivery, retry, topology, or scalability guarantee, so no ADR was
  required.
- One hosted service with multiple workers preserves the existing `ConsumerCount`
  capacity meaning while removing application-level registration of RabbitMQ channels.
- PLAN 003 is complete because its documented acceptance boundary, parser path,
  real-broker verification, capacity configuration, and review criteria are present.

## Intentionally unresolved

- Retry, requeue, dead-letter, poison-message, and final acknowledgement policies.
- Outbox, idempotency, deduplication, and delivery guarantees.
- Batch-status/result storage and public status/query APIs.
- Redis, authentication, load testing, performance targets, and deployment topology.

## Next recommended step

Define the smallest Stage 3 reliability decision and its verifiable failure scenario.
Do not select retry, requeue, DLQ, Outbox, or idempotency mechanisms before that
scenario and its acceptance criteria are explicit.
