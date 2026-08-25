# Checkpoint: Separate startup lifecycle from HTTP composition

**Date:** 2026-08-25

## Starting point

Checkpoint 058 separated production infrastructure composition from test composition,
but `AddPulseFlowHttpApplication` still called `AddStartupInitialization`. Therefore,
every component host started `StartupInitializationService`, including event-acceptance,
Redis quota, and liveness scenarios that do not exercise the startup lifecycle.

## What changed

- Removed `AddStartupInitialization` from `AddPulseFlowHttpApplication`.
- Added `AddStartupInitialization` explicitly in production `Program`.
- Added `AddStartupInitialization` explicitly only in the readiness component-host
  setup. Event acceptance, Redis quota, and liveness hosts now contain no startup
  hosted service.
- Updated ADR 0018 and the ingestion architecture documentation to distinguish the
  HTTP composition seam from application-lifecycle composition.

## Resulting repository state

`AddPulseFlowHttpApplication` and `UsePulseFlowHttpApplication` now form a strictly
HTTP-focused reusable boundary: controllers, middleware, Problem Details, OpenAPI,
HTTPS redirection, and health endpoint mapping. Startup initialization remains an
explicit, independent registration in production and in only those component tests
that verify readiness semantics.

Production startup/readiness behavior from ADR 0015 is unchanged. No component host
automatically registers PostgreSQL, RabbitMQ, Redis, or startup lifecycle services.

## Verification

- `dotnet test tests\PulseFlow.IntegrationTests\PulseFlow.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~EventAcceptanceTests|FullyQualifiedName~LivenessEndpointTests|FullyQualifiedName~StartupReadinessEndpointTests"` passed: 12 tests.
- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit tests and 57 integration tests.

## Decisions made

This is a refinement of the test-owned dependency-composition decision in
[ADR 0018](../decisions/0018-keep-production-composition-environment-agnostic.md),
not a new architectural decision. The roadmap and product boundaries did not change.

## Intentionally unresolved

- The existing startup/readiness, messaging, rate-limiting, and deployment limitations
  recorded in the current ADRs remain unchanged.
- Full-system verification intentionally continues to require Docker for PostgreSQL,
  RabbitMQ, and Redis Testcontainers.

## Next recommended step

Resume the documented Stage 5 work: define and implement the minimal deployment
architecture for the verified application without prematurely selecting AWS services,
network topology, or release strategy.
