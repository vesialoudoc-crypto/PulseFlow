# Checkpoint: Bounded transient PostgreSQL processing retry

**Date:** 2026-08-20

## Starting point

Stage 3 terminally rejected any unexpected parser or PostgreSQL processing failure to
the durable dead-letter queue. Event Contract v2 and the unique `(source, event_id)`
constraint already made reprocessing a batch safe when an earlier persistence chunk
had committed before a later chunk failed.

## What changed

- Added one fixed 100 ms in-process retry only when the initial complete-batch
  processing attempt throws `NpgsqlException` with `IsTransient == true`.
- Extracted one processing attempt into `EventParserConsumer.ProcessBatchAsync`. Each
  invocation creates a fresh asynchronous DI scope, resolves a fresh scoped handler and
  persistence dependencies, creates a fresh `MemoryStream`, creates a fresh NDJSON
  sequence, and handles the entire copied raw RabbitMQ body.
- After a transient first failure, the consumer logs one warning, waits the fixed delay,
  and performs exactly one more complete processing attempt. A successful retry
  acknowledges normally; a failed retry uses the existing terminal rejection path.
- Kept non-transient processing failures terminal after one attempt, shutdown
  cancellation unsettled, and acknowledgement or rejection failures outside retry
  handling.
- Added focused unit coverage for transient success after retry, transient failure
  after both attempts, non-transient terminal failure, and cancelled processing.
- Updated the Stage 3 roadmap, implemented ingestion architecture, and existing ADRs
  0010 and 0011 to describe the accepted bounded retry and its limits.

## Resulting repository state

The retry boundary is the full RabbitMQ batch processing attempt, not an individual SQL
command. If an initial transient database failure occurs after earlier chunks committed,
the second attempt reads the raw batch again; already committed Contract v2 events are
duplicate no-ops under `(source, event_id)`, while previously uncommitted events may be
inserted.

Exactly two attempts are possible: one initial attempt and at most one retry. The retry
does not use Polly, configuration, exponential backoff, retry queues, `requeue = true`,
or automatic dead-letter redrive. A non-transient failure, an exhausted transient retry,
or another retry failure is terminally rejected with `requeue = false` and follows the
existing durable DLQ route. This is not exactly-once RabbitMQ delivery or exactly-once
processing.

RabbitMQ topology, dead-letter topology, acknowledgement/rejection settlement
semantics, worker supervision, prefetch, Event Contract v2, and PostgreSQL event-level
idempotency are otherwise unchanged.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 130 passed, 0 failed, 0 skipped (85 unit and 45
  integration tests).
- The focused `EventParserConsumerTests` passed: 14 passed, 0 failed, 0 skipped.
- `check-project-docs.ps1` passed using Windows PowerShell; `pwsh` was unavailable in
  this environment.

## Decisions made

- Extended the existing Stage 3 terminal-processing-failure decision in
  [ADR 0010](../decisions/0010-dead-letter-unexpected-batch-processing-failures.md):
  one transient Npgsql processing failure receives one complete in-process retry before
  the established terminal rejection path.
- Event-level replay safety remains the accepted decision in
  [ADR 0011](../decisions/0011-use-event-level-idempotency.md). No new ADR was needed
  because this is a narrow extension of those existing failure-handling decisions.

## Intentionally unresolved

- Retry queues, automatic redrive, retry counters, configurable delay/backoff, and
  `requeue = true`.
- Exactly-once delivery and exactly-once processing claims.
- Outbox, Inbox, processed-batches storage, batch status, public query APIs, and
  broker-side flow control or prefetch.
- Record and batch limits, authentication, Redis rate limiting, load tests, and
  deployment topology.

## Next recommended step

Define and document one manual dead-letter redrive procedure that preserves Contract v2
`eventId` values, including its operator checks and explicit recovery boundary.
