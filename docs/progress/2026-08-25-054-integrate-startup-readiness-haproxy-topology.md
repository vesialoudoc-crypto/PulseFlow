# Checkpoint: Integrate startup readiness with HAProxy measurement topologies

**Date:** 2026-08-25

## Starting point

The `feature/stage4-haproxy-topology-analysis` branch provided isolated Single and
Multi local Compose topologies, an HAProxy ingress for Multi, a fixed 10-second k6
baseline, RabbitMQ and container sampling, accepted-equals-persisted validation,
invariant report-number parsing, and an ingestion-path warm-up mechanism.

`develop` contained the merged startup-readiness work from PR #7: application-owned
startup initialization, dependency-free liveness, and dependency-aware readiness.

## What changed

- Merged `origin/develop` into the feature branch without creating the merge commit.
- Preserved the feature runner's mandatory topology selection, isolated Compose project
  names and ports, topology validation, fixed 10-second baseline, sampling, completion
  invariant, culture-invariant report parsing, and null-safe sampler cleanup.
- Removed the synthetic ingestion warm-up POST and all associated PostgreSQL-row and
  Redis-key cleanup from the permanent runner.
- Made Single wait for its public `/health/ready` endpoint before starting samplers or
  k6.
- Made Multi probe `/health/ready` on both `api-1` and `api-2` from HAProxy over the
  internal Compose network; the replicas remain without direct host ports.
- Added a Multi-only artifact, `multi-replica-post-traffic.txt`, derived from HAProxy
  HTTP logs. The runner fails unless measured client POST traffic reached both replicas.
- Changed HAProxy active backend health checks from `/health/live` to `/health/ready`.
- Resolved ADR numbering: the startup-readiness decision is ADR 0015, the HAProxy
  local-ingress decision is ADR 0016, and the prior warm-up decision/history is ADR
  0017. ADR 0017 is now superseded by ADR 0015.
- Updated the roadmap, architecture documentation, performance-runner README, and
  historical checkpoint links affected by the ADR renames.

## Resulting repository state

`GET /health/live` remains a dependency-free indication that the ASP.NET Core process
can serve HTTP. `GET /health/ready` is the traffic boundary: mandatory startup
initialization and parser-consumer registration completed, and PostgreSQL, Redis, and
the existing RabbitMQ connection are currently healthy.

HAProxy retains round-robin routing across the two internal replicas, but only sends
client traffic to replicas whose `/health/ready` probe succeeds. The fixed 10-second
baseline begins only after the selected measurement topology is ready; it no longer
uses a synthetic ingestion request as readiness evidence.

## Verification

- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-build --no-restore`
  passed: 85 tests.
- PowerShell parsing of `tests/performance/run.ps1` passed.
- `git diff --check` and `git diff --cached --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully. It warns that two
  historical checkpoints changed; those changes correct ADR-link paths after the
  required renumbering.
- `pwsh ./scripts/test.ps1`, both `-ValidateTopology` invocations, and the requested
  Single and Multi 30-VU smoke runs were blocked because Docker is unavailable. The
  runner and test script both reported that Docker Desktop or Docker Engine must be
  started. No workload was started, so neither historical stall could be retested and
  no new per-replica traffic artifact was produced.

## Decisions made

- [ADR 0015](../decisions/0015-separate-startup-initialization-from-runtime-readiness.md)
  remains the accepted application-owned startup/readiness decision.
- [ADR 0016](../decisions/0016-use-haproxy-for-local-multi-instance-api-ingress.md)
  now records `/health/ready` as the HAProxy health-check path.
- [ADR 0017](../decisions/0017-warm-full-ingestion-path-before-performance-baseline.md)
  preserves the warm-up experiment as superseded historical evidence, not as the
  current readiness mechanism.

## Intentionally unresolved

- Docker-backed validation and the new Single/Multi 30-VU results remain pending until
  a Docker daemon is available.
- Whether either historical stall reproduces and the exact measured Multi distribution
  will be established by the pending smoke runs.
- Bottleneck conclusions, target metrics, and any optimization remain Stage 4 work.

## Next recommended step

Start Docker, run both topology-validation commands, then run the requested 30-VU
Single and Multi fixed-duration smoke validations. Confirm the Multi readiness output
and `multi-replica-post-traffic.txt`, rerun the complete local verification suite, and
only then review the uncommitted merge result for a merge commit.
