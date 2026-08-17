# Checkpoint: E2E mixed-record and chunking behavior verified

**Date:** 2026-08-17

## Starting point

[PLAN 002 Step 6](../plans/002-stage-1-ingestion-pipeline.md) already had a reusable
PostgreSQL Testcontainers fixture, a per-test ASP.NET Core host, and two focused E2E
scenarios for successful response accounting and one persisted event. Mixed malformed
and contract-invalid input, plus end-to-end persistence over multiple chunks, remained
unverified.

## What changed

- Configured `PulseFlowWebApplicationFactory` with the generated Testcontainers
  `ConnectionStrings:PulseFlow` value and test-only `Ingestion:ChunkCapacity = 2`.
- Added `PostEvents_MixedRecords_ReturnsExpectedAccounting`, which posts five NDJSON
  records (three valid, one malformed, and one contract-invalid) and verifies HTTP
  normal-completion accounting of total 5, accepted 3, and rejected 2.
- Added `PostEvents_MixedRecords_PersistsValidEventsAcrossChunks`, which posts the
  same logical input and verifies the three valid rows directly through a fresh
  PostgreSQL `DbContext`. With test chunk capacity 2, these rows demonstrate one full
  committed chunk followed by one final partial committed chunk.
- Added narrow test-local JSON and multi-record NDJSON helpers; no production code or
  application configuration changed.
- Updated PLAN 002 Step 6 status text to reflect the newly verified scenarios while
  retaining its in-progress status.

## Resulting repository state

The E2E host retains the real PostgreSQL connection-string override and now also
supplies a deliberately small, test-only chunk capacity. `EventIngestionTests` covers
independent mixed-record accounting and direct observation of valid records persisted
across the full-plus-final-partial chunk boundary. The tests sort database rows by the
deterministic test event type and do not assert generated IDs or receipt times.

PLAN 002 Step 6 and PLAN 002 as a whole remain incomplete.

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

- Focused `EventIngestionTests` passed: 4 passed, 0 failed, 0 skipped.
- `dotnet build PulseFlow.slnx` succeeded with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` passed: 64 unit tests and 33 integration tests, with
  0 failed and 0 skipped.
- `pwsh ./scripts/check-project-docs.ps1` could not start because PowerShell 7 is not
  installed in this environment.
- The same documentation script completed successfully with the available Windows
  PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Decisions made

No architectural decision or ADR was required. The capacity override is test-host
configuration used to exercise an existing accepted chunking behavior; it does not
select an operational chunk size.

## Intentionally unresolved

- The deterministic later-chunk PostgreSQL persistence-failure scenario remains
  pending and was not implemented.
- The remaining PLAN 002 Step 6 cancellation or truncated-final-record coverage
  remains pending.
- Step 6 completion, PLAN 002 completion, and the final Stage 1 review remain
  pending.
- Idempotency, deduplication, retries, production migration execution, request and
  record limits, and all deferred later-stage concerns remain unresolved.

## Next recommended step

Continue PLAN 002 Step 6 with the separately scoped deterministic later-chunk
persistence-failure scenario, without introducing retry or idempotency behavior.
