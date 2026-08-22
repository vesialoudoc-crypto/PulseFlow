# ADR 0012: Process each RabbitMQ delivery once

**Date:** 2026-08-22

## Status

Accepted

## Context

ADR 0010 originally extended terminal rejection with one automatic in-process retry
for a transient PostgreSQL exception. That retry replayed the complete raw batch after
a fixed delay, creating two processing attempts for one RabbitMQ delivery.

The required failure policy is now simpler: one delivery gets one processing attempt.
Successful processing must acknowledge the delivery, any processing failure must be
terminally rejected to the durable dead-letter queue, and host shutdown cancellation
must remain unsettled rather than being rejected.

## Options

1. Process each delivery once and terminally reject any processing failure.
2. Retain the one transient PostgreSQL retry.
3. Replace it with a configurable in-process or broker-based retry policy.

## Decision

`EventParserConsumer` performs exactly one complete processing attempt for every
RabbitMQ delivery. It acknowledges only after that attempt succeeds. Any processing
exception other than host shutdown cancellation is logged and terminally rejected with
`requeue = false`, using the established dead-letter route.

The consumer has no Npgsql-specific exception handling, delay, second attempt, or
replacement retry policy. Host shutdown cancellation continues to propagate normally
and does not settle the delivery.

This supersedes only the automatic transient-retry extension in ADR 0010. Its durable
dead-letter topology and terminal rejection decision remain in effect.

## Consequences

- A RabbitMQ delivery has one local processing attempt, regardless of exception type.
- A transient PostgreSQL failure is terminally rejected and available in the DLQ for
  investigation or separately defined manual recovery.
- The existing Contract v2 `eventId`, unique `(source, event_id)` constraint, and
  `ON CONFLICT (source, event_id) DO NOTHING` behavior are unchanged.
- No retry queue, redrive, delay, backoff, requeue, or automatic worker restart is
  introduced.

## Explicit limitations

- This is not exactly-once RabbitMQ delivery or exactly-once processing.
- RabbitMQ dead-letter republishing can fail for some broker topologies and
  availability states.
- Earlier PostgreSQL chunks may already be persisted when a later chunk fails.
- Manual recovery, retry queues, automatic redrive, Outbox, prefetch, and delivery
  guarantees remain unresolved.
