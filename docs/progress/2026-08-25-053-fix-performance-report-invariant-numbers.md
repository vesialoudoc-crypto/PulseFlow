# Checkpoint: Parse performance report numbers invariantly

**Date:** 2026-08-25

## Starting point

The deterministic ingestion warm-up had corrected the cold-start performance-runner
stall. Its successful Single 30-VU result was retained in
`tests/performance/results/20260825-011031-060/`.

The raw k6 console and `k6-summary.json` reported correct decimal values, but the
runner's final convenience report displayed some decimal metrics incorrectly on hosts
whose current culture uses a comma decimal separator. The conversion helper first
called `.ToString()` using the host culture, then parsed that culture-specific text as
invariant. Invariant parsing interpreted a comma as a thousands separator.

## What changed

- Changed `Get-RequiredNonNegativeDouble` to convert incoming numeric values to text
  through `Convert.ToString(value, CultureInfo.InvariantCulture)` before parsing.
- Parse that invariant machine-generated text with
  `NumberStyles.Float` and `CultureInfo.InvariantCulture`, which accepts invariant
  decimal and exponent notation but not thousands separators.

## Resulting repository state

The report parser and its existing invariant output formatting now use the same
numeric convention for k6 JSON, RabbitMQ CSV, container CSV, and database-verification
artifact values. The load workload, fixed 10-second duration, VU handling, topology,
warm-up, samplers, Redis cleanup, application code, and performance methodology are
unchanged.

## Verification

No performance workload was rerun. The existing successful 30-VU k6 summary was read
directly under both `en-US` and `pl-PL` current cultures through the updated helper.

Results for both cultures:

- requests/sec: `2780.84`;
- average latency: `10.67 ms`;
- p95 latency: `15.39 ms`;
- HTTP failures: `0.00%`.

The PowerShell parser validation passed and `git diff --check` passed. Standard
repository validation also passed: CSharpier checked 40 files, the build completed
with 0 warnings and 0 errors, all 82 unit and 53 integration tests passed, and project
documentation checks passed.

## Decisions made

No product or architectural decision changed. This is a deterministic presentation
and machine-artifact parsing correction in the existing local performance harness; no
ADR is required.

## Intentionally unresolved

- The measured bottleneck, target metrics, and any later optimization remain Stage 4
  work.

## Next recommended step

Use the corrected report output in separately requested Single or Multi performance
measurements when investigating the Stage 4 scaling question.
