# Checkpoint: Implement Redis fixed-window ingestion limiter

**Date:** 2026-08-23

## Starting point

Stage 4 had Redis connection and options infrastructure plus the
`IIngestionRateLimiter` contract. No limiter implementation, Redis counter algorithm,
or rate-limit tests existed. `EventsController` still read and published every request
without a rate-limit check.

## What changed

- Added and registered `RedisIngestionRateLimiter` behind
  `IIngestionRateLimiter`.
- Implemented the accepted global fixed-window counter with one atomic Lua script and
  the key `pulseflow:rate-limit:ingestion:global`.
- The script denies a counter at `RequestLimit`; otherwise it increments and sets the
  TTL only when creating the counter. It returns the allow/deny decision and remaining
  TTL.
- Mapped results to allowed, exceeded with `RetryAfter`, and unavailable when a Redis
  operation throws a Redis client exception or the connection has been disposed.
- Added focused Redis Testcontainers tests for allowed, exhausted, and unavailable
  outcomes.

## Resulting repository state

The application now has a single shared, Redis-backed global fixed-window limiter.
It has no retries, distributed locks, generic Redis abstraction, cache, or local
fallback counter. The limiter is registered but is not called by an HTTP endpoint.
Consequently, no HTTP 429/503 behavior or `Retry-After` header is implemented, and
RabbitMQ publication is not yet protected by the limiter.

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
- `dotnet test PulseFlow.slnx` passed all 80 unit tests. The integration project ran
  29 tests successfully, while 19 Testcontainers-dependent tests could not start
  because Docker was unavailable at `npipe://./pipe/docker_engine`; this includes the
  three new Redis limiter tests.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully.

## Decisions made

- [ADR 0014](../decisions/0014-use-redis-for-global-ingestion-rate-limiting.md) now
  records the selected atomic script mechanics and result mapping.

## Intentionally unresolved

- Controller integration before request-body reading and RabbitMQ publication.
- HTTP 429/503 response mappings and `Retry-After` header serialization.
- Final quota values, client identity, multi-instance execution, load scenario, and
  measurements.

## Next recommended step

Integrate `IIngestionRateLimiter` into `EventsController` before it reads the request
body or publishes to RabbitMQ, with focused HTTP tests for 429, 503, and
`Retry-After` behavior.
