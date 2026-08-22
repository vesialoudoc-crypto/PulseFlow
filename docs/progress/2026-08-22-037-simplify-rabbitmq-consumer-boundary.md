# Checkpoint: Simplify RabbitMQ consumer boundary

**Date:** 2026-08-22

## Starting point

`RabbitMqIngestionBatchConsumer` adapted RabbitMQ's `ReceivedAsync` callback to an
unbounded `Channel<IngestionBatchDelivery>`. `EventParserConsumer` pulled deliveries
through `IAsyncEnumerable<IngestionBatchDelivery>` and processed them in an
`await foreach` loop. That callback-to-channel-to-async-stream bridge was introduced
by the Stage 2 messaging-boundary refactor, not by an accepted ADR.

## What changed

- Replaced `IIngestionBatchConsumer.ReadAllAsync` with callback-based
  `ConsumeAsync` consumption.
- Simplified `EventParserConsumer` so each worker creates its consumer and passes
  `ProcessDeliveryAsync` directly to `ConsumeAsync`.
- Removed the RabbitMQ adapter's application `Channel`, reader/writer plumbing,
  async iterator, and async-stream bridge. Its RabbitMQ callback now copies the body,
  constructs `IngestionBatchDelivery`, and invokes the application handler directly.
- Kept RabbitMQ.Client primitives, delivery tags, acknowledgement/rejection delegates,
  consumer tags, cancellation, and channel disposal inside the RabbitMQ adapter.
- Rewrote consumer test doubles around the callback boundary and retained processing,
  acknowledgement, rejection, host-cancellation, multi-worker, and consumer-disposal
  coverage.
- Updated the current roadmap, Stage 2 plan, and ingestion architecture documentation.
  Historical checkpoint 029 remains unchanged because it accurately records the
  previous implementation.

## Resulting repository state

Each parser worker now follows this boundary:

```text
RabbitMQ callback
    -> application delivery handler
    -> EventParserConsumer.ProcessDeliveryAsync
```

There is no application-level queue, buffer, polling loop, retry, mediator, or worker
supervision added by this change. `ConsumeAsync` remains active until host cancellation
or a consumer-level failure. Delivery semantics are unchanged: one delivery receives
one processing attempt; successful processing acknowledges it; a processing failure
terminally rejects it; and host shutdown cancellation leaves it unsettled.

## Verification

Commands to run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --filter FullyQualifiedName~EventParserConsumerTests
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter FullyQualifiedName~EventParserConsumerIntegrationTests
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter FullyQualifiedName~RabbitMqAsynchronousIngestionTests
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- The focused `EventParserConsumerTests` suite passed: 9 passed, 0 failed, 0 skipped.
- The full unit-test suite passed: 80 passed, 0 failed, 0 skipped.
- `dotnet build PulseFlow.slnx`, the full solution test command, and the integration
  test project are blocked by the unrelated pre-existing worktree mismatch recorded
  in checkpoint 036: `RabbitMqAsynchronousIngestionTests` references the missing
  `RabbitMqConnectionManager.Connection` member (`CS1061`). The API and unit-test
  projects built successfully.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully.

## Decisions made

- The removed channel/async-stream bridge was an implementation choice, not an ADR
  requirement. No ADR is superseded or added.
- [ADR 0012](../decisions/0012-process-each-rabbitmq-delivery-once.md) and
  [ADR 0013](../decisions/0013-use-backgroundservice-worker-lifecycle.md) remain in
  effect.

## Intentionally unresolved

- RabbitMQ prefetch and other broker-side flow control.
- Manual DLQ recovery, retry queues, automatic redrive, backoff, requeue, and retry
  counters.
- Delivery guarantees, Outbox, Inbox, processed-batch storage, batch status, and
  public query APIs.

## Next recommended step

Define and document one manual dead-letter redrive procedure that preserves Contract v2
`eventId` values, including its operator checks and explicit recovery boundary.
