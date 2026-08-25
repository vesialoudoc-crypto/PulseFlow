# Checkpoint: Add startup readiness lifecycle

**Date:** 2026-08-25

## Starting point

Checkpoint 050 recorded the final fresh post-fix local ingestion performance baseline.
RabbitMQ initialization already ran through a dedicated hosted service, but Redis
multiplexer creation and rate-limit Lua resource loading could still occur on the first
ingestion request. The API exposed only dependency-free `/health/live`; it had no
readiness endpoint or boundary between startup work and runtime dependency health.

## What changed

- Added the one-time `IStartupInitializer` contract, `StartupReadinessState`, and a
  sequential `StartupInitializationService` orchestrator.
- Moved RabbitMQ topology and publisher-channel-pool setup into a startup initializer;
  removed publisher and consumer lazy initialization from the request and worker paths.
- Added PostgreSQL startup connectivity and pending-migration verification without
  calling `Database.Migrate`.
- Added Redis startup multiplexer resolution, limiter Lua resource loading, and `PING`.
- Added tagged startup-state, PostgreSQL, Redis, and RabbitMQ runtime health checks;
  mapped `/health/ready` alongside the dependency-free `/health/live` endpoint.
- Made parser workers await the infrastructure-complete gate and report completed
  `BasicConsume` subscriptions before readiness is marked ready.
- Updated the performance runner and its README to wait for `/health/ready == 200`.
- Added focused unit and endpoint tests for awaitable readiness, initialization failure,
  startup-in-progress liveness/readiness behavior, and runtime-health failure without
  reinitialization. Real RabbitMQ integration startup now uses Redis and migrates the
  test database before its application host starts.
- Recorded the lifecycle decision in [ADR 0015](../decisions/0015-separate-startup-initialization-from-runtime-readiness.md)
  and updated the ingestion architecture and Stage 4 roadmap state.

## Resulting repository state

The normal startup lifecycle is:

```text
process starts
    ↓
/health/live = 200
    ↓
PostgreSQL, RabbitMQ, and Redis one-time initialization runs
    ↓
parser consumers register their subscriptions
    ↓
/health/ready = 200 when runtime dependency checks are also healthy
```

While initialization is still running or after it has failed, `/health/ready` returns
HTTP 503. Later PostgreSQL, Redis, or RabbitMQ runtime failure also returns HTTP 503,
but startup state stays `Ready` and no startup initializer runs again.

`/health/live` means only that this ASP.NET Core process can serve HTTP; it does not
depend on PostgreSQL, Redis, RabbitMQ, migration execution, or startup initialization.
`/health/ready` means mandatory startup initialization finished and all required runtime
dependencies are currently healthy.

The ingestion path has no remaining first-request lazy startup initialization: Redis
multiplexer creation and script loading, RabbitMQ connection/topology/publisher pool,
and parser consumer registration all occur before readiness. The API still does not run
database migrations; Compose's migrations service owns that operation.

## Verification

Commands run from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-build
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~StartupReadiness"
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
```

Results:

- Build completed with zero warnings and zero errors.
- Unit tests passed: 85 passed, 0 failed.
- Focused startup-readiness endpoint tests passed: 5 passed, 0 failed.
- CSharpier check passed.
- Documentation check passed.
- The full Testcontainers suite could not start because Docker Desktop / Docker Engine
  is unavailable. `scripts/test.ps1` stopped at its documented Docker preflight before
  it ran any tests.

## Decisions made

- Startup initialization and runtime health are separate responsibilities. See
  [ADR 0015](../decisions/0015-separate-startup-initialization-from-runtime-readiness.md).
- Parser consumer subscription is part of readiness because the Stage 4 end-to-end
  performance workload depends on consumers being registered before traffic begins.

## Intentionally unresolved

- Startup retry/backoff, dependency recovery, and worker restart policy.
- Production deployment probe configuration, retry budgets, and alerting policy.
- Measured performance conclusions or a selected Stage 4 bottleneck.

## Next recommended step

With Docker available, run the full Testcontainers suite and a readiness-gated local
performance scenario, then select a focused, evidence-based Stage 4 bottleneck
investigation.
