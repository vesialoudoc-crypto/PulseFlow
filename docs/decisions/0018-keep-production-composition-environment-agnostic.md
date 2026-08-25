# ADR 0018: Keep production composition environment-agnostic

**Date:** 2026-08-25

## Status

Accepted

## Context

ASP.NET Core's `Testing` environment name had become an implicit dependency-injection
profile. Production registration extensions selectively omitted PostgreSQL, RabbitMQ,
and Redis startup initialization and readiness checks when that environment was active.
RabbitMQ registration also substituted a test-only hosted service for the real parser
consumer.

As a result, a test that selected a real RabbitMQ broker by switching to `Development`
also enabled unrelated PostgreSQL and Redis infrastructure. Other HTTP tests supplied
placeholder connection strings only to let `Program` start. The environment name was
therefore selecting dependencies rather than normal environment behavior.

## Options

1. Retain an environment-specific test profile and extend it for each test scenario.
2. Keep starting `Program` for all HTTP tests and remove production registrations from
   the service collection after the fact.
3. Make production composition unconditional, expose a small reusable HTTP composition
   seam, and let tests explicitly compose only the dependencies they require.

## Decision

Production composition is environment-agnostic. `AddPulseFlowPersistence`,
`AddIngestionMessaging`, and `AddIngestionRateLimiting` always register their real
startup initialization, readiness, health-check, and hosted-service components.
Production code contains no test-specific hosted service or dependency-selection branch.

`AddPulseFlowHttpApplication` and `UsePulseFlowHttpApplication` own the reusable
controller, exception handling, Problem Details, status-code handling, OpenAPI, HTTPS,
and health-endpoint composition. The only environment-sensitive behavior is exposing
OpenAPI and Swagger UI in `Development`; it does not alter dependency registrations.

Component/API tests use the test-project-local `PulseFlowComponentTestHost`. It applies
the production HTTP composition to an in-memory `TestServer` but never starts
`Program` or automatically registers PostgreSQL, RabbitMQ, or Redis. Each scenario
registers its required publishers, rate limiters, startup initializers, and runtime
health checks explicitly. The Redis quota HTTP test composes real Redis-backed limiters
against its Redis Testcontainer, with one multiplexer per test host to represent
independent API process ownership.

`PulseFlowSystemWebApplicationFactory` is reserved for full-system RabbitMQ tests. It
starts the real `Program` and supplies PostgreSQL, RabbitMQ, and Redis Testcontainer
connection strings plus an isolated queue name before application composition begins.

## Consequences

- Component tests show their dependency composition directly and require no placeholder
  infrastructure connection strings.
- Full-system tests continue to verify the production startup/readiness path with all
  mandatory infrastructure enabled.
- Tests cannot use an ASP.NET Core environment name to substitute infrastructure
  implementations; their service registrations are the explicit test boundary.
- The production HTTP pipeline is shared between the application and component hosts,
  reducing drift without introducing a separate framework or a global test mode.