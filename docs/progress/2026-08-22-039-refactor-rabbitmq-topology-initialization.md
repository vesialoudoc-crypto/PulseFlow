# Checkpoint: Refactor RabbitMQ topology initialization

**Date:** 2026-08-22

## Starting point

`RabbitMqConnectionManager.InitializeAsync` created the RabbitMQ connection,
declared the dead-letter and main queue topology, updated initialization state, and
cleaned up failed initialization in one method. The accepted topology and lifecycle
behavior were already documented in the ingestion architecture and ADRs.

## What changed

- Extracted dead-letter exchange, dead-letter queue, and binding declarations into
  `DeclareDeadLetterTopologyAsync`.
- Extracted the main queue declaration and its RabbitMQ-defined dead-letter
  arguments into `DeclareMainQueueAsync`.
- Extracted failed-initialization connection disposal into
  `CleanupFailedInitializationAsync`.
- Added concise comments explaining the short-lived topology channel, the
  dead-letter path, and RabbitMQ's `x-dead-letter-*` queue arguments.

## Resulting repository state

`InitializeAsync` now makes the startup lifecycle visible: acquire the initialization
gate, create the shared connection, use a short-lived channel to declare dead-letter
and main-queue topology, then mark initialization successful. It still disposes and
clears a created connection when initialization fails.

RabbitMQ topology is unchanged: the durable direct dead-letter exchange, durable
dead-letter queue, binding routing key, durable main queue, and
`x-dead-letter-exchange` and `x-dead-letter-routing-key` arguments retain their
previous values and declaration options. Publisher and consumer channel ownership,
initialization synchronization, and disposal behavior are unchanged.

## Verification

Commands run from the repository root:

```powershell
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter FullyQualifiedName~RabbitMq
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- The focused RabbitMQ suite built successfully; the non-container disposal test
  passed. Its three RabbitMQ/PostgreSQL tests could not start because Docker was
  unavailable at `npipe://./pipe/docker_engine`.
- The full integration project built successfully; 29 tests passed and 16
  Testcontainers-dependent tests could not start for the same unavailable-Docker
  reason.
- `dotnet build PulseFlow.slnx` passed with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` passed all 80 unit tests; its integration result was
  the same Docker-blocked 29 passed and 16 failed-to-start tests.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully.

## Decisions made

- No architectural decision changed. This is a readability-only refactor of the
  existing accepted RabbitMQ topology initialization behavior, so no ADR is needed.

## Intentionally unresolved

- RabbitMQ prefetch and other broker-side flow control.
- Manual DLQ recovery, retry queues, automatic redrive, backoff, requeue, and retry
  counters.
- Delivery guarantees, Outbox, Inbox, processed-batch storage, batch status, and
  public query APIs.

## Next recommended step

Define and document one manual dead-letter redrive procedure that preserves Contract v2
`eventId` values, including its operator checks and explicit recovery boundary.
