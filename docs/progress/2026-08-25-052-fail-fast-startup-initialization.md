# Checkpoint: Fail fast on mandatory startup initialization failure

**Date:** 2026-08-25

## Starting point

Checkpoint 051 added one-time startup initialization and readiness state. A mandatory
initializer or parser readiness-participant failure was recorded as `Failed`, but the
exception was swallowed. The host therefore remained live indefinitely while readiness
stayed unhealthy.

## What changed

- Preserved `StartupReadinessState.MarkFailed` and error logging, then rethrew a
  mandatory startup failure from `StartupInitializationService`.
- Relied on normal `BackgroundService` host-failure semantics to stop the instance;
  host-stop cancellation remains a non-failure path.
- Replaced the endpoint test that expected a failed initializer to leave liveness
  available with a test that verifies `Failed` state and host stopping.
- Updated [ADR 0015](../decisions/0015-separate-startup-initialization-from-runtime-readiness.md)
  to record fail-fast startup behavior and deployment/container restart ownership.
- Inspected `RedisStartupInitializer`; its service-provider resolution remains limited
  to eager startup initialization and required no lifecycle change.

## Resulting repository state

The process is live but not ready only while mandatory startup work is in progress. If
a mandatory initializer or readiness participant fails, the readiness state records
`Failed` for diagnostics, the failure is logged, and the host stops. This slice has no
in-process startup retry or backoff; the deployment or container restart policy is
responsible for retrying process startup.

After the process reaches `Ready`, a later PostgreSQL, Redis, or RabbitMQ outage still
makes `/health/ready` return HTTP 503 without stopping the process or rerunning startup
initializers.

## Verification

Commands run from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-build --filter "FullyQualifiedName~StartupReadiness"
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~StartupReadiness"
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
```

Results:

- CSharpier check passed.
- Build completed with zero warnings and zero errors.
- Focused startup-readiness unit tests passed: 2 passed, 0 failed.
- Focused startup-readiness endpoint tests passed: 4 passed, 0 failed.
- Documentation check passed.
- The full Testcontainers suite could not run because Docker Desktop / Docker Engine
  is unavailable; `scripts/test.ps1` stopped at its documented Docker preflight.

## Decisions made

- Mandatory startup initialization failures stop the instance after readiness state and
  diagnostic logging are updated. See [ADR 0015](../decisions/0015-separate-startup-initialization-from-runtime-readiness.md).
- Runtime dependency health remains separate from startup failure and does not stop a
  process that has reached `Ready`.

## Intentionally unresolved

- In-process startup retry/backoff, dependency recovery, and worker restart policy.
- Deployment/container restart policy configuration and retry budget.
- Production deployment probe configuration and alerting policy.

## Next recommended step

With Docker available, run the full Testcontainers suite and verify the chosen
deployment environment restarts an instance after a mandatory startup failure.
