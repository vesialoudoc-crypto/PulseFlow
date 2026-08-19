# Checkpoint: Terminal rejection and dead-lettering for batch failures

**Date:** 2026-08-20

## Starting point

Stage 2 accepted raw NDJSON batches into RabbitMQ and acknowledged a delivery only
after parsing, validation, and chunked PostgreSQL persistence completed. An unexpected
processing failure was logged but left unacknowledged, with no terminal broker state.
The Stage 3 reliability decision defined zero automatic retries with terminal broker
retention for this failure path.

## What changed

- Added `RejectAsync` to the application-facing `IngestionBatchDelivery` abstraction.
  Its acknowledgement and rejection delegates remain private adapter details.
- Changed `EventParserConsumer` so an unexpected processing exception is logged and
  then terminally rejects exactly that delivery. Cancellation during host shutdown
  still propagates as normal shutdown and is not rejected.
- Added a durable direct dead-letter exchange, a durable dead-letter queue, and an
  explicit dead-letter routing key derived from `RabbitMq:QueueName`.
- Declared the durable main queue with the corresponding `x-dead-letter-exchange` and
  `x-dead-letter-routing-key` arguments. No retry queue, TTL, redrive, prefetch, or
  bounded channel was added.
- Added focused unit tests for acknowledgement on success, terminal rejection on an
  unexpected persistence failure, no terminal rejection on cancellation, and the
  unchanged malformed/contract-invalid record behavior.
- Added a real RabbitMQ and PostgreSQL integration test. It causes a test-only
  persistence failure, reads the failed raw batch from the configured dead-letter
  queue, verifies the main queue is empty, and verifies a later healthy batch persists.
- Added [ADR 0010](../decisions/0010-dead-letter-unexpected-batch-processing-failures.md)
  and updated the active architecture and roadmap documentation.

## Resulting repository state

The selected unexpected-failure path is:

```text
unexpected parsing or PostgreSQL persistence failure
    -> log error
    -> reject one delivery with requeue = false
    -> RabbitMQ dead-letter exchange
    -> durable dead-letter queue
```

Successful processing still follows `handler completes -> acknowledge`. Malformed
NDJSON and contract-invalid records remain normal record-level outcomes in the existing
handler, so they do not dead-letter the whole batch.

The configured main queue is now declared with application-owned dead-letter arguments.
RabbitMQ rejects an incompatible redeclaration of an existing Stage 2 queue. The
application does not delete, purge, or silently recreate queues; a developer must
manually recreate an existing local queue before using this topology. RabbitMQ policies
would be the preferred mutable production mechanism, but policy automation is outside
this slice.

This does not establish end-to-end no-loss, at-least-once, or exactly-once delivery.
RabbitMQ dead-letter republishing can fail depending on broker topology and
availability. Earlier PostgreSQL chunks may also have committed before a later chunk
fails. Replay, retry, redrive, idempotency, and deduplication remain undefined.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 107 passed, 0 failed, 0 skipped (69 unit and 38
  integration tests).
- Documentation validation completed successfully through Windows PowerShell. The
  requested `pwsh` executable is unavailable in this environment.

## Decisions made

- Unexpected batch processing failure uses terminal rejection without requeue and
  durable dead-letter retention; see [ADR 0010](../decisions/0010-dead-letter-unexpected-batch-processing-failures.md).
- Application-owned dead-letter queue arguments are used for the current small local
  topology. Incompatible existing queues fail explicitly rather than losing queued
  data through automatic migration.

## Intentionally unresolved

- Automatic retry, requeue, retry counters, delay or backoff, and automatic DLQ
  redrive.
- Idempotency, deduplication, Outbox, and any end-to-end delivery guarantee.
- RabbitMQ prefetch and other broker-side flow control.
- Batch-status/result storage and public status/query APIs.
- Redis, authentication, load testing, performance targets, and deployment topology.

## Next recommended step

Define the next smallest Stage 3 scenario before adding any retry or flow-control
mechanism. A possible next decision is the investigation and manual redrive boundary
for a dead-lettered batch, but it must first define behavior when earlier PostgreSQL
chunks may already exist.
