# Checkpoint: Record post-fix ingestion performance baseline

**Date:** 2026-08-24

## Starting point

Checkpoint 049 recorded Baseline 001 as the historical pre-fix local ingestion
measurement. The current branch already included a RabbitMQ publisher channel pool
with `PublisherChannelCount = 4`, a corrected performance runner that validates final
PostgreSQL persistence against the k6 accepted-HTTP-202 count, and a valid successful
30-VU post-fix artifact at `20260824-172255-297`.

## What changed

- Ran the post-fix `10 VU / 10s` and `20 VU / 10s` local scenarios and retained their
  generated artifacts at `20260824-172741-703` and `20260824-172538-229`.
- Reused the valid post-fix `30 VU / 10s` artifact at `20260824-172255-297`; it was
  not rerun.
- Added [Ingestion Performance Baseline 002](../performance/ingestion-baseline-002.md)
  with HTTP, RabbitMQ, PostgreSQL, and sampled container-resource results for all
  three levels.
- Updated the Stage 4 roadmap state to distinguish immutable pre-fix Baseline 001
  from the current post-fix Baseline 002.

## Resulting repository state

Baseline 002 records one API instance, `RabbitMq:ConsumerCount = 1`,
`RabbitMq:PublisherChannelCount = 4`, and `Ingestion:ChunkCapacity = 100`. Its three
rows use the corrected completion verification: after k6, the runner requires an
empty ingestion queue and equality between accepted HTTP 202 responses and persisted
PostgreSQL rows.

All included rows have zero HTTP failures and zero failed HTTP-202 checks. The
accepted and persisted counts are equal at every level:

- 10 VU: 27,898 accepted HTTP 202 responses and 27,898 persisted rows.
- 20 VU: 21,813 accepted HTTP 202 responses and 21,813 persisted rows.
- 30 VU: 30 accepted HTTP 202 responses and 30 persisted rows.

Baseline 001 remains unchanged as the historical pre-fix measurement.

## Verification

Commands run from the repository root:

```powershell
$env:VUS = '10'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1

$env:VUS = '20'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1
```

Results:

- The valid 10-VU run completed successfully, wrote its four artifacts, and cleaned
  up its isolated Compose stack. Its `database.txt` records 27,898 accepted HTTP 202
  responses and 27,898 persisted rows.
- The 20-VU run completed successfully, wrote its four artifacts, and cleaned up its
  isolated Compose stack. Its `database.txt` records 21,813 accepted HTTP 202
  responses and 21,813 persisted rows.
- The existing 30-VU artifact was checked before reuse. Its k6 summary records 30
  successful HTTP-202 checks and its `database.txt` records 30 accepted HTTP 202
  responses and 30 persisted rows.
- No application code, runner code, or configuration was modified. The standard
  repository checks were not rerun for this measurement-and-documentation task.

## Decisions made

No architecture or product decision changed. Baseline 002 is a local measured
observation, not a target, SLO, delivery guarantee, bottleneck conclusion, or
justification for an optimization.

## Intentionally unresolved

- The cause of the 30-VU run's 30 completed requests, long HTTP latency, and lack of
  sampled RabbitMQ backlog.
- The component or constraint that should be investigated as a real bottleneck.
- Any justified optimization and a same-scenario follow-up measurement.
- Target throughput, latency metrics, thresholds, and SLOs.
- Production observability and container/database metrics design.

## Next recommended step

Select one focused, evidence-driven investigation of the post-fix 30-VU observation,
then measure only a justified change against the same scenario.
