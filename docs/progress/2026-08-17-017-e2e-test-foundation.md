# Checkpoint: E2E ingestion test foundation established

**Date:** 2026-08-17

## Starting point

[PLAN 002 Step 5](2026-08-17-016-http-ingestion-boundary.md) had implemented the
public NDJSON ingestion boundary, but did not yet exercise the real hosted HTTP path
against PostgreSQL. The working tree already contained a reusable
`PostgreSqlFixture`, a `PulseFlowWebApplicationFactory<Program>`, and an experimental
one-request `WebAplicationTests` smoke test.

## What changed

- Reused the existing PostgreSQL Testcontainers fixture and application factory; no
  competing container lifecycle was introduced.
- Renamed the experimental test class and file to `EventIngestionTests` and made it
  implement `IClassFixture<PostgreSqlFixture>` and `IAsyncLifetime`.
- Moved reusable per-test setup into `InitializeAsync`: construct the factory using
  the fixture connection string, apply committed EF Core migrations, delete existing
  `EventRecords` with `ExecuteDeleteAsync`, and create the HTTP client.
- Made `DisposeAsync` release the HTTP client and application factory only. The
  shared fixture retains ownership of the PostgreSQL container.
- Added `PostEvents_ValidEvent_ReturnsAcceptedResult`, which verifies the HTTP 200
  normal-completion accounting for a valid Event Contract v1 NDJSON record.
- Added `PostEvents_ValidEvent_PersistsEvent`, which reads with a fresh scoped
  `PulseFlowDbContext` and `AsNoTracking()` after the request to verify the committed
  row's representative fields and semantic JSON payload content.
- Updated PLAN 002 to state accurately that only the focused Step 6 foundation is in
  progress. Step 6 and PLAN 002 remain incomplete.

## Resulting repository state

Every `EventIngestionTests` case creates its own test-host resources while reusing the
class fixture's PostgreSQL container. After migrations run, central
`ExecuteDeleteAsync` cleanup leaves the `events` table empty before the test creates
its scenario-specific NDJSON content. The application then handles a real HTTP
request and commits through its regular request-scoped EF Core path.

The test-owned response DTO avoids changing the production `IngestEventsResult`, whose
constructor remains internal. The persistence scenario deliberately uses a fresh
scope and `DbContext`, rather than an EF Core change tracker retained from setup.

No production application behavior changed. The existing top-level `Program` type was
already accessible to `WebApplicationFactory`, so no test-host exposure declaration
was required.

## Verification

Required commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds with 64 unit tests and 31 integration tests
  passed, 0 failed, and 0 skipped. The two additional integration tests are the
  focused `EventIngestionTests` scenarios.
- The focused `EventIngestionTests` filter also passes with 2 tests passed.
- `pwsh ./scripts/check-project-docs.ps1` could not start because `pwsh` is not
  installed in this environment. The same script completed successfully with the
  available Windows PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Decisions made

No architectural decision or ADR was required. Applying committed migrations and
cleaning test rows in per-test setup is a bounded test implementation detail and does
not select a production migration strategy.

## Intentionally unresolved

- The remaining PLAN 002 Step 6 scenarios: mixed malformed/invalid records, multiple
  chunks, deterministic mid-request PostgreSQL failure, and useful deterministic
  cancellation or truncated-final-record coverage.
- Step 6 completion, PLAN 002 completion, and the final Stage 1 review.
- Idempotency, deduplication, retries, production migration execution, request and
  record limits, and all deferred later-stage concerns.

## Next recommended step

Continue PLAN 002 Step 6 with one additional focused HTTP-to-PostgreSQL scenario at a
time. Keep the reusable per-test factory, migrations, and cleanup in
`EventIngestionTests`, and do not add deferred production or reliability mechanisms.
