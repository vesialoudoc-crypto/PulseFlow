# Checkpoint: Add local performance runner

**Date:** 2026-08-24

## Starting point

Stage 4 had a reusable Grafana k6 ingestion-baseline scenario, but running it
required manually starting and stopping the local Compose stack. k6 output was not
persisted by that workflow, and RabbitMQ queue backlog was not sampled during a run.

## What changed

- Added a one-command local performance-runner workflow through
  `pwsh .\tests\performance\run.ps1`.
- The runner uses the isolated `pulseflow-performance` Compose project. It removes
  any previous stack and its volumes before creating a fresh stack for each run.
- The runner waits for `GET /health/live` at `http://localhost:5254` to return HTTP
  200 before it starts k6, so startup time and transient startup connection failures
  are not part of the load scenario.
- k6 runs the existing `ingestion-baseline.js` scenario and writes its end-of-run
  summary to `tests/performance/results/<timestamp>/k6-summary.json`.
- While k6 runs, the runner samples RabbitMQ's
  `pulseflow.ingestion-batches` queue every two seconds and writes
  `timestamp_utc`, `messages_ready`, `messages_unacknowledged`, and `messages_total`
  to `tests/performance/results/<timestamp>/rabbitmq.csv`.
- Cleanup is in the runner's `finally` path: the isolated Compose stack and its
  volumes are removed after the run, including when startup or k6 fails after the
  stack has been started.
- Updated `tests/performance/README.md` with the one-command workflow and result
  artifact semantics.

## Resulting repository state

The local ingestion-baseline experiment now has an orchestrated fresh-stack workflow
and persistently recorded k6 and RabbitMQ-backlog artifacts. It remains a local
measurement harness; it does not add CI behavior, PostgreSQL metrics, Docker resource
metrics, performance thresholds, or application behavior.

## Verification

Commands run from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 80 unit tests and 53 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `VUS=1`, `DURATION=6s`, and `pwsh ./tests/performance/run.ps1` completed
  successfully. The run waited for the liveness endpoint, saved both artifacts in a
  timestamped results directory, and removed the `pulseflow-performance` Compose
  stack and its volumes. This short orchestration check is not an official
  performance baseline.

## Decisions made

No architecture or product decision changed. The isolated Compose project and result
artifacts support local repeatability only; they do not establish a production
deployment or observability design.

## Intentionally unresolved

- An official performance baseline and the environment configuration used to produce
  it.
- Target throughput, latency metrics, performance thresholds, and SLOs.
- A measured bottleneck, a justified optimization, and a follow-up measurement.
- PostgreSQL and Docker resource metrics.

No official performance baseline or optimization decision has been made yet.

## Next recommended step

Run the documented command in a controlled local environment, retain the result
artifacts with the environment configuration, and use the observed data to decide
whether a bottleneck investigation is justified.
