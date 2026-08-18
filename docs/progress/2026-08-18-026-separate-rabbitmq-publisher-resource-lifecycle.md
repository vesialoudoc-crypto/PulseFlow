# Checkpoint: Separate RabbitMQ publisher resource lifecycle

**Date:** 2026-08-18

## Starting point

PLAN 003 Step 2 had a separate RabbitMQ publishing-resource provider in addition to
`RabbitMqIngestionBatchPublisher`. For one publish operation, that additional layer
obscured the direct publisher path without adding a required capability.

## What changed

- Removed `IRabbitMqPublisherChannelProvider` and
  `RabbitMqPublisherChannelProvider`.
- Moved one-time RabbitMQ connection/channel initialization, publisher-confirmation
  setup, and durable queue declaration into application composition before the host
  starts accepting requests.
- Registered the ready shared channel for the singleton publisher and registered
  application-stop disposal for the owned channel and connection.
- Simplified `RabbitMqIngestionBatchPublisher` to publish directly through its
  injected channel, keeping only the semaphore required to serialize shared-channel
  publication.
- Configured the integration-test host to use the `Testing` environment. It replaces
  the publisher in its HTTP-boundary tests, so RabbitMQ startup initialization is
  intentionally skipped only for that test host.
- Removed the explicit `AutomaticRecoveryEnabled = false` override. RabbitMQ.Client
  defaults automatic recovery to enabled; no custom retry or reconnection behavior
  was added.
- Updated the active ingestion architecture documentation.

## Resulting repository state

The application still reuses one long-lived RabbitMQ connection and one shared
publisher channel. Application composition owns startup initialization and shutdown
disposal. `RabbitMqIngestionBatchPublisher` serializes access to the injected channel
and owns raw NDJSON publication, persistent message properties, default-exchange
routing, mandatory publication, and publisher-confirmed completion semantics.

The HTTP contract is unchanged: `POST /api/events` returns HTTP `202 Accepted` only
after confirmed publication completes successfully; publication failures continue to
reach centralized error handling. PLAN 003 Step 3 and `EventParserConsumer` remain
unimplemented.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 97 passed, 0 failed, 0 skipped (64 unit and 33
  integration tests).
- `pwsh` was unavailable in the environment, so the same documentation-validation
  script was run with Windows PowerShell (`powershell -NoProfile -ExecutionPolicy
  Bypass -File ./scripts/check-project-docs.ps1`) and completed successfully.

## Decisions made

- Separating RabbitMQ publishing-resource ownership from the batch publisher is an
  implementation cleanup within the already accepted Step 2 boundary. It does not
  change the public HTTP contract or an accepted architectural decision, so no ADR
  was created.

## Intentionally unresolved

- Custom retry or reconnection behavior after broker, connection, or channel failure.
- The `EventParserConsumer` and all consumer acknowledgement, persistence, and
  failure policies.
- Final RabbitMQ topology, throughput configuration, and horizontal-scaling behavior.

## Next recommended step

Implement only PLAN 003 Step 3: add `EventParserConsumer` using the existing parsing,
validation, and persistence boundaries.
