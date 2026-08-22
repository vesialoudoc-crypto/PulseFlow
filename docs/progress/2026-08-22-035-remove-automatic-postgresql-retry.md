# Checkpoint: Remove automatic PostgreSQL batch retry

**Date:** 2026-08-22

## Starting point

Stage 3 terminally rejected unexpected parser and persistence failures to the durable
dead-letter queue. Checkpoint 034 had added one automatic 100 ms in-process replay of
the complete raw batch when the first failure was a transient `NpgsqlException`.

## What changed

- Removed the Npgsql-specific transient-exception branch, fixed delay, and second
  `ProcessBatchAsync` invocation from `EventParserConsumer`.
- Each RabbitMQ delivery now receives exactly one complete processing attempt.
  Success acknowledges the delivery; any processing failure uses the existing terminal
  `requeue = false` rejection route to the DLQ.
- Preserved host shutdown cancellation propagation without rejecting the delivery.
- Removed transient-retry unit tests and their Npgsql test helper/import. Renamed the
  remaining failure test to cover any processing failure and asserted that `StoreAsync`
  is attempted exactly once.
- Updated the Stage 3 roadmap and ingestion architecture. ADR 0012 records the
  intentional removal and supersedes the retry extension in ADR 0010; ADR 0011 now
  describes idempotency independently of an automatic retry.

## Resulting repository state

`EventParserConsumer` has no automatic PostgreSQL, batch, or replacement retry policy.
The established Contract v2 event-level idempotency remains unchanged: sources provide
`eventId`, PostgreSQL enforces unique `(source, event_id)`, and each persistence chunk
uses `INSERT ... ON CONFLICT (source, event_id) DO NOTHING`.

Malformed NDJSON and contract-invalid records remain record-level outcomes. A terminal
processing failure dead-letters the whole raw batch, while host shutdown cancellation
leaves its delivery unsettled. The old retry implementation remains accurately recorded
in historical checkpoint 034.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --filter FullyQualifiedName~EventParserConsumerTests --no-restore
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter "FullyQualifiedName~EventParserConsumerIntegrationTests|FullyQualifiedName~RabbitMqAsynchronousIngestionTests" --no-restore
pwsh ./scripts/check-project-docs.ps1
```

Results:

- The focused `EventParserConsumerTests` passed: 12 passed, 0 failed, 0 skipped.
- `check-project-docs.ps1` passed.
- The solution build and the relevant integration-test project are currently blocked
  by an unrelated pre-existing change to `RabbitMqConnectionManager.cs`: the
  `RabbitMqAsynchronousIngestionTests` project still references its missing
  `Connection` member (`CS1061`). The API and unit-test projects built successfully
  during the focused unit-test run.

## Decisions made

- Accepted [ADR 0012](../decisions/0012-process-each-rabbitmq-delivery-once.md): each
  RabbitMQ delivery receives one processing attempt and any processing failure is
  terminally rejected.
- Event-level idempotency remains the accepted decision in
  [ADR 0011](../decisions/0011-use-event-level-idempotency.md).

## Intentionally unresolved

- Manual DLQ recovery, retry queues, automatic redrive, backoff, requeue, and retry
  counters.
- Exactly-once delivery and exactly-once processing claims.
- Outbox, Inbox, processed-batches storage, batch status, public query APIs, and
  broker-side flow control or prefetch.

## Next recommended step

Define and document one manual dead-letter redrive procedure that preserves Contract v2
`eventId` values, including its operator checks and explicit recovery boundary.
