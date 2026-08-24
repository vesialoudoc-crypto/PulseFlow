# Checkpoint: Record post-fix ingestion performance baseline

**Date:** 2026-08-24

## Starting point

Checkpoint 049 recorded Baseline 001 as the historical pre-fix local ingestion
measurement. The current branch already included a RabbitMQ publisher channel pool
with `PublisherChannelCount = 4` and a corrected performance runner that validates
final PostgreSQL persistence against the k6 accepted-HTTP-202 count.

## What changed

- Ran a fresh sequential post-fix `10 VU / 10s`, `20 VU / 10s`, and `30 VU / 10s`
  local sweep. The three generated artifacts are `20260824-182935-412`,
  `20260824-183201-938`, and `20260824-183400-633`, respectively.
- Replaced the Baseline 002 draft with only the three fresh report results.
- Updated [Ingestion Performance Baseline 002](../performance/ingestion-baseline-002.md)
  and the Stage 4 roadmap reference with the final HTTP, RabbitMQ, PostgreSQL, and
  sampled container-resource results.
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

- 10 VU: 28,783 accepted HTTP 202 responses and 28,783 persisted rows.
- 20 VU: 21,505 accepted HTTP 202 responses and 21,505 persisted rows.
- 30 VU: 28,105 accepted HTTP 202 responses and 28,105 persisted rows.

Baseline 001 remains unchanged as the historical pre-fix measurement.

## Verification

Commands run from the repository root:

```powershell
$env:VUS = '10'
pwsh .\tests\performance\run.ps1

$env:VUS = '20'
pwsh .\tests\performance\run.ps1

$env:VUS = '30'
pwsh .\tests\performance\run.ps1
```

Results:

- The 10-VU run completed successfully, wrote its four artifacts, and cleaned up its
  isolated Compose stack. Its `database.txt` records 28,783 accepted HTTP 202
  responses and 28,783 persisted rows.
- The 20-VU run completed successfully, wrote its four artifacts, and cleaned up its
  isolated Compose stack. Its `database.txt` records 21,505 accepted HTTP 202
  responses and 21,505 persisted rows.
- The 30-VU run completed successfully, wrote its four artifacts, and cleaned up its
  isolated Compose stack. Its `database.txt` records 28,105 accepted HTTP 202
  responses and 28,105 persisted rows.
- No application code, runner code, or configuration was modified. The standard
  repository checks were not rerun for this measurement-and-documentation task.

## Decisions made

No architecture or product decision changed. Baseline 002 is a local measured
observation, not a target, SLO, delivery guarantee, bottleneck conclusion, or
justification for an optimization.

## Intentionally unresolved

- The component or constraint that should be investigated as a real bottleneck.
- Any justified optimization and a same-scenario follow-up measurement.
- Target throughput, latency metrics, thresholds, and SLOs.
- Production observability and container/database metrics design.

## Next recommended step

Select one focused, evidence-driven investigation of the final fresh sweep, then
measure only a justified change against the same scenario.
