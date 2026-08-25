# ADR 0015: Separate startup initialization from runtime readiness

**Date:** 2026-08-25

## Status

Accepted

## Context

The API previously initialized RabbitMQ when a hosted service started, but Redis
connection creation and Lua-script loading could remain on the first ingestion request.
There was only a liveness endpoint, so it did not express whether mandatory
infrastructure setup had completed or whether dependencies were currently usable.

The performance runner must begin only after the complete ingestion path is ready. At
the same time, a later Redis, PostgreSQL, or RabbitMQ outage must not rerun one-time
startup work such as RabbitMQ topology declaration or publisher-channel pool creation.

## Options

1. Run startup initialization once in a hosted orchestrator and combine its state with
   side-effect-free runtime dependency checks in a readiness endpoint.
2. Retain lazy initialization in request handling and use only dependency probes for
   readiness.
3. Add a general retry and lifecycle framework that restarts initialization whenever a
   dependency health check fails.

## Decision

Use a singleton, awaitable `StartupReadinessState` with `Starting`, `Ready`, and
`Failed` states. A `StartupInitializationService` runs registered
`IStartupInitializer` implementations sequentially once per process start. It marks
the state ready only after all initializers and the parser-consumer subscription
participant finish successfully. A mandatory initializer or readiness-participant
failure is recorded in the state, logged with the failing component type, and then
propagated through normal `BackgroundService` host-failure semantics so the instance
stops.

RabbitMQ topology and publisher-channel-pool setup, Redis multiplexer creation plus
`PING` and limiter script loading, and PostgreSQL connectivity plus pending-migration
verification are startup initialization. The API does not execute migrations; the
Compose `migrations` service remains responsible for `dotnet ef database update`.

`GET /health/live` has no checks and represents only a running ASP.NET Core process.
`GET /health/ready` combines the startup state with repeatable PostgreSQL connectivity,
Redis `PING`, and existing RabbitMQ connection-state checks. Runtime health checks do
not create RabbitMQ channels, declare topology, publish messages, or invoke startup
initializers.

## Consequences

- An instance may be live before it is ready; load balancers and the performance runner
  can wait for `/health/ready` without relying on a first ingestion request or a delay.
- Parser workers wait asynchronously for mandatory infrastructure initialization, then
  report their completed RabbitMQ subscriptions before the instance becomes ready.
- A runtime dependency outage makes readiness return HTTP 503 while startup state stays
  `Ready`; global initialization is not rerun automatically.
- This slice has no in-process startup retry or backoff. A mandatory startup failure
  stops the instance; the deployment or container restart policy is responsible for a
  subsequent process-start attempt.

## Explicit limitations

- This decision does not add migrations to the API, a generalized lifecycle framework,
  worker restart, dependency recovery, or a retry/backoff policy.
- Readiness does not claim that a future request cannot fail after its probe completes.
