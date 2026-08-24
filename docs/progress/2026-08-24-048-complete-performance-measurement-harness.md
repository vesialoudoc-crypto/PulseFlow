# Checkpoint: Complete local performance measurement harness

**Date:** 2026-08-24

## Starting point

Checkpoint 047 provided an isolated local performance runner with a k6 summary and
RabbitMQ backlog samples. It did not wait for asynchronous processing to complete,
record the final PostgreSQL persisted row count, or capture resource use by the local
containers.

## What changed

- Extended `pwsh .\tests\performance\run.ps1` with the following completed local
  measurement workflow:

  ```text
  fresh isolated Compose stack
  -> API liveness gate
  -> k6 load
  -> k6-summary.json
  -> RabbitMQ backlog sampling
  -> queue drain
  -> final PostgreSQL persisted row count
  -> container CPU/RAM sampling
  -> automatic cleanup
  ```

- Added `tests/performance/results/<timestamp>/database.txt`, which records the final
  PostgreSQL persisted row count and its UTC capture time.
- After k6 completes successfully, the runner waits for the RabbitMQ main queue to
  be fully drained (`messages_ready = 0` and `messages_unacknowledged = 0`) before it
  reads PostgreSQL. Consumers acknowledge deliveries only after persistence completes,
  so the final row count is read after the asynchronous persistence work finishes.
- Added `tests/performance/results/<timestamp>/containers.csv` with
  `timestamp_utc`, `service`, `cpu_percent`, and `memory_usage_mb` columns. It records
  the `api`, `rabbitmq`, `redis`, and `postgres` services.
- Container sampling starts with k6 and continues through RabbitMQ drain, so resource
  use from asynchronous persistence is captured after HTTP load stops.
- Updated `tests/performance/README.md` with the new artifact semantics and sampling
  boundaries.

## Resulting repository state

The isolated local runner now produces four artifacts for each successful run:

- `k6-summary.json`;
- `rabbitmq.csv`;
- `database.txt`;
- `containers.csv`.

The runner begins with a fresh `pulseflow-performance` Compose stack, gates k6 on API
liveness, preserves RabbitMQ and container time-series data through queue drain, reads
PostgreSQL only after the main queue is empty, and removes the isolated stack and its
volumes automatically. This remains local measurement infrastructure only: it adds no
CI behavior, application metrics, or runtime behavior.

## Verification

Commands run from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
git diff --check
```

Results:

- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed.
- The full default `VUS=10`, `DURATION=30s` run completed successfully with 36,557
  HTTP 202 responses and 36,557 persisted rows after RabbitMQ drain. All four expected
  artifacts were produced, and cleanup completed.

These numbers verify the harness only. They are not an official performance baseline,
and no optimization decision is made from them.

## Decisions made

No architecture or product decision changed. The drain-before-database-read ordering
uses the existing acknowledgement-after-persistence behavior solely to make the local
measurement result meaningful; it does not add a delivery guarantee or an
observability design.

## Intentionally unresolved

- An official performance baseline and the environment configuration used to produce
  it.
- Target throughput, latency metrics, performance thresholds, and SLOs.
- A measured bottleneck, a justified optimization, and a follow-up measurement.
- Any production observability, container-metrics collection, or database-metrics
  design.

## Next recommended step

Retain a controlled local run together with its environment configuration, then use
the recorded artifacts to investigate a bottleneck only if the evidence justifies it.
