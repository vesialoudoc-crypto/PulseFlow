# Checkpoint: Record first ingestion performance baseline

**Date:** 2026-08-24

## Starting point

Checkpoint 048 completed the isolated local performance measurement harness. It
produced the required artifacts but no official baseline had been recorded.

## What changed

- Ran the existing default performance scenario three times with `VUS=10` and
  `DURATION=30s`. Each run used the runner's fresh isolated Compose stack.
- Added [Ingestion Performance Baseline 001](../performance/ingestion-baseline-001.md),
  which records the environment, configuration, generated-artifact results, RabbitMQ
  and PostgreSQL observations, and sampled container-resource peaks.
- Updated the Stage 4 current implementation text to link to the first measured
  baseline. Stage 4 remains in progress.

## Resulting repository state

The repository now has a reproducible local baseline for one API instance, one
RabbitMQ consumer, `Ingestion:ChunkCapacity=100`, and a Redis limit of 10,000,000
requests per one-minute window. Across the three generated runs, all HTTP requests
passed the HTTP 202 status check, the final queue samples reached zero, and the final
PostgreSQL row count equalled the accepted request count in each run.

No production code, performance-runner code, Compose configuration, optimization, or
new monitoring infrastructure was changed.

## Verification

Commands run from the repository root:

```powershell
$env:VUS = '10'
$env:DURATION = '30s'
pwsh .\tests\performance\run.ps1

dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
git diff --check
```

Results:

- All three performance runs completed successfully and cleaned up their isolated
  Compose stacks.
- The generated artifacts recorded zero HTTP failures and zero failed status checks
  in every run; final PostgreSQL counts matched accepted requests after queue drain.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 80 unit and 53 integration tests passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed.

## Decisions made

No architecture or product decision changed. The measured values are a local baseline,
not a target, an SLO, a bottleneck finding, or a justification for an optimization.

## Intentionally unresolved

- The component or constraint that should be investigated as a real bottleneck.
- Any justified optimization and its follow-up measurement.
- Target throughput, latency metrics, thresholds, and SLOs.
- Production observability and container/database metrics design.

## Next recommended step

Use this baseline and the existing artifacts to select one focused investigation only
when the evidence supports it, then measure any justified change against the same
scenario.
