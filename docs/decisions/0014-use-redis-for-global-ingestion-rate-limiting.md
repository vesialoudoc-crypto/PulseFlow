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

The rate-limit check runs before request-body reading and before RabbitMQ publication.
An exhausted quota returns HTTP 429 with a delta-seconds `Retry-After` header. The
header rounds the Redis TTL up to the next whole second. Redis unavailability returns
HTTP 503, and no process-local fallback is permitted.

The application owns one shared process-level StackExchange.Redis connection
multiplexer. Its application-facing limiter contract must not expose StackExchange.Redis
types.

The implementation uses one atomic Redis Lua script and the global key
`pulseflow:rate-limit:ingestion:global`. The script denies when the current count is
already at the configured limit. Otherwise, it increments the counter and assigns the
fixed-window TTL only when that increment creates the counter. The script returns the
allow/deny decision and the remaining TTL. A denied result exposes that TTL as
`RetryAfter`; a Redis operation failure maps to the existing unavailable result.

## Consequences

- API instances will use one shared quota rather than independent local counters.
- A Redis outage prevents ingestion instead of allowing the quota to be bypassed.
- The body is not read and RabbitMQ is not contacted for a request rejected by the
  limiter; focused HTTP tests verify that the publisher is not called.
- Redis availability becomes a dependency of ingestion availability.
- The configuration foundation was added before the limiter algorithm and endpoint
  integration.

## Explicit limitations

- Final quota values, client identity, load scenarios, multi-instance execution, and
  measured scaling conclusions remain unresolved.
