# Checkpoint: Bound startup connect and RabbitMQ cleanup

**Date:** 2026-08-26

## Starting point

The explicit dependency-timeout slice used a synchronous
`ConnectionMultiplexer.Connect(...)` DI factory. Resolving that dependency during
Redis startup could block for `Redis:ConnectTimeout`, outside the overall startup
token. RabbitMQ publisher and failed-startup connection cleanup awaited
`DisposeAsync()` before propagating the publish timeout, caller cancellation, or
startup failure, allowing RabbitMQ.Client network abort to extend those budgets.

## What changed

- Moved Redis multiplexer creation to `RedisStartupInitializer` through
  `ConnectionMultiplexer.ConnectAsync(...)`.
- Made the shared `IConnectionMultiplexer` DI registration read the connection state
  established by that initializer, so resolving it no longer performs network I/O.
- Bound the logical Redis connection wait by the overall startup cancellation token.
  If that token wins, a connection that completes later is disposed in the background.
- Added startup regression coverage for cancellation during Redis connection and late
  connection disposal.
- Added validated `RabbitMq:CleanupTimeout`, with a one-second default.
- Detached uncertain publisher channels and failed-startup connections before starting
  background best-effort disposal. Cleanup has its own logical budget and logs timeout
  or late failure without delaying or replacing the original operation outcome.
- Added publisher tests proving a blocking channel cleanup does not delay either a
  publish timeout or caller cancellation.
- Updated ADR 0020 and the roadmap to document the corrected timeout semantics.

## Resulting repository state

The process-wide startup deadline now bounds the Redis connection wait even when
`Redis:ConnectTimeout` is longer. RabbitMQ channels with uncertain publishes are still
removed from the fixed slot pool immediately, but cleanup no longer extends the HTTP
publish outcome. The failed-startup RabbitMQ connection follows the same best-effort
cleanup rule. No retry, circuit breaker, delivery guarantee, Terraform, deployment, or
broker-topology behavior changed.

## Verification

- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 135 unit tests and 64 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- Clarified the accepted dependency-timeout decision in
  [ADR 0020](../decisions/0020-use-explicit-dependency-timeout-budgets.md): cleanup
  has an independent logical budget and must not alter the original timeout or
  cancellation outcome.

## Intentionally unresolved

- The cleanup timeout is an initial operational value, not a measured target.
- Cleanup is logical best effort: RabbitMQ.Client disposal cannot be physically
  cancelled after it has begun, but it is no longer on the caller or startup path.
- Measured dependency latency, production-specific budget tuning, retry policy, and
  circuit-breaker policy remain unresolved.

## Next recommended step

Run the full repository checks, then review the timeout configuration against a real
staging deployment once its controlled bootstrap is authorized.
