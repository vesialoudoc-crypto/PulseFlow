# Checkpoint: Separate RabbitMQ publisher resource lifecycle

**Date:** 2026-08-18

## Starting point

PLAN 003 Step 2 already reused one long-lived RabbitMQ connection and one shared
channel, but `RabbitMqIngestionBatchPublisher` also owned their initialization,
queue declaration, serialization, and disposal. That mixed resource lifecycle with
the batch publishing operation.

## What changed

- Added `IRabbitMqPublisherChannelProvider` and its singleton
  `RabbitMqPublisherChannelProvider` implementation.
- Moved lazy connection/channel creation, publisher-confirmation channel setup,
  durable queue declaration, serialization of shared-channel use, and asynchronous
  disposal into that provider.
- Simplified `RabbitMqIngestionBatchPublisher` to forward the raw ingestion batch to
  the provider's small RabbitMQ-specific `PublishAsync` operation, without a generic
  callback-based API.
- Removed the explicit `AutomaticRecoveryEnabled = false` override. RabbitMQ.Client
  defaults automatic recovery to enabled; no custom retry or reconnection behavior
  was added.
- Added a focused unit test for the publisher-to-provider responsibility boundary.
- Updated the active ingestion architecture documentation.

## Resulting repository state

The application still reuses one long-lived RabbitMQ connection and one shared
publisher channel. The provider serializes access to that channel and disposes its
owned resources during application shutdown. The publisher retains the exact raw
NDJSON body and forwards it unchanged. The provider owns persistent message
properties, default-exchange routing, mandatory publication, and publisher-confirmed
completion semantics.

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
- The solution tests passed: 98 passed, 0 failed, 0 skipped (65 unit and 33
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
