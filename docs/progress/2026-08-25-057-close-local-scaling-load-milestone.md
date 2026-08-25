# Checkpoint: Close local scaling and load milestone

**Date:** 2026-08-25

## Starting point

Commit `c7df48848a743b92605ce57337176774aac511a3` recorded the completed controlled
Single-vs-Multi `10 / 20 / 30 VU` ingestion comparison. The local Stage 4 topology,
readiness work, and historical performance baselines already existed. Stage 4 was
still marked in progress because its original roadmap wording included a bottleneck
investigation and before/after optimization cycle.

## What changed

- Marked Stage 4 as completed for its local horizontal-scaling/load scope.
- Consolidated the completed evidence: Redis-backed shared ingestion quota, multiple
  API instances, HAProxy local ingress, readiness/liveness, reproducible Single and
  Multi performance execution, and controlled Single-vs-Multi measurements at 10, 20,
  and 30 VUs.
- Recorded that every valid measured run had 0% HTTP failures and equal accepted HTTP
  202 and final PostgreSQL persisted-row counts.
- Deferred the bottleneck investigation and justified before/after optimization
  objective to the deployed-environment performance phase, after deployment.
- Marked Stage 5 deployment as the next active major stage.

## Resulting repository state

The local Stage 4 horizontal-scaling/load milestone is complete. The measurements are
useful local architecture and load baselines, not production-capacity or linear-scaling
claims. Multi changes both API-process count and co-hosted RabbitMQ-consumer count,
so its result does not isolate API or ingress scaling. RabbitMQ CPU/backlog observations
remain investigation leads, not proof that RabbitMQ is the bottleneck.

Further bottleneck attribution and local tuning are deliberately deferred because the
API, RabbitMQ, PostgreSQL, Redis, and HAProxy share one local Docker Desktop host and
its CPU, RAM, disk, and virtualization resources. The next sequence is:

```text
local topology baseline -> deployment -> deployed load measurement
-> bottleneck investigation -> justified improvement -> before/after
```

The immediate next task is to design and implement a minimal deployment architecture
from the already working application without prematurely selecting specific AWS
services, network topology, or other infrastructure.

No application, runtime, Compose, HAProxy, Redis, RabbitMQ, PostgreSQL, readiness, or
performance-runner behavior changed in this documentation-only milestone closure.

## Verification

- `dotnet csharpier check .` passed: 54 files checked.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed.
- Build and automated tests were not run because this task changes documentation only.
- No k6 command or workload was run.

## Decisions made

No new application or deployment architecture decision was made, and no ADR was
required. The roadmap now records the accepted planning sequence that moves the
existing bottleneck/optimization objective to the deployed-environment performance
phase.

## Intentionally unresolved

- The minimal AWS deployment architecture, services, network topology, environments,
  release strategy, cost model, and operational configuration remain undecided.
- Target performance metrics and production capacity remain undecided.
- The causal source of the observed local throughput/backlog limits remains unproven.
- The deployed bottleneck investigation, justified improvement, and before/after
  comparison remain future work.

## Next recommended step

Define and implement the minimal deployment architecture for the existing working
application, documenting significant choices as they are accepted. After deployment,
run a reproducible deployed load measurement before investigating and optimizing a
measured bottleneck.
