# Checkpoint: Separate production and test composition

**Date:** 2026-08-25

## Starting point

Production registration used the ASP.NET Core environment name `Testing` as a hidden
DI profile. PostgreSQL, RabbitMQ, and Redis startup/readiness registrations were
conditionally omitted, and RabbitMQ replaced its parser consumer with a test-specific
hosted service. `PulseFlowWebApplicationFactory` switched between `Testing` and
`Development`, so requesting a real RabbitMQ broker also enabled unrelated PostgreSQL
and Redis composition.

## What changed

- Made `AddPulseFlowPersistence`, `AddIngestionMessaging`, and
  `AddIngestionRateLimiting` environment-agnostic. Production composition now always
  registers the PostgreSQL, RabbitMQ, and Redis initializers, readiness components,
  health checks, and real parser consumer.
- Removed `TestingMessagingHostedService` and the environment-dependent
  `PulseFlowWebApplicationFactory`.
- Extracted `AddPulseFlowHttpApplication` and `UsePulseFlowHttpApplication` from
  `Program`. The shared boundary contains controllers, exception handling, Problem
  Details, status-code handling, OpenAPI, HTTPS, and health endpoint mapping.
- Added the test-project-local `PulseFlowComponentTestHost`, an asynchronously
  disposable in-memory `TestServer` host that applies the production HTTP composition
  without starting `Program` or registering external infrastructure.
- Converted event acceptance, liveness, and startup/readiness tests to explicit
  component composition. They register only their recording/failing publisher, fixed
  rate limiter, startup initializer, or runtime health check as required.
- Converted the Redis quota HTTP scenario to two component hosts with separate
  Redis multiplexer instances against one Redis Testcontainer and a shared recording
  publisher. It does not start PostgreSQL or RabbitMQ lifecycle services.
- Added `PulseFlowSystemWebApplicationFactory` exclusively for the RabbitMQ
  end-to-end suite. It starts the real `Program` with PostgreSQL, RabbitMQ, and Redis
  Testcontainers, a migrated database, and a unique RabbitMQ queue.
- Recorded the durable composition boundary in
  [ADR 0018](../decisions/0018-keep-production-composition-environment-agnostic.md)
  and updated [ingestion architecture](../architecture/ingestion.md).

## Resulting repository state

Production code contains no test-specific service or environment branch that chooses
infrastructure dependencies. Development-only OpenAPI and Swagger UI exposure remains
normal environment behavior and does not affect registrations.

Component/API tests use the same production HTTP pipeline but own their dependencies
explicitly. `RabbitMqAsynchronousIngestionTests` is the sole HTTP suite that exercises
the full production composition and mandatory startup/readiness path.

## Verification

- `dotnet test tests\PulseFlow.IntegrationTests\PulseFlow.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~EventAcceptanceTests|FullyQualifiedName~LivenessEndpointTests|FullyQualifiedName~StartupReadinessEndpointTests"` passed: 12 tests.
- `dotnet test tests\PulseFlow.IntegrationTests\PulseFlow.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~RedisIngestionRateLimiterTests"` passed: 4 tests.
- `dotnet test tests\PulseFlow.IntegrationTests\PulseFlow.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~RabbitMqAsynchronousIngestionTests"` passed: 3 tests.
- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit tests and 57 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- Docker Engine 29.5.3 was available, so all Testcontainers verification ran.

## Decisions made

[ADR 0018](../decisions/0018-keep-production-composition-environment-agnostic.md)
accepts environment-agnostic production composition and test-owned dependency
composition. No product boundary changed, so `01_SOURCE_OF_TRUTH.md` was not updated.
The Stage 4 result and Stage 5 status did not change, so `02_ROADMAP.md` was not
updated.

## Intentionally unresolved

- This change does not add a generalized test framework, fake infrastructure profile,
  startup retry, migration execution from the API, or new infrastructure component.
- The existing operational and messaging limitations recorded in the prior ADRs remain
  unchanged.
- Full-system verification still requires Docker because it intentionally uses real
  PostgreSQL, RabbitMQ, and Redis containers.

## Next recommended step

Resume the documented Stage 5 work: define and implement the minimal deployment
architecture for the verified application without prematurely selecting AWS services,
network topology, or release strategy.