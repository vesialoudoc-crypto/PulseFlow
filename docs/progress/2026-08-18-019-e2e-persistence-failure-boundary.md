# Checkpoint: E2E persistence-failure boundary verified

**Date:** 2026-08-18

## Starting point

[PLAN 002 Step 6](../plans/002-stage-1-ingestion-pipeline.md) already verified the
normal HTTP-to-real-PostgreSQL ingestion path, mixed-record accounting, and chunked
persistence. Its final focused scenario was still pending: a deterministic later
persistence failure after an earlier chunk had committed.

## What changed

- Added `PostEvents_LaterChunkFails_KeepsEarlierCommittedChunk` to
  `EventIngestionTests`.
- The test creates a disposable `WithWebHostBuilder` variant only for that case and
  replaces `IEventChunkStore` in its test host with
  `FailOnSecondCallEventChunkStore`.
- The test-only store delegates its first call to the real
  `EfCoreEventChunkStore`, so the first two input records are committed through the
  real `PulseFlowDbContext` to PostgreSQL.
- The test-only store throws before delegating its second call. With the existing
  test-only chunk capacity of two, the second chunk is not added or saved.
- Updated PLAN 002 Step 6 wording and status to describe the verified deterministic
  persistence failure accurately. The test does not physically fail, stop, or
  corrupt the PostgreSQL container.

## Resulting repository state

Posting four valid NDJSON events with types `failure-a` through `failure-d` through
the test-specific host produces HTTP 500 via the existing centralized exception
handler. A fresh real-PostgreSQL `DbContext` observes exactly `failure-a` and
`failure-b`; `failure-c` and `failure-d` are absent. No production registration or
production behavior changed, and the normal E2E tests continue to use
`IEventChunkStore` mapped to `EfCoreEventChunkStore`.

PLAN 002 Step 6 is implemented and verified. PLAN 002 remains in progress because
Step 7, the final review and completion checkpoint, remains next.

## Verification

Commands run from the repository root:

```powershell
dotnet test PulseFlow.slnx --filter FullyQualifiedName~EventIngestionTests
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
& .\scripts\check-project-docs.ps1
```

Results:

- Focused `EventIngestionTests` passed: 5 passed, 0 failed, 0 skipped.
- `dotnet build PulseFlow.slnx` succeeded with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` passed: 64 unit tests and 34 integration tests, with
  0 failed and 0 skipped.
- `pwsh ./scripts/check-project-docs.ps1` could not start because PowerShell 7 is
  not installed in this environment.
- The same documentation script completed successfully with the available Windows
  PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Decisions made

No architectural decision or ADR was required. The replacement store is disposable
test infrastructure scoped to one test-host variant, and it preserves the real
PostgreSQL commit boundary for the first chunk.

## Intentionally unresolved

- Step 7 final review, documentation reconciliation, and the PLAN 002 completion
  decision remain pending.
- A separate cancellation or truncated-final-record E2E test is intentionally not
  added: focused `NdjsonRecordReader` tests already verify those framing semantics,
  and such an E2E test would not add an HTTP or persistence guarantee.
- Idempotency, deduplication, retries, production migration execution, request and
  record limits, and all deferred later-stage concerns remain unresolved.

## Next recommended step

Perform PLAN 002 Step 7 final review only; do not begin subsequent roadmap stages
until that review determines PLAN 002 is complete.
