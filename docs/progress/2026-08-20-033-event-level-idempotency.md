# Checkpoint: Event-level idempotency for repeated delivery

**Date:** 2026-08-20

## Starting point

Stage 3 already terminally rejected an unexpected parser or PostgreSQL failure to the
dead-letter queue. Earlier PostgreSQL chunks could already be committed when a later
chunk failed, while every stored row had only a server-generated `EventRecord.Id`.
Replaying the complete batch would therefore have duplicated earlier logical events.

## What changed

- Added Event Contract v2 with a required source-owned UUID `eventId`; v1 remains
  historical documentation and is not implemented concurrently.
- Propagated `EventId` through NDJSON record validation, `EventEnvelope`, and
  `EventRecord`.
- Added an EF Core migration that adds nullable `event_id`, backfills it from legacy
  `id`, makes it non-null, and adds a unique `(source, event_id)` index.
- Replaced the EF Core add-and-save persistence path with one parameterized PostgreSQL
  `INSERT ... VALUES ... ON CONFLICT (source, event_id) DO NOTHING` command inside one
  explicit transaction for each supplied chunk.
- Added focused unit and real-PostgreSQL integration coverage for invalid event IDs,
  propagation, duplicate no-ops, source scoping, partial replay, and concurrent inserts.
- Added ADR 0011 and reconciled current contract, architecture, and Stage 3 roadmap
  documentation.

## Resulting repository state

The logical identity is `(source, eventId)`. A source must reuse `eventId` for a resend
of the same logical event and use a different ID for a genuine new event; different
sources may use the same UUID. `EventRecord.Id` remains the server-generated primary
key for the durable row.

The PostgreSQL unique index is the final concurrency boundary. A duplicate valid event
is successfully handled as a no-op: it does not create another row, fail a chunk, or
cause RabbitMQ dead-lettering. `IngestEventsResult` keeps its existing meaning, so a
valid duplicate is included as successfully handled without a public duplicate count.

One supplied chunk remains one atomic persistence boundary. In a partial replay,
already committed first-chunk events conflict and become no-ops while previously
uncommitted later events are inserted. This does not claim exactly-once RabbitMQ
delivery or exactly-once processing.

The legacy backfill preserves existing rows but does not give historical v1 events
genuine client-provided identities.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 128 passed, 0 failed, 0 skipped (83 unit and 45
  integration tests).
- The full real-PostgreSQL suite includes the partial-replay and concurrent-insert
  checks; both passed.
- `pwsh` was unavailable in this Windows environment, so the same documentation script
  was successfully run with `powershell -NoProfile -ExecutionPolicy Bypass -File
  .\scripts\check-project-docs.ps1`.

## Decisions made

- Accepted [ADR 0011](../decisions/0011-use-event-level-idempotency.md): Contract v2
  requires UUID `eventId`; PostgreSQL uniqueness on `(source, event_id)` enforces
  event-level idempotency; duplicates are successful no-ops.
- No RabbitMQ topology, acknowledgement/rejection, retry, prefetch, or dead-letter
  policy changed in this slice.

## Intentionally unresolved

- Exactly-once delivery and exactly-once processing claims.
- Automatic retry, requeue, dead-letter redrive, delay/backoff, and retry counters.
- Outbox, Inbox, processed-batches storage, batch status, and public query APIs.
- RabbitMQ prefetch and other broker-side flow control.
- Record and batch limits, authentication, Redis rate limiting, load tests, and
  deployment topology.

## Next recommended step

Define the next small Stage 3 recovery scenario, likely a manually operated
dead-letter redrive procedure that explicitly preserves Contract v2 `eventId` values
and documents its operational boundary.
