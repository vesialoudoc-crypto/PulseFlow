# ADR 0020: Use explicit dependency timeout budgets

**Date:** 2026-08-26

## Status

Accepted

## Context

The application depends on PostgreSQL, RabbitMQ, and Redis on its startup,
ingestion, and readiness paths. Leaving the duration of those operations entirely to
client-library defaults would make request outcomes, readiness latency, and failed
startup timing dependent on implicit library configuration. Host shutdown and client
request cancellation must remain distinct from a dependency that exceeded an
application-owned budget.

## Options

1. Keep client-library defaults and only rely on caller cancellation.
2. Add automatic retry and circuit-breaker policies around every dependency call.
3. Define validated application-owned operation budgets and use linked cancellation
   for each operation.

## Decision

Use component-owned typed options with positive `TimeSpan` values, validated at
startup. PostgreSQL, RabbitMQ, Redis, startup, and health-check composition own the
settings for their respective operations rather than sharing a universal timeout
executor.

PostgreSQL uses Npgsql's connection and command timeouts. EF Core receives the command
timeout, and the manually created `NpgsqlCommand` receives the same explicit timeout.
RabbitMQ.Client uses its connection, handshake, and continuation timeout settings. Its
topology declaration, publisher-channel creation, and publish/confirmation calls take
their own directly-created linked cancellation token because those APIs accept one.
Redis configures `ConnectTimeout` and `AsyncTimeout`; the rate-limit script uses
`WaitAsync(AsyncTimeout, callerToken)` because StackExchange.Redis does not accept a
per-command cancellation token. This bounds caller waiting but does not claim to cancel
an already-sent Redis command.

`StartupInitializationService` creates one linked cancellation token for its complete
initialization sequence. Health-check registrations use ASP.NET Core's built-in timeout
property. Caller or host cancellation is checked before internal timeout handling and
remains normal cancellation.

The configured initial budgets are:

| Operation | Budget |
| --- | --- |
| PostgreSQL connection and command | 10 seconds |
| RabbitMQ connection, handshake, continuation, and topology | 10 seconds |
| RabbitMQ publisher channel and publish/confirmation | 5 seconds |
| Redis connect | 10 seconds |
| Redis async operation | 1 second |
| Readiness registration | 2 seconds |
| Overall startup initialization | 30 seconds |

Redis rate-limit timeout is treated as unavailable, so the HTTP endpoint returns 503
before publishing. A RabbitMQ publish/confirmation timeout faults publishing and
therefore cannot return 202. A startup timeout fails readiness and propagates through
the normal hosted-service startup-failure path. Readiness registrations and the
dependency checks they invoke use the readiness budget.

## Consequences

- Provider-native settings own PostgreSQL, RabbitMQ connection, and Redis connection
  timing; local cancellation is used only where an API needs an additional operation
  deadline.
- Normal client aborts and host shutdown remain cancellation, rather than dependency
  failures, and do not mark startup as failed.
- A timed-out RabbitMQ publisher channel is discarded rather than returned to the
  pool, avoiding reuse after an uncertain publish result.
- The budgets are initial operational values, not measured latency targets. They can
  be adjusted through configuration after observation without changing public HTTP
  contracts.
- Successful dependency operations are not logged on the request path. Timeout and
  failure logs use fixed messages and structured values without inserting connection
  strings or credentials; global and parser-consumer exception diagnostics retain their
  exception objects.
- This decision adds no retry, Polly policy, circuit breaker, Render-specific value,
  or change to RabbitMQ DLQ behavior.
