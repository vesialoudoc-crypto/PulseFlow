# Checkpoint: Warm the performance ingestion path

**Date:** 2026-08-25

## Starting point

The topology-aware performance runner started RabbitMQ and container sampling, then
k6, immediately after the selected ingress returned HTTP 200 from `/health/live`.
Repeated fresh Single-topology 30-VU runs could therefore stall during downstream
startup even though the API was live. A manually delayed run demonstrated that the
fixed workload itself could complete normally once the ingestion path was ready.

The background-job cleanup also passed null sampler variables to `Stop-Job`,
`Wait-Job`, and `Remove-Job`, which could add a cleanup error after an earlier failure.

## What changed

- Added one pre-measurement Event Contract v2 NDJSON request through the selected
  Single or Multi ingress after liveness succeeds.
- Generate a new GUID for each warm-up and use the dedicated source
  `performance-runner-warm-up`.
- Require HTTP 202, poll until that exact `(source, event_id)` is present in
  PostgreSQL, and then require the RabbitMQ main queue to report both
  `messages_ready = 0` and `messages_unacknowledged = 0`.
- Delete exactly the warm-up row by its source and event ID. Delete exactly the Redis
  key `pulseflow:rate-limit:ingestion:global` and verify that it no longer exists.
- Require total PostgreSQL event count 0 and RabbitMQ ready/unacknowledged 0 before
  starting either performance sampler or k6.
- Made final sampler cleanup null-safe while retaining the existing stop, wait, and
  forced removal sequence for jobs that were created.
- Documented the pre-measurement lifecycle in the performance README and Stage 4
  roadmap state.

## Resulting repository state

The runner now uses an observed end-to-end ingestion result, rather than elapsed time,
as the readiness boundary:

```text
selected ingress -> Redis limiter -> RabbitMQ publish/consume -> PostgreSQL row
                -> queue drain -> targeted DB/Redis cleanup -> zero-state checks
                -> performance samplers -> fixed 10-second k6 workload
```

The warm-up request is made outside k6, and its PostgreSQL row, Redis quota increment,
and RabbitMQ activity are removed or completed before sampling. It therefore does not
contribute to k6 request or accepted counts, final baseline rows, or sampled RabbitMQ
baseline backlog.

Application code, dependency configuration, publisher/consumer counts, HAProxy,
the k6 payload, VU handling, topology behavior, and the fixed duration are unchanged.

## Verification

Repository validation was run before implementation from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
```

Results:

- CSharpier checked 40 files successfully.
- The solution built with 0 warnings and 0 errors.
- All 82 unit tests and all 53 integration tests passed.
- Project documentation checks passed.

Only the requested load scenario was run:

```powershell
$env:VUS = '30'
pwsh .\tests\performance\run.ps1 -Topology Single
```

Result directory: `tests/performance/results/20260825-011031-060/`.

Results:

- Warm-up HTTP status: 202.
- Warm-up event `2ba166f5-2d25-45e6-b5ce-63c8206b292d` reached PostgreSQL.
- Warm-up queue drained to ready 0 and unacknowledged 0.
- Exactly one warm-up row and one Redis limiter key were deleted; key absence was
  verified.
- Immediately before k6, PostgreSQL contained 0 events and RabbitMQ reported 0 ready
  and 0 unacknowledged messages.
- k6 ran 30 VUs for the fixed 10 seconds: 27,831 requests, 2,780.84 requests/second,
  10.67 ms average latency, 13.32 ms p90, 15.39 ms p95, and 45.14 ms maximum.
- All 27,831 status checks received HTTP 202; HTTP failure rate was 0%.
- RabbitMQ then drained and PostgreSQL contained 27,831 rows, so accepted equaled
  persisted.
- The previous stall did not reproduce.
- The runner exited 0, removed the isolated stack and its volumes, and produced no
  null-job cleanup error.

The locale-sensitive numeric formatting in the runner's final convenience report
rendered decimal metrics incorrectly on this host. The k6 console output and
`k6-summary.json` contain the correct metrics recorded above; counts and equality were
unaffected.

The same four repository validation commands were run again after the completed diff.
CSharpier again checked 40 files, the build again completed with 0 warnings and
0 errors, all 82 unit and 53 integration tests passed, and documentation checks
passed.

## Decisions made

The accepted measurement-readiness rule is recorded in
[ADR 0017](../decisions/0017-warm-full-ingestion-path-before-performance-baseline.md).
Liveness alone is insufficient for this ingestion baseline: the harness must
successfully traverse and clean the complete ingestion path, then verify clean
database and queue state. Arbitrary fixed delays are not accepted as readiness
criteria.

## Intentionally unresolved

- The measured bottleneck, target metrics, and any later optimization remain Stage 4
  work.
- The existing locale-sensitive formatting of decimal values in the final convenience
  report should be corrected separately without changing stored k6 measurements.

## Next recommended step

Correct the locale-sensitive presentation of k6 decimal metrics, then use separately
requested Single and Multi measurements to investigate the Stage 4 scaling question.
