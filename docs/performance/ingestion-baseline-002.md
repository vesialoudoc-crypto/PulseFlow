# Ingestion Performance Baseline 002

**Measured:** 2026-08-24 (local time, Europe/Warsaw)

## Purpose

Baseline 002 is a fresh post-fix local load sweep. It does not supersede
[Baseline 001](ingestion-baseline-001.md), which remains the historical pre-fix
record at commit `ad1da229afeb36dbdf17e7a18d780196a137abd6`.

The three rows below come only from the fresh runs captured in these artifact
directories:

- `tests/performance/results/20260824-182935-412` (10 VU)
- `tests/performance/results/20260824-183201-938` (20 VU)
- `tests/performance/results/20260824-183400-633` (30 VU)

## Scenario and configuration

The local runner used its isolated `pulseflow-performance` Compose stack. Each run
started with fresh volumes and removed the stack and volumes after artifacts were
captured. The closed-model k6 scenario continuously sent one valid Event Contract v2
NDJSON record per request to `POST /api/events` and checked for HTTP 202.

- API instances: 1.
- `RabbitMq:ConsumerCount`: 1.
- `RabbitMq:PublisherChannelCount`: 4.
- `Ingestion:ChunkCapacity`: 100.
- VU sweep: 10, 20, and 30.
- Duration: 10 seconds at every load level.
- Same current code and configuration were used for all three runs.
- Redis local rate-limit quota: 10,000,000 requests per `00:01:00` fixed window.

After k6 exits, the runner gets the accepted HTTP 202 count from the k6 summary. It
confirms downstream completion only when the ingestion queue is empty and PostgreSQL's
persisted event count equals that accepted count. A mismatch fails the run. Every row
below therefore satisfies:

```text
accepted HTTP 202 count == final PostgreSQL persisted row count
```

All latency values are milliseconds. RabbitMQ and container-resource values are
two-second samples, so their peaks and drain times are sampled approximations.
Approximate drain time is from the sample with peak total backlog to the first later
zero-backlog sample.

## Load-sweep results

| VU | Requests | Requests/sec | Avg latency | p90 | p95 | Max | HTTP failures | Failed 202 checks | Accepted HTTP 202 | Persisted PostgreSQL rows | Peak ready | Peak unacknowledged | Peak total backlog | Approximate drain time |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 10 | 28,783 | 2,877.18 | 3.38 ms | 4.23 ms | 5.00 ms | 160.90 ms | 0.00% | 0 | 28,783 | 28,783 | 0 | 26,611 | 26,611 | 95.0 s |
| 20 | 21,505 | 2,149.25 | 9.19 ms | 8.31 ms | 9.82 ms | 2,690.25 ms | 0.00% | 0 | 21,505 | 21,505 | 0 | 19,271 | 19,271 | 68.8 s |
| 30 | 28,105 | 2,808.26 | 10.57 ms | 12.74 ms | 14.79 ms | 223.05 ms | 0.00% | 0 | 28,105 | 28,105 | 0 | 25,926 | 25,926 | 93.0 s |

## Sampled container-resource peaks

Each cell is sampled peak CPU / sampled peak memory from `containers.csv`.

| VU | API peak CPU / memory | RabbitMQ peak CPU / memory | Redis peak CPU / memory | PostgreSQL peak CPU / memory |
| ---: | --- | --- | --- | --- |
| 10 | 369.84% / 174.06 MB | 356.59% / 414.61 MB | 21.37% / 11.51 MB | 14.49% / 82.98 MB |
| 20 | 440.94% / 163.79 MB | 284.29% / 322.86 MB | 22.40% / 8.31 MB | 14.68% / 52.19 MB |
| 30 | 436.09% / 177.42 MB | 418.58% / 382.21 MB | 21.24% / 8.56 MB | 15.93% / 54.81 MB |

## Result and limits

All three fresh runs completed successfully with zero HTTP failures, zero failed
HTTP-202 checks, and equal accepted-HTTP-202 and final-persisted-row counts. This
baseline verifies the corrected runner's completion criterion for this local
configuration. It does not establish a target, SLO, delivery guarantee, or a
bottleneck conclusion.
