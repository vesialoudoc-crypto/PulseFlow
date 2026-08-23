# Checkpoint: Protect ingestion publishing with the rate limiter

**Date:** 2026-08-23

## Starting point

The Redis-backed global fixed-window limiter was implemented, registered, and covered
by focused limiter tests. `EventsController` did not yet use it, so every request body
could be read and published to RabbitMQ regardless of the limiter result.

## What changed

- Integrated `IIngestionRateLimiter` into `EventsController` before request-body
  reading and batch publication.
- Mapped an exceeded result to HTTP 429 with a `Retry-After` delta-seconds header,
  rounding the limiter duration up to a whole second.
- Mapped an unavailable limiter result to HTTP 503.
- Added focused HTTP tests using fixed limiter outcomes and a recording publisher.
  They verify exceeded-to-429 with `Retry-After` and no publisher call,
  unavailable-to-503 with no publisher call, and allowed-to-202 with one publisher
  call.
- Kept existing HTTP tests independent of a running Redis instance by injecting an
  allowed test limiter by default.

## Resulting repository state

The API now fail-closes ingestion when Redis cannot decide the global quota. It checks
the limiter before allocating and copying the raw body and before contacting RabbitMQ.
Only an allowed request reaches the publisher and can receive HTTP 202. The tests make
the publisher non-invocation on rejected requests an explicit architecture boundary.

## Verification

Commands run from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet csharpier check .` completed successfully.
- `dotnet build PulseFlow.slnx -warnaserror` completed successfully with 0 warnings
  and 0 errors.
- `dotnet test PulseFlow.slnx` passed all 80 unit tests and 32 integration tests.
  The remaining 19 integration tests could not start because Docker was unavailable
  at `npipe://./pipe/docker_engine`; they are existing Testcontainers-dependent
  PostgreSQL, RabbitMQ, and Redis tests. The focused HTTP tests in this checkpoint
  passed.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully.

Focused verification already completed successfully:

```powershell
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter FullyQualifiedName~EventAcceptanceTests
```

- All 7 `EventAcceptanceTests` passed.

## Decisions made

- [ADR 0014](../decisions/0014-use-redis-for-global-ingestion-rate-limiting.md)
  records the HTTP mapping and `Retry-After` serialization used by the controller.

## Intentionally unresolved

- Final quota values, client identity, multi-instance execution, load scenario, and
  measurements.
- The broader Stage 4 horizontal-scaling and load-testing work.

## Next recommended step

Define a small, reproducible multi-instance rate-limit verification or load scenario
before making scaling claims or selecting measured quota values.
