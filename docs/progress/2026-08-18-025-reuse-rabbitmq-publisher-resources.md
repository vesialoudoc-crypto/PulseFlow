# Checkpoint: Reuse RabbitMQ publisher connection and channel

**Date:** 2026-08-18

## Starting point

PLAN 003 Step 2 had implemented API-to-RabbitMQ asynchronous acceptance, but its
publisher opened and disposed an `IConnection` and `IChannel` for every batch. That
would create avoidable connection churn and did not follow the RabbitMQ client's
intended long-lived connection usage.

## What changed

- Changed `RabbitMqIngestionBatchPublisher` to a singleton application service.
- It now lazily creates and retains one RabbitMQ connection and one channel for the
  application lifetime, disposing both during DI-container shutdown.
- Kept queue declaration during one-time initialization and kept the shared channel's
  publisher confirmations, persistent message properties, and mandatory publishing.
- Added a `SemaphoreSlim` to serialize initialization and publication because the
  shared RabbitMQ channel must not perform concurrent publishing operations.
- Explicitly disabled client automatic recovery. A connection or channel failure still
  propagates and therefore cannot produce HTTP `202`; retries and reconnection remain
  outside this Step 2 slice.
- Updated active architecture documentation to describe the singleton lifetime and
  concurrency boundary.

## Resulting repository state

The HTTP contract is unchanged: raw NDJSON bytes are published and HTTP `202` follows
only a successful publisher confirmation. The publisher now reuses a single lazily
initialized connection and confirmation-enabled channel instead of opening a new pair
for every request.

`EventParserConsumer` remains unimplemented. No retry, reconnection, consumer,
dead-letter, Outbox, idempotency, or batch-tracking work was added.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx --no-restore
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 97 passed, 0 failed, 0 skipped (64 unit and 33
  integration tests).

## Decisions made

- A singleton connection/channel with serialized publish operations is a bounded
  implementation correction within PLAN 003 Step 2. It does not change the accepted
  HTTP acceptance boundary in [ADR 0009](../decisions/0009-define-stage-2-rabbitmq-batch-acceptance-boundary.md),
  so no ADR was created.

## Intentionally unresolved

- Reconnection and retry behavior after a broker, connection, or channel failure.
- The `EventParserConsumer` and all consumer acknowledgement, persistence, and
  failure policies.
- Final RabbitMQ topology, throughput configuration, and horizontal-scaling behavior.

## Next recommended step

Implement only PLAN 003 Step 3: add `EventParserConsumer` using the existing parsing,
validation, and persistence boundaries.
