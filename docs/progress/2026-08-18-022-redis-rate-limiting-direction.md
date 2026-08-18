# Checkpoint: Accepted Redis direction for future distributed ingestion rate limiting

**Date:** 2026-08-18

## Starting point

Stage 1 remained the implemented synchronous ingestion path. Stage 2 remained not
started, with RabbitMQ accepted as its future asynchronous work-transfer broker and
PLAN 003 defining its first bounded slice. Redis was deferred without an accepted
concrete use case.

## What changed

- Recorded Redis as a required learning and portfolio technology with one concrete
  future responsibility: distributed ingestion rate limiting and shared quota state
  across multiple `PulseFlow.Api` instances.
- Placed Redis implementation in Stage 4, when multiple API instances are introduced.
- Clarified that process-local in-memory counters are insufficient once requests are
  distributed across instances, and that Redis is not the primary event store, a
  generic cache, or batch-status storage.
- Recorded the intended boundary: an over-limit request returns HTTP 429, includes
  `Retry-After` where appropriate, and is not published to RabbitMQ.
- Kept Stage 2 and PLAN 003 focused on RabbitMQ asynchronous ingestion. No Redis
  implementation work was added to that stage.

## Resulting repository state

Redis remains absent from the implemented architecture and configuration. It is a
future Stage 4 component only, to provide shared fast-changing rate-limit/quota state
when multiple `PulseFlow.Api` instances handle requests independently.

No production code, tests, package references, Docker configuration, or application
configuration changed.

## Decisions made

- Redis is required for the project's learning and portfolio scope.
- Its accepted planned role is distributed ingestion rate limiting across multiple
  `PulseFlow.Api` instances.
- No ADR was created: this checkpoint places an accepted future technology role in the
  roadmap and does not select an implementation algorithm or infrastructure design.

## Verification

Commands run from the repository root after the documentation changes:

```powershell
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `pwsh ./scripts/check-project-docs.ps1` could not start because PowerShell 7 is not
  installed in this environment.
- The equivalent documentation-validation script completed successfully with the
  available Windows PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Intentionally unresolved

- Rate-limiting algorithm and quota values.
- Time-window strategy.
- Redis commands or script implementation.
- Redis failure behavior.
- The exact `Retry-After` policy.
- Stage 4 instance count and load-test targets.

## Next recommended step

Continue with PLAN 003 Step 1 for Stage 2 asynchronous ingestion. Redis work begins
only in Stage 4 when multiple `PulseFlow.Api` instances are introduced.
