# Checkpoint: Isolate RabbitMQ integration tests from Redis

**Date:** 2026-08-23

## Starting point

`EventsController` now requires `IIngestionRateLimiter` before publishing an accepted
batch. `RabbitMqAsynchronousIngestionTests` creates an application host with real
RabbitMQ and PostgreSQL, but its factory did not replace the production Redis-backed
limiter. When CI started those dependencies without Redis, the controller returned
HTTP 503 instead of the HTTP 202 expected by the RabbitMQ tests.

## What changed

- The `RabbitMqAsynchronousIngestionTests` factory setup now removes the production
  `IIngestionRateLimiter` registration and adds a test-local implementation that
  always returns `Allowed`.
- The existing optional test-service customization still runs after that replacement,
  so the failure-path test continues to replace only its event-chunk store.
- No production rate-limiter registration or controller rate-limit test changed.

## Resulting repository state

RabbitMQ asynchronous-ingestion tests exercise RabbitMQ, PostgreSQL, and their
processing behavior without requiring Redis. Redis fixed-window behavior remains
covered exclusively by `RedisIngestionRateLimiterTests`; controller rate-limit
response behavior remains covered by the existing HTTP tests.

## Verification

Commands run from the repository root:

```powershell
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter FullyQualifiedName~RabbitMqAsynchronousIngestionTests
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
dotnet csharpier check .
pwsh ./scripts/check-project-docs.ps1
```

Results:

- The focused RabbitMQ test command was run first. It could not start its PostgreSQL
  and RabbitMQ Testcontainers fixtures because Docker was unavailable at
  `npipe://./pipe/docker_engine`; it therefore could not reproduce or rerun the CI
  Redis failure locally.
- `dotnet build PulseFlow.slnx -warnaserror` completed successfully with 0 warnings
  and 0 errors.
- `dotnet test PulseFlow.slnx` passed all 80 unit tests and 32 integration tests.
  The remaining 19 integration tests could not start because Docker was unavailable;
  this includes PostgreSQL, RabbitMQ, and Redis Testcontainers tests.
- `dotnet csharpier check .` completed successfully.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully.

The CI failure scenario is resolved by the focused host registration: its controller
now receives the always-allowed test limiter, so it cannot attempt a Redis operation
while exercising RabbitMQ and PostgreSQL.

## Decisions made

No architectural decision changed. This is test-environment isolation for an existing
dependency boundary.

## Intentionally unresolved

- A successful local rerun of the Testcontainers-backed tests requires a running
  Docker daemon.
- Stage 4 multi-instance rate-limit and load verification remains outstanding.

## Next recommended step

With Docker available, rerun the focused RabbitMQ test class to verify the full
RabbitMQ/PostgreSQL path independently of Redis, then continue the Stage 4
multi-instance rate-limit verification work.
