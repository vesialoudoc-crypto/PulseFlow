# Checkpoint: Record first ingestion performance baseline

**Date:** 2026-08-24

## Starting point

Checkpoint 048 completed the isolated local performance measurement harness. It
produced the required artifacts but no correctly shaped official baseline had been
recorded.

## What changed

- Replaced the incorrect repeated `VUS=10`, `DURATION=30s` baseline shape with a
  one-run-per-level load sweep: `10 VU / 10s`, `20 VU / 10s`, and `30 VU / 10s`.
  Each measured run used the existing runner's fresh isolated Compose stack.
- Rewrote [Ingestion Performance Baseline 001](../performance/ingestion-baseline-001.md)
  to compare the three load levels, including generated-artifact results, RabbitMQ
  and PostgreSQL observations, and sampled container-resource peaks.
- Updated the Stage 4 current implementation text to describe the load sweep. Stage 4
  remains in progress.

## Resulting repository state

The repository now has a reproducible local load-sweep baseline for one API instance,
one RabbitMQ consumer, `Ingestion:ChunkCapacity=100`, and a Redis limit of 10,000,000
requests per one-minute window. The 10-VU and 20-VU runs had zero HTTP failures and
failed status checks; their final queue samples reached zero and final PostgreSQL row
counts equalled accepted requests. The 30-VU run also had zero HTTP failures and
failed status checks, but its final PostgreSQL row count was zero and all sampled
RabbitMQ backlog values were zero. This result is recorded as an observation requiring
investigation, not as a successful persistence result.

No production code, performance-runner code, Compose configuration, optimization, or
new monitoring infrastructure was changed.

## Verification

Commands run from the repository root:

```powershell
$env:VUS = '10'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1

$env:VUS = '20'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1

$env:VUS = '30'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1

dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
git diff --check
```

Results:

- The three measured performance runs completed successfully and cleaned up their
  isolated Compose stacks. A separate first 30-VU startup attempt ended at API
  liveness with a local transport-connection abort and was rerun; it is not a
  baseline sample.
- The generated artifacts recorded zero HTTP failures and zero failed status checks
  at every measured load level. Final PostgreSQL counts matched accepted requests at
  10 VU and 20 VU. At 30 VU, the final PostgreSQL count was zero and no RabbitMQ
  backlog was sampled; this is documented without an inferred cause.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 80 unit and 53 integration tests passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed.

## Decisions made

No architecture or product decision changed. The measured values are a local load
sweep, not a target, an SLO, a root-cause finding, or a justification for an
optimization.

## Intentionally unresolved

- The cause of the 30-VU result: successful HTTP 202 checks with zero final persisted
  rows and no sampled RabbitMQ backlog.
- The component or constraint that should be investigated as a real bottleneck.
- Any justified optimization and its follow-up measurement.
- Target throughput, latency metrics, thresholds, and SLOs.
- Production observability and container/database metrics design.

## Next recommended step

Use this baseline and the existing artifacts to select one focused investigation of
the 30-VU result, then measure any justified change against the same scenario.
