# Checkpoint: Add ingestion load baseline harness

**Date:** 2026-08-23

## Starting point

Stage 4 had a verified Redis-backed global ingestion quota shared by two independent
API hosts. No reproducible load scenario or baseline measurement existed.

## What changed

- Added `tests/performance/ingestion-baseline.js`, a Grafana k6 closed-model scenario for
  `POST /api/events`.
- Each request posts one valid Event Contract v2 record as `application/x-ndjson`.
  It generates a unique `eventId` with `crypto.randomUUID()` and checks for HTTP 202.
- Made `BASE_URL`, `VUS`, and `DURATION` k6 environment variables. The local defaults
  are `http://localhost:5254`, 10 VUs, and 30 seconds.
- Added local-run and result-interpretation instructions in `tests/performance/README.md`,
  including the required high local Redis rate-limit quota.
- Updated the Stage 4 current-implementation record without changing its status.

## Resulting repository state

The repository now has a small, reproducible acceptance-path load harness. It does
not add a .NET load-testing dependency, thresholds, dashboards, production metrics,
or application changes.

The scenario measures the HTTP acceptance path:

```text
API -> Redis rate limiter -> RabbitMQ publisher confirmation -> HTTP 202
```

PostgreSQL persistence occurs asynchronously after RabbitMQ, so this scenario alone
does not establish PostgreSQL persistence throughput. No bottleneck is claimed.

## Verification

Commands to run from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 80 unit tests and 52 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- Grafana k6 was not installed in this environment, so the new scenario could not be
  executed against a local dependency stack here.

## Decisions made

No architecture or product decision changed. This is measurement infrastructure only;
the existing Redis rate limiter remains enabled in the measured request path.

## Intentionally unresolved

- A recorded load baseline and its environment configuration.
- Bottleneck identification, a justified improvement, and a follow-up measurement.
- Target throughput, latency metrics, and performance thresholds or SLOs.

## Next recommended step

Run the documented scenario against the complete local stack with a quota high enough
to avoid HTTP 429 responses, record the observed k6 output and environment details,
then investigate a real bottleneck only if the measurements justify it.
