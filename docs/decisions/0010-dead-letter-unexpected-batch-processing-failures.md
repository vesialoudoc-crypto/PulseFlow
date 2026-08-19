# ADR 0010: Dead-letter unexpected batch processing failures

**Date:** 2026-08-20

## Status

Accepted

## Context

`EventParserConsumer` processes a raw NDJSON batch through the existing parsing,
validation, chunked persistence path. A malformed NDJSON record or a record that does
not satisfy Event Contract v1 is a normal record-level result: later valid records in
the same batch may still be persisted.

An unexpected parsing or PostgreSQL persistence failure is different. The current
handler persists chunks one at a time, so chunks before a failing chunk may already be
committed. Persisted `EventRecord` rows receive new generated GUIDs. Automatically
replaying the full raw batch before idempotency or deduplication exists could therefore
create duplicate records.

The earlier Stage 2 implementation logged an unexpected batch failure and left the
delivery unacknowledged. This did not define a terminal state, could block later work
on the channel, and did not retain the failed raw batch in a dedicated location.

## Options

1. Reject the failed delivery without requeueing it and route it to a dead-letter
   queue, with no automatic retry.
2. Requeue the failed delivery immediately.
3. Retry or delay delivery through retry queues.
4. Acknowledge the failed delivery and rely only on logs.

## Decision

For an unexpected batch processing failure, `EventParserConsumer` logs the failure and
terminally rejects exactly that delivery. The RabbitMQ adapter implements this with a
single-delivery reject and `requeue = false`.

The configured main ingestion queue has dead-letter exchange and routing-key
arguments. A durable direct dead-letter exchange and a durable dead-letter queue are
declared from the configured queue name. The dead-letter queue is bound with the
explicit dead-letter routing key.

There are zero automatic retries and no automatic dead-letter redrive in this slice.
Successful processing still acknowledges the delivery only after the ingestion handler
completes. Host shutdown cancellation remains normal shutdown behavior and does not
reject a delivery.

## Consequences

- Poison-message requeue loops are avoided for the selected unexpected-failure path.
- Later healthy deliveries can continue after one batch fails.
- The failed raw batch is retained in RabbitMQ's dead-letter queue for investigation.
- Malformed NDJSON and contract-invalid records remain normal record-level outcomes;
  they do not dead-letter the whole batch.
- A local main queue that was declared by Stage 2 has no dead-letter arguments.
  RabbitMQ will reject an incompatible redeclaration, so the application does not
  delete, purge, or recreate it. A developer must manually recreate that local queue
  before using this topology. Production RabbitMQ policies are the recommended mutable
  DLX configuration mechanism, but policy automation is outside this slice.

## Explicit limitations

- A dead-letter queue does not provide an end-to-end no-loss guarantee. RabbitMQ
  dead-letter republishing can fail for some broker topologies and availability states.
- Earlier PostgreSQL chunks may already be persisted when the batch is rejected.
- Replay and redrive behavior are intentionally undefined.
- Retry, idempotency, deduplication, Outbox, and delivery guarantees remain unresolved.
- RabbitMQ prefetch remains unresolved until after this failure policy is implemented.
- This decision does not introduce quorum queues.
