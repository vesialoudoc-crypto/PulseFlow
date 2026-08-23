# Checkpoint: Add Stage 4 rate-limit infrastructure foundation

**Date:** 2026-08-23

## Starting point

Stage 3 was complete. Redis was reserved for future distributed ingestion rate
limiting, but no Redis package, configuration, connection ownership, or limiter
abstraction existed. `EventsController` read the request body and published it to
RabbitMQ without rate-limit integration.

## What changed

- Added the `StackExchange.Redis` package.
- Added startup-validated `IngestionRateLimit` options with positive `RequestLimit`
  and `WindowDuration` values.
- Required a non-empty `ConnectionStrings:Redis` value.
- Registered one shared process-level `IConnectionMultiplexer` for Redis.
- Added Redis-independent `IIngestionRateLimiter` and result-contract types, without
  registering an implementation.
- Added accepted-design documentation in ADR 0014 and marked Stage 4 as in progress.

## Resulting repository state

Redis connection and rate-limit configuration now form a validated infrastructure
foundation. The shared multiplexer is configured not to abort application startup when
Redis is initially unavailable, so the future limiter can provide the accepted HTTP
503 behavior rather than a process-local fallback.

No Redis rate-limit algorithm, Redis command or script, limiter implementation,
controller change, request-body ordering change, HTTP 429/503 mapping, RabbitMQ-publish
prevention, or new tests were added. Existing integration-test host configuration now
supplies the new required settings but does not instantiate a Redis limiter.

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
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` passed all 80 unit tests. The integration project ran
  29 tests successfully, while 16 Testcontainers-dependent tests could not start
  because Docker was unavailable at `npipe://./pipe/docker_engine`.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully.

## Decisions made

- [ADR 0014](../decisions/0014-use-redis-for-global-ingestion-rate-limiting.md)
  records the accepted Redis-backed global fixed-window quota, request ordering, and
  fail-closed Redis-unavailable behavior.

## Intentionally unresolved

- Redis counter command or script and atomicity mechanics.
- The `IIngestionRateLimiter` implementation and its Redis error handling.
- Controller integration and HTTP 429/503 response details, including `Retry-After`.
- Final quota values, multi-instance execution, load scenario, and measurements.

## Next recommended step

Implement the Redis fixed-window limiter behind `IIngestionRateLimiter`, with a focused
test suite for allowed, exhausted, and Redis-unavailable outcomes before integrating it
into `EventsController`.
