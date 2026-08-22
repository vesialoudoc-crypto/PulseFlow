# Checkpoint: Simplify parser worker lifecycle

**Date:** 2026-08-22

## Starting point

Checkpoint 035 had removed automatic in-process PostgreSQL retry while preserving the
existing `EventParserConsumer` worker supervision. The hosted service created a linked
worker cancellation token, used `Task.WhenAny` to observe the first worker completion,
cancelled siblings after a worker failure, and preserved the initiating exception
through worker cleanup.

## What changed

- Simplified `EventParserConsumer.ExecuteAsync` to start exactly `ConsumerCount`
  workers with the host stopping token and await them with `Task.WhenAll`.
- Removed linked cancellation-token creation, `Task.WhenAny`, sibling cancellation,
  unexpected-worker-completion handling, and initiating-exception preservation.
- Simplified each worker to natural async enumeration and asynchronous consumer
  disposal. Worker exceptions now flow naturally to `BackgroundService` through
  `Task.WhenAll`.
- Removed unit tests that existed only for sibling cancellation and preservation of an
  initiating exception through custom supervision. Kept normal processing, ACK,
  Reject, cancellation, configured-worker, and RabbitMQ consumer-disposal coverage.
- Updated the current roadmap and ingestion architecture. ADR 0013 records the
  lifecycle decision and supersedes the worker-supervision portion of ADR 0010.

## Resulting repository state

Normal shutdown is controlled only by the host stopping token passed to every parser
worker. `EventParserConsumer` does not cancel, replace, restart, health-check, or
recover workers. If one worker faults while another continues running, the hosted
service remains awaiting all worker tasks; after they complete, the failure naturally
faults the `BackgroundService`.

Delivery semantics are unchanged. One delivery receives one processing attempt;
successful processing acknowledges it, a processing failure terminally rejects it to
the existing DLQ, and host cancellation leaves it unsettled. RabbitMQ topology,
publisher behavior, Contract v2 idempotency, PostgreSQL persistence, and Event Contract
behavior are unchanged.

## Verification

Commands run from the repository root:

```powershell
dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --filter FullyQualifiedName~EventParserConsumerTests --no-restore
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx --no-restore
pwsh ./scripts/check-project-docs.ps1
```

Results:

- The focused `EventParserConsumerTests` suite passed: 9 passed, 0 failed, 0 skipped.
- The full unit-test suite passed during `dotnet test PulseFlow.slnx`: 80 passed,
  0 failed, 0 skipped.
- `dotnet build PulseFlow.slnx` and the integration-test portion of
  `dotnet test PulseFlow.slnx` are blocked by an unrelated pre-existing worktree
  mismatch: `RabbitMqAsynchronousIngestionTests` references the missing
  `RabbitMqConnectionManager.Connection` member (`CS1061`). The API and unit-test
  projects built successfully.
- `pwsh ./scripts/check-project-docs.ps1` passed.

## Decisions made

- Accepted [ADR 0013](../decisions/0013-use-backgroundservice-worker-lifecycle.md):
  rely on normal `BackgroundService` and host lifecycle behavior for parser workers.
- [ADR 0012](../decisions/0012-process-each-rabbitmq-delivery-once.md) remains in
  effect for per-delivery processing and terminal rejection.

## Intentionally unresolved

- Worker health checks, restart, replacement, and recovery behavior.
- Manual DLQ recovery, retry queues, automatic redrive, backoff, requeue, and retry
  counters.
- Exactly-once delivery and exactly-once processing claims.
- Outbox, Inbox, processed-batches storage, batch status, public query APIs, and
  broker-side flow control or prefetch.

## Next recommended step

Define and document one manual dead-letter redrive procedure that preserves Contract v2
`eventId` values, including its operator checks and explicit recovery boundary.
