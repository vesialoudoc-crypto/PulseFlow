# Checkpoint: Verify multi-instance Redis ingestion quota

**Date:** 2026-08-23

## Starting point

Stage 4 had an accepted Redis-backed global fixed-window ingestion limiter and focused
single-host HTTP tests. The test factory always configured its Redis endpoint as
`localhost:6379`, so an integration test could not start two independent API hosts
against the existing Redis Testcontainer.

## What changed

- Added optional Redis connection-string and rate-limit settings to
  `PulseFlowWebApplicationFactory`; its existing constructor paths retain their
  previous defaults.
- Added a focused Redis Testcontainer integration test that starts two separate API
  hosts with the same Redis connection string and one shared recording publisher.
- The test alternates four accepted requests between the hosts, then verifies that a
  fifth request through the first host is rate-limited with HTTP 429 and
  `Retry-After`. The publisher records exactly four calls.
- Recorded the verified multi-instance scenario in the ingestion architecture.

## Resulting repository state

Two independently composed `PulseFlow.Api` test hosts share the production Redis
global ingestion-quota key. The test does not start RabbitMQ or use PostgreSQL: it
replaces only `IIngestionBatchPublisher` with a shared recording test publisher.

This verifies the fixed-window quota is shared across the tested API instances. It
does not establish performance, throughput, fairness, or a production deployment
topology.

## Verification

Commands run from the repository root:

```powershell
dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --filter FullyQualifiedName~RedisIngestionRateLimiterTests
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- The focused Redis suite passed: 4 tests passed, including the two-host shared-quota
  scenario.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` passed: 80 unit tests and 52 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.

`scripts/test.ps1` is not present in this branch, so the standard test command from
the repository's `AGENTS.md` was run directly instead.

## Decisions made

No architectural decision changed. The test verifies the existing global Redis quota
decision in [ADR 0014](../decisions/0014-use-redis-for-global-ingestion-rate-limiting.md).

## Intentionally unresolved

- Load-test scenario, baseline metrics, bottleneck analysis, and follow-up
  measurement.
- Final measured quota values, client identity, and production deployment topology.
- Redis availability behavior beyond the already accepted fail-closed limiter result.

## Next recommended step

Define and run a reproducible Stage 4 load scenario, then record the baseline,
identified bottleneck, and any justified improvement without marking the stage complete
until those measurements exist.
