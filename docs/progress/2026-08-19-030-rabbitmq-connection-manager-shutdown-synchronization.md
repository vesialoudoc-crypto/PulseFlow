# Checkpoint: RabbitMQ connection-manager shutdown synchronization

**Date:** 2026-08-19

## Starting point

The Stage 2 messaging-boundary refactor had introduced
`RabbitMqConnectionManager` as the owner of the application RabbitMQ connection.
Its initialization was serialized by `SemaphoreSlim`, but `DisposeAsync` did not use
that gate and disposed it. Initialization could therefore race with shutdown, and a
later `InitializeAsync` after disposal could report an unrelated missing-connection
error instead of `ObjectDisposedException`.

## What changed

- Removed the unlocked initialization fast path.
- Made `DisposeAsync` acquire and release the same initialization gate as
  `InitializeAsync`.
- Kept the synchronization primitive alive for the manager lifetime, so queued
  callers can complete safely and observe the disposed state.
- Updated the setup comment to refer to publisher and worker components rather than
  multiple hosted parser services.
- Added a focused test that verifies initialization after disposal throws
  `ObjectDisposedException` without attempting to connect to RabbitMQ.

## Resulting repository state

RabbitMQ connection initialization and disposal are now mutually exclusive. A caller
that reaches initialization after shutdown acquires the gate and receives
`ObjectDisposedException`; it does not observe a successful initialization result with
a null connection.

This changes only resource-lifecycle safety. It does not change HTTP acceptance,
acknowledgement, retry, requeue, dead-letter, Outbox, idempotency, or delivery
semantics.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 105 passed, 0 failed, 0 skipped (68 unit and 37
  integration tests).
- Documentation validation completed successfully through Windows PowerShell. The
  requested `pwsh` executable is unavailable in this environment.

## Decisions made

- The existing initialization gate also governs shutdown because both operations own
  the same connection lifecycle.
- The gate is intentionally not disposed. Its negligible managed-memory cost avoids
  shutdown races with queued callers.
- No ADR is required because this is a correctness fix within the accepted Stage 2
  resource ownership model.

## Intentionally unresolved

- Retry, requeue, dead-letter, poison-message, and final acknowledgement policies.
- Outbox, idempotency, deduplication, and delivery guarantees.
- Batch-status/result storage and public status/query APIs.
- Redis, authentication, load testing, performance targets, and deployment topology.

## Next recommended step

Define the smallest Stage 3 reliability decision and its verifiable failure scenario.
Do not select retry, requeue, DLQ, Outbox, or idempotency mechanisms before that
scenario and its acceptance criteria are explicit.
