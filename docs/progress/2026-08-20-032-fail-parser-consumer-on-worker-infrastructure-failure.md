# Checkpoint: Parser worker infrastructure failure supervision

**Date:** 2026-08-20

## Starting point

`EventParserConsumer` ran the configured workers through `Task.WhenAll`. If one worker
failed, the remaining workers could continue serving the queue, reducing parser capacity
without the hosted service failing. `RabbitMqIngestionBatchConsumer.DisposeAsync` also
could skip channel disposal when `BasicCancelAsync` failed.

The existing Stage 3 processing-failure policy was already accepted: unexpected parsing
or PostgreSQL persistence failure terminally rejects only that delivery with
`requeue = false` and routes it to the durable dead-letter queue.

## What changed

- Added linked worker-lifetime supervision to `EventParserConsumer`.
- When a worker faults or finishes while the host is still running, the service cancels
  all sibling workers, waits for every worker cleanup task, and rethrows the original
  initiating exception.
- Preserved an initial worker exception when its own consumer disposal also fails.
- Kept host cancellation as clean shutdown: cancellation stops all workers without
  turning normal shutdown into a parser-consumer failure.
- Changed RabbitMQ consumer disposal so channel disposal is attempted in `finally`
  after a consumer-tag cancellation attempt.
- Changed the processing-failure log message to state that terminal rejection is being
  attempted, rather than claiming it has completed.
- Added focused unit coverage for acknowledgement failure, rejection failure, consumer
  creation failure, normal multi-worker shutdown, and channel disposal after a
  `BasicCancelAsync` failure.

## Resulting repository state

With more than one configured worker, the hosted parser consumer now has one failure
boundary:

```text
worker fault or unexpected completion
    -> cancel sibling worker lifetime
    -> await all worker cleanup
    -> rethrow original initiating exception
    -> ASP.NET Core host supervision observes the failure
```

Unexpected parsing or persistence failure remains separate: it logs the processing
failure, attempts `RejectAsync(requeue: false)`, and allows the worker to continue when
that rejection succeeds. ACK failure never calls Reject; Reject failure never calls ACK.
There is no automatic worker restart, retry, redrive, requeue, idempotency, or new
delivery guarantee.

RabbitMQ cleanup still attempts `BasicCancelAsync` when a tag exists and does not
explicitly settle outstanding deliveries. It now always attempts channel disposal. A
channel closure can make unsettled deliveries eligible for broker redelivery; that is
RabbitMQ recovery behavior, not an application retry policy.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 114 passed, 0 failed, 0 skipped (76 unit and 38
  integration tests).
- Documentation validation completed successfully.

## Decisions made

- Extended [ADR 0010](../decisions/0010-dead-letter-unexpected-batch-processing-failures.md)
  with hosted parser-worker supervision. This is part of the existing Stage 3 failure
  boundary and does not select a new infrastructure component or delivery guarantee.

## Intentionally unresolved

- Automatic worker restart, reconnect, retry, requeue, retry counters, delay or
  backoff, and automatic dead-letter redrive.
- Idempotency, deduplication, Outbox, and any end-to-end delivery guarantee.
- RabbitMQ prefetch and other broker-side flow control.
- Batch-status/result storage and public status/query APIs.
- Redis, authentication, load testing, performance targets, and deployment topology.

## Next recommended step

Define the next smallest Stage 3 scenario before adding recovery behavior. A possible
next investigation is the manual redrive boundary for a dead-lettered batch, including
how to avoid duplicate records when earlier PostgreSQL chunks may already exist.
