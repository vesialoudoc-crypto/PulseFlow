# ADR 0014: Use Redis for global ingestion rate limiting

**Date:** 2026-08-23

## Status

Accepted

## Context

Stage 4 introduces horizontal `PulseFlow.Api` execution. A process-local quota would
not constrain requests that are distributed between API instances. The ingestion path
must protect RabbitMQ and downstream processing before an incoming request body is
read or a batch is published.

The accepted rate-limit scope is one global quota for all ingestion requests. The
selected algorithm is a fixed window. Redis is the shared operational state store; it
is not the primary event store, a generic cache, or batch-status storage.

## Options

1. Use Redis with one global fixed-window quota.
2. Use a process-local in-memory quota for each API instance.
3. Use Redis with a per-client quota or a sliding-window algorithm.

## Decision

Use Redis-backed state for one global fixed-window ingestion quota shared by all API
instances. Configure the quota through `IngestionRateLimit:RequestLimit` and
`IngestionRateLimit:WindowDuration`, and the Redis endpoint through
`ConnectionStrings:Redis`.

The rate-limit check will run before request-body reading and before RabbitMQ
publication. An exhausted quota will return HTTP 429. Redis unavailability will return
HTTP 503, and no process-local fallback is permitted.

The application owns one shared process-level StackExchange.Redis connection
multiplexer. Its application-facing limiter contract must not expose StackExchange.Redis
types.

## Consequences

- API instances will use one shared quota rather than independent local counters.
- A Redis outage prevents ingestion instead of allowing the quota to be bypassed.
- The body is not read and RabbitMQ is not contacted for a request rejected by the
  limiter.
- Redis availability becomes a dependency of ingestion availability.
- The configuration foundation may be added before the limiter algorithm and endpoint
  integration.

## Explicit limitations

- The Redis command or script implementation is not selected or implemented yet.
- No `IIngestionRateLimiter` implementation is registered yet.
- The controller does not perform the rate-limit check and has no HTTP 429 or 503
  behavior yet.
- `Retry-After` behavior, final quota values, client identity, load scenarios, and
  measured scaling conclusions remain unresolved.
