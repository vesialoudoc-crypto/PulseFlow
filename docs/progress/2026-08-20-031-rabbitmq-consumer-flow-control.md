# Checkpoint: RabbitMQ consumer flow control

**Date:** 2026-08-20

## Starting point

The Stage 2 RabbitMQ async-stream adapter used an unbounded
`System.Threading.Channels` bridge. Its delivery callback copied each RabbitMQ body
and returned immediately, so the broker could send batches faster than the single
parser worker and PostgreSQL persistence path could process them.

## What changed

- Set RabbitMQ per-consumer QoS before subscription with a prefetch count of 1.
- Replaced the unbounded bridge with a bounded channel of capacity 1.
- Kept each worker sequential: it receives one delivery, persists it through the
  existing handler, then manually acknowledges it.
- Made the `IngestionBatchDelivery` constructor internal. The acknowledgement
  delegate remains an adapter detail; the parser worker only reads the body and calls
  `AcknowledgeAsync`.
- Added a real-RabbitMQ integration test that leaves the first delivery
  unacknowledged and verifies that the next delivery stays on the broker.
- Updated the active ingestion architecture documentation.

## Resulting repository state

Each `EventParserConsumer` worker still owns a separate RabbitMQ consumer channel.
That channel now has one broker delivery in flight at most, and its async-stream bridge
can retain one copied body at most. The worker processes deliveries sequentially.
The application still uses one RabbitMQ connection, one publisher channel, and one
consumer channel per worker.

Manual acknowledgement still happens only after persistence succeeds. This flow-control
change does not define retry, requeue, dead-letter, poison-message, Outbox,
idempotency, or delivery guarantees.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results at this checkpoint:

- `dotnet build PulseFlow.slnx -warnaserror` succeeded with 0 warnings and 0 errors.
- The focused real-RabbitMQ flow-control integration test passed.
- `dotnet test PulseFlow.slnx` passed: 106 passed, 0 failed, 0 skipped (68 unit and
  38 integration tests).
- `./scripts/check-project-docs.ps1` completed successfully.

## Decisions made

- A prefetch count and bridge capacity of 1 are conservative Stage 2 flow control.
  They bound copied-batch memory and match the existing one-at-a-time worker behavior.
- No ADR is required because this is a bounded-resource correction within the accepted
  Stage 2 RabbitMQ ownership and manual-acknowledgement design.

## Intentionally unresolved

- Retry, requeue, dead-letter, poison-message, and final acknowledgement policies.
- Outbox, idempotency, deduplication, and delivery guarantees.
- Batch-status/result storage and public status/query APIs.
- Redis, authentication, load testing, performance targets, and deployment topology.

## Next recommended step

Define the smallest Stage 3 reliability decision and its verifiable failure scenario
without selecting retry, requeue, DLQ, Outbox, or idempotency mechanisms in advance.
