# Checkpoint: Record Single vs Multi ingestion comparison

**Date:** 2026-08-25

## Starting point

Commit `96ebc94` had the accepted Single and Multi local topologies, the
application-owned readiness boundary, and the completed controlled 10-second k6
measurement sweep awaiting documentation. Historical Baseline 001 and Baseline 002
were already recorded and remain historical measurements.

## What changed

- Added [Ingestion Single vs Multi Comparison 001](../performance/ingestion-single-vs-multi-001.md)
  with the exact 10, 20, and 30 VU results, methodology, comparison table, and
  conservative conclusions.
- Linked the comparison from the performance-runner documentation.
- Updated the Stage 4 current implementation record with the measured result and
  its limits.

## Resulting repository state

The repository now documents a controlled comparison between one-process Single
ingestion and two-process Multi ingestion. In all six valid runs, 0% HTTP failures
occurred and the accepted HTTP 202 count equalled the final PostgreSQL persisted-row
count after RabbitMQ drained.

At 10 VUs Multi was 3.63% lower in accepted request rate than Single. At 20 and
30 VUs Multi was 21.59% and 16.05% higher, respectively. The results are not an
isolated HTTP/API-scaling measurement because Multi increases both API processes and
RabbitMQ consumers from one to two. The post-burst RabbitMQ drain times of roughly
74-101 seconds also distinguish HTTP acceptance throughput from sustainable
end-to-end persistence throughput.

## Verification

- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit tests and 57 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed.
- No k6 command or workload was run for this documentation-only step.

## Decisions made

No new architectural decision was made. The results do not establish linear scaling,
2x scaling, production capacity, a production instance requirement, or a proven
RabbitMQ bottleneck. High RabbitMQ CPU and backlog remain evidence for a future
bottleneck investigation only.

## Intentionally unresolved

- The causal source of the observed throughput and backlog limits is not proven.
- A bottleneck investigation, performance target, optimization choice, and
  production capacity requirement remain undecided.

## Next recommended step

Design a focused, reproducible bottleneck investigation before selecting an
optimization or making further scaling claims.
