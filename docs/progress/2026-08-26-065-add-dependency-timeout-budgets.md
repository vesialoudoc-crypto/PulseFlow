# Checkpoint: Add explicit dependency timeout budgets

**Date:** 2026-08-26

## Starting point

PostgreSQL, RabbitMQ, Redis, startup initialization, and readiness existed, but their
maximum wait time was determined by caller cancellation and client-library defaults.
There was no application-owned timeout configuration that was aligned with the native
semantics of each dependency, or an overall startup deadline distinct from normal host
or caller cancellation.

## What changed

- Added startup-validated component-owned options: `PostgreSql`, `RabbitMq`, `Redis`,
  `Startup`, and `HealthChecks`.
- Configured Npgsql connection and command timeouts through the connection string and
  EF Core, and explicitly applied the command timeout to the manually created
  `NpgsqlCommand` in `EfCoreEventChunkStore`.
- Configured RabbitMQ.Client 7.2.2 connection, handshake, and continuation timeouts.
  Topology declaration, publisher-channel creation, and publish/confirmation create a
  linked cancellation token directly beside the RabbitMQ operation and pass it to the
  client API without a second `WaitAsync` wrapper.
- Configured StackExchange.Redis `ConnectTimeout` and `AsyncTimeout`. StackExchange.Redis
  3.1.13 reports an overdue asynchronous operation as `RedisTimeoutException`, so the
  rate-limit script and startup `PING` use `WaitAsync` only for caller cancellation;
  this does not claim that an already-sent Redis command is physically cancelled.
- Added the readiness budget to PostgreSQL, RabbitMQ, and Redis health-check
  registrations.
- Made a timed-out Redis rate-limit operation return the existing unavailable result,
  preserving HTTP 503 before request-body reading or publishing.
- Reworked the RabbitMQ publisher into a fixed `PublisherChannelCount` slot pool. A
  timed-out, cancelled, or failed publish discards its uncertain channel and returns an
  empty slot. A later request can create a bounded replacement for its own batch only;
  the prior uncertain batch is never retried. RabbitMQ readiness is unhealthy when no
  usable publisher channel remains.
- Added positive and safe-upper-bound startup validation for all RabbitMQ timeouts;
  `TopologyDeclarationTimeout`, `PublisherChannelTimeout`, and
  `PublishConfirmationTimeout` are therefore valid for `CancelAfter` before runtime.
- Removed the PostgreSQL and Redis health-check cancellation catches. Framework
  registration timeouts are handled by `DefaultHealthCheckService`, while external
  caller cancellation now propagates instead of being logged as a dependency timeout.
- Removed the production default-options constructors from `EfCoreEventChunkStore` and
  `RedisIngestionRateLimiter`; test construction passes explicit options.
- Applied one overall startup budget. Expiration marks readiness failed and propagates
  a startup error; host shutdown remains normal cancellation.
- Restored exception-object logging in the global exception handler and parser
  consumer. Component-specific timeout messages do not include connection strings or
  credentials, and successful hot-path operations are not logged.
- Added focused tests for PostgreSQL timeout conversion, RabbitMQ caller cancellation
  and internal publish timeout, Redis operation timeout to unavailable, HTTP 503/no
  publish, non-202 RabbitMQ timeout behavior, readiness timeout, and failed readiness
  on startup timeout.

## Resulting repository state

The application explicitly owns the maximum waiting time for defined dependency
operations without a universal runtime executor. It has no automatic retry, Polly
policy, circuit breaker, Render-specific configuration, or change to DLQ semantics.
The initial configured budgets are recorded in
[ADR 0020](../decisions/0020-use-explicit-dependency-timeout-budgets.md); they are
operational starting values rather than measured targets.

## Verification

- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 131 unit tests and 64 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- Accepted component-owned dependency timeout budgets and their cancellation and HTTP
  semantics in
  [ADR 0020](../decisions/0020-use-explicit-dependency-timeout-budgets.md).
- The values remain configured initial budgets, not a production SLO or performance
  conclusion.

## Intentionally unresolved

- Measured dependency latency, production-specific budget tuning, retry policy, and
  circuit-breaker policy remain unresolved.
- No Render resource, credential, deployment, Terraform plan, or apply was created.

## Next recommended step

Run the full project checks and review the dependency-timeout configuration against a
real staging deployment once its controlled bootstrap is authorized.
