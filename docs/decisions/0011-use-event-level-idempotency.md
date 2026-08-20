# ADR 0011: Use event-level idempotency for repeated delivery

**Date:** 2026-08-20

## Status

Accepted

## Context

RabbitMQ delivery and batch processing attempts can repeat. The existing chunked
persistence path can commit an earlier chunk before a later chunk fails. Replaying the
complete batch would therefore duplicate earlier rows because the server-generated
`EventRecord.Id` describes the stored row, not the logical client event.

The project needs a small, PostgreSQL-backed semantic that remains correct when two
independent consumers attempt the same event concurrently. It does not need generic
messaging infrastructure, a batch identity, or a distributed lock.

## Options

1. Require a source-owned event UUID and enforce `(source, event_id)` uniqueness in
   PostgreSQL.
2. Deduplicate by comparing `type`, `occurredAt`, and payload content.
3. Query for a prior row before inserting.
4. Add a processed-batches table, Inbox, Redis key, or distributed lock.

## Decision

Event Contract v2 requires `eventId`, a JSON-string UUID supplied by the event source.
The logical identity is `(source, eventId)`. The `events` table stores a non-null
`event_id uuid` and has a unique index on `(source, event_id)`.

The persistence implementation submits each supplied chunk as one parameterized
PostgreSQL `INSERT ... VALUES ... ON CONFLICT (source, event_id) DO NOTHING` command
inside one explicit transaction. A duplicate is a successful no-op. The database
constraint is the final correctness boundary; no select-before-insert check or
duplicate-key exception flow is used.

Existing rows are preserved by first adding nullable `event_id`, backfilling it from
the existing server-generated `id`, then making it non-null and adding the unique
index. The backfilled values make legacy rows schema-compatible only; v1 events did
not have genuine source-provided identities.

## Consequences

- Replaying a complete batch after later-chunk failure leaves already committed logical
  events as duplicate no-ops and allows uncommitted events to be inserted.
- Different sources can use the same `eventId`.
- Normal ingestion accounting remains unchanged: a valid duplicate is successfully
  handled, and no public duplicate count or batch-status API is added.
- The current RabbitMQ topology, acknowledgement/rejection behavior, retry policy,
  prefetch, and dead-letter policy are unchanged.

## Explicit limitations

- This is not exactly-once RabbitMQ delivery or exactly-once processing.
- There is no automatic dead-letter redrive or retry policy.
- A sender that does not preserve `eventId` on resend defeats event-level idempotency.
- No processed-batches table, Inbox, Outbox, Redis deduplication, distributed lock,
  batch idempotency key, or `Idempotency-Key` HTTP header is introduced.
