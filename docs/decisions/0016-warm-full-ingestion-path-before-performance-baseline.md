# ADR 0016: Warm the full ingestion path before a performance baseline

**Date:** 2026-08-25

## Status

Accepted

## Context

The local performance runner originally treated a successful `/health/live` response
as sufficient readiness for a measured ingestion baseline. On fresh Single-topology
30-VU executions, this produced the same false stall in five out of five runs:

- exactly 30 requests completed;
- throughput was about 2 requests/second;
- server waiting time was about 13.6 to 15.1 seconds.

The `/health/live` endpoint itself handled 30 VUs normally at about 16,000
requests/second. HTTP handling and Kestrel were therefore not the capacity limit
demonstrated by the stalled ingestion runs.

A temporary settle period of about 20 seconds removed the symptom. This demonstrated
that measurement began too early, but an arbitrary fixed sleep would make readiness
depend on elapsed time rather than observed system state. The evidence did not prove
which internal component caused the cold initialization delay.

## Alternatives considered

1. Start performance sampling and k6 after liveness succeeds.
2. Add a fixed settle delay after liveness.
3. Warm only the HTTP endpoint before measurement.
4. Traverse the complete ingestion path, clean its exact state, and verify a clean
   baseline before starting measurement.

## Decision

After liveness succeeds and before performance sampling or k6 starts, prepare the
complete ingestion path with one uniquely identified warm-up event:

```text
HTTP warm-up request
-> Redis limiter
-> RabbitMQ publish
-> RabbitMQ consumer
-> PostgreSQL persistence
-> verify queue drained
-> delete exact warm-up row
-> reset only ingestion Redis limiter key
-> verify clean database and queue state
-> start measured baseline
```

The runner requires HTTP 202 for the warm-up request, observes the exact event in
PostgreSQL, and verifies that the RabbitMQ main queue has zero ready and zero
unacknowledged messages. It then deletes only that warm-up row and only the ingestion
limiter key, `pulseflow:rate-limit:ingestion:global`. Before measurement begins, it
verifies zero PostgreSQL event rows and an empty RabbitMQ main queue.

Liveness means that the process can serve the health endpoint. Liveness alone is not
sufficient measurement readiness for this ingestion baseline. Baseline measurements
exclude cold initialization effects unless cold-start performance is explicitly the
subject being measured. Arbitrary sleeps are not accepted as readiness criteria.

## Consequences

- Measurement starts only after an observed traversal of the HTTP, Redis, RabbitMQ,
  consumer, and PostgreSQL path.
- The warm-up request runs outside k6 and before performance sampling.
- Warm-up state cannot contaminate measured HTTP request counts, persisted-row
  counts, Redis quota, or RabbitMQ backlog; cleanup and zero-state checks are required
  before the baseline starts.
- The baseline represents the warmed ingestion path, not cold-start performance.
- A failure to traverse, drain, clean, or verify the warm-up path prevents the
  measured baseline from starting.
- No conclusion is made about which internal component caused the observed cold
  initialization delay.

## Validation

After the deterministic warm-up was added, a fresh Single 30-VU fixed 10-second run
completed with:

- 27,831 requests;
- 2,780.84 requests/second;
- 10.67 ms average latency;
- 15.39 ms p95 latency;
- 0% HTTP failures;
- 27,831 accepted requests and 27,831 persisted rows.

The previous stall did not reproduce.

## Intentionally deferred

- Identification of the internal source of the cold initialization delay.
- Cold-start performance as a separately defined measurement subject.
- Bottleneck conclusions, performance targets, and optimization decisions.
