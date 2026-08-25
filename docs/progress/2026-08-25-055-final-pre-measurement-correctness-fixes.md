# Checkpoint: Final pre-measurement correctness fixes

**Date:** 2026-08-25

## Starting point

Commit `4614b0a4f9d82fe34f109511f0e210b1c90990dc` had the accepted Single and
Multi performance topologies, application-owned readiness, and a fixed 10-second
k6 workload. The Multi runner accumulated replica readiness successes across polling
cycles, individual readiness probes had no explicit timeout, and repository-root
`docker compose up` included only shared dependencies rather than the normal Multi
topology.

## What changed

- Changed Multi readiness polling to create a fresh result set in every evaluation
  cycle. Measurement can begin only when `api-1` and `api-2` both return HTTP 200
  from `/health/ready` in that same cycle.
- Added a one-second timeout to the Single `Invoke-WebRequest` readiness probe and
  to each Multi internal `wget` readiness probe. The existing two-second polling
  interval and two-minute global readiness deadline remain unchanged.
- Restored the repository-root Compose entry point by including the existing shared
  Compose file and existing Multi Compose file. No shared service configuration was
  duplicated; root `docker compose up` now starts `HAProxy -> api-1 / api-2`.
- Updated the performance-runner and architecture documentation to describe the
  simultaneous-cycle and bounded-probe behavior.

## Resulting repository state

The explicit `compose.single.yaml` and `compose.multi.yaml` files remain the
performance runner's topology inputs. The repository-root `compose.yaml` is the
normal local Multi entry point and publishes HAProxy on port 5254; API replicas remain
internal-only.

The runner still uses application-owned readiness rather than artificial waits or
synthetic ingestion. It retains the fixed 10-second k6 workload, configured VU
handling, HAProxy round-robin behavior, `/health/ready` path, dependency settings,
and reporting behavior.

## Verification

- PowerShell AST parsing of `tests/performance/run.ps1` passed.
- `docker compose config` passed and resolved `haproxy`, `api-1`, `api-2`,
  `migrations`, `postgres`, `rabbitmq`, and `redis` from the repository root.
- `pwsh ./tests/performance/run.ps1 -Topology Single -ValidateTopology` passed.
- `pwsh ./tests/performance/run.ps1 -Topology Multi -ValidateTopology` passed.
- `docker compose up --build --detach` passed. The requested root services
  `haproxy`, `api-1`, `api-2`, `rabbitmq`, `redis`, and `postgres` were all running,
  and `http://localhost:5254/health/ready` returned HTTP 200.
- The runner's bounded internal HAProxy `wget -T 1` readiness probe passed for both
  `api-1` and `api-2` in the running root topology.
- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit tests and 57 integration tests.
- No k6 command or workload was run.

## Decisions made

No new architectural decision was made. These changes correct the existing accepted
readiness and local-topology behavior documented in
[ADR 0015](../decisions/0015-separate-startup-initialization-from-runtime-readiness.md)
and [ADR 0016](../decisions/0016-use-haproxy-for-local-multi-instance-api-ingress.md).

## Intentionally unresolved

- No new performance measurement, bottleneck conclusion, target, or optimization
  decision has been made.
- The next k6 measurement remains deliberately pending.

## Next recommended step

Review the final pre-measurement diff, then run the planned fixed-duration k6
measurements with the explicit Single and Multi topologies.
