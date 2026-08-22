# Checkpoint: Verify RabbitMQ consumers through broker metadata

**Date:** 2026-08-22

## Starting point

`RabbitMqAsynchronousIngestionTests.ConsumerCount_Two_StartsWorkersWithSeparateConsumerChannels`
inspected `EventParserConsumer`, `RabbitMqIngestionBatchConsumerFactory`,
`RabbitMqIngestionBatchConsumer`, and `RabbitMqConnectionManager` implementation
state. Its assertion depended on the removed `RabbitMqConnectionManager.Connection`
member, which prevented the integration-test project and solution from compiling.

## What changed

- Replaced the implementation-inspection test with
  `ConsumerCount_Two_RegistersTwoRabbitMqConsumers`.
- The test starts the real application and polls passive RabbitMQ queue declaration
  metadata until the configured queue reports `ConsumerCount == 2`.
- Removed test-only implementation exposure:
  `EventParserConsumer.ConsumerFactory`, `EventParserConsumer.ConsumerCount`,
  `RabbitMqIngestionBatchConsumerFactory.Consumers`, and
  `RabbitMqIngestionBatchConsumer.Channel`.
- Removed the consumer-factory collection that existed solely to support the former
  test. Worker count is now private state of `EventParserConsumer`.
- Updated current roadmap and ingestion architecture documentation to state the
  observable broker behavior verified by the integration test.

## Resulting repository state

The consumer-count integration test treats RabbitMQ as the verification boundary: with
`RabbitMq:ConsumerCount = 2`, the configured queue must report two registered
consumers. Production code does not expose RabbitMQ connections, channels, consumers,
or consumer collections for tests.

## Verification

Commands run from the repository root:

```powershell
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter FullyQualifiedName~RabbitMqAsynchronousIngestionTests
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj
dotnet build PulseFlow.slnx
dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- The focused RabbitMQ suite built successfully, confirming that the missing
  `RabbitMqConnectionManager.Connection` build failure is resolved. Its three tests
  could not execute because Docker was unavailable at `npipe://./pipe/docker_engine`.
- The full integration project built successfully; 29 tests passed and 16
  Testcontainers-dependent tests could not start for the same unavailable-Docker
  reason.
- `dotnet build PulseFlow.slnx` passed with 0 warnings and 0 errors.
- The unit-test project passed: 80 passed, 0 failed, 0 skipped.
- `dotnet test PulseFlow.slnx` had the same Docker-blocked integration results while
  the 80 unit tests passed.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully after this
  checkpoint was added.

## Decisions made

- Broker-observable queue metadata is the integration-test contract for configured
  consumer registration. No ADR is needed because this changes test technique, not
  runtime architecture or behavior.

## Intentionally unresolved

- RabbitMQ prefetch and other broker-side flow control.
- Manual DLQ recovery, retry queues, automatic redrive, backoff, requeue, and retry
  counters.
- Delivery guarantees, Outbox, Inbox, processed-batch storage, batch status, and
  public query APIs.

## Next recommended step

Define and document one manual dead-letter redrive procedure that preserves Contract v2
`eventId` values, including its operator checks and explicit recovery boundary.
