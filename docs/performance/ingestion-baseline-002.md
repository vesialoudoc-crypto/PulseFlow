# Ingestion Performance Baseline 002

**Measured:** 2026-08-24 (local time, Europe/Warsaw)

## Purpose

Baseline 002 is the post-fix local load sweep. It supersedes neither the historical
measurements nor the provenance of [Baseline 001](ingestion-baseline-001.md): Baseline
001 remains the pre-fix record at commit `ad1da229afeb36dbdf17e7a18d780196a137abd6`.

This baseline measures the branch at commit `dbee46ca53f95f5752384933ba960a3554f2a277`,
which includes the RabbitMQ publisher channel pool and the corrected performance-runner
completion verification.

## Scenario and configuration

The local runner used its isolated `pulseflow-performance` Compose stack. Each run
started with fresh volumes and removed the stack and volumes after the artifacts were
captured. The closed-model k6 scenario continuously sent one valid Event Contract v2
NDJSON record per request to `POST /api/events` and checked for HTTP 202.

- API instances: 1.
- `RabbitMq:ConsumerCount`: 1.
- `RabbitMq:PublisherChannelCount`: 4.
- `Ingestion:ChunkCapacity`: 100.
- VU sweep: 10, 20, and 30.
- Duration: 10 seconds at every load level.
- Redis local rate-limit quota: 10,000,000 requests per `00:01:00` fixed window.

The valid 10-VU and 20-VU artifacts were created for this sweep. The existing valid
30-VU artifact was reused rather than rerun. Its `database.txt` records 30 accepted
HTTP 202 responses and 30 persisted PostgreSQL rows. During its completion phase, the
runner initially observed an empty queue and zero persisted rows, waited, and then
observed 30 persisted rows.

After k6 exits, the corrected runner obtains the accepted HTTP 202 count from the k6
summary. It treats downstream completion as confirmed only when the ingestion queue is
empty and PostgreSQL's persisted event count equals that accepted count. A mismatch
fails the run. Therefore, every row below satisfies:

```text
accepted HTTP 202 count == final PostgreSQL persisted row count
```

All latency values are milliseconds. Queue and container values are two-second samples,
so their peaks and drain times are sampled approximations. Approximate drain time is
from the sample with the peak total backlog to the first later zero-backlog sample.

## Load-sweep results

| Load level | Run artifact directory | Total HTTP requests | Requests/sec | Avg | p90 | p95 | Max | HTTP failures | Failed 202 checks | Accepted HTTP 202 | Peak ready | Peak unacknowledged | Peak total backlog | Approximate drain time | Final PostgreSQL persisted row count |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| 10 VU / 10s | `tests/performance/results/20260824-172741-703` | 27,898 | 2,789.01 | 3.48 | 4.27 | 5.23 | 164.02 | 0.00% (0) | 0 | 27,898 | 0 | 25,700 | 25,700 | 93.0 s | 27,898 |
| 20 VU / 10s | `tests/performance/results/20260824-172538-229` | 21,813 | 2,179.99 | 9.06 | 9.00 | 10.54 | 2,128.28 | 0.00% (0) | 0 | 21,813 | 0 | 19,667 | 19,667 | 68.8 s | 21,813 |
| 30 VU / 10s (reused) | `tests/performance/results/20260824-172255-297` | 30 | 2.28 | 13,158.93 | 13,159.15 | 13,159.15 | 13,159.66 | 0.00% (0) | 0 | 30 | 0 | 0 | 0 | Not observable; every backlog sample was zero | 30 |

## Sampled container-resource peaks

Each cell is sampled peak CPU / sampled peak memory from `containers.csv`.

| Load level | API | RabbitMQ | Redis | PostgreSQL |
| --- | --- | --- | --- | --- |
| 10 VU / 10s | 444.23% / 172.81 MB | 423.83% / 362.91 MB | 21.75% / 8.09 MB | 14.85% / 54.22 MB |
| 20 VU / 10s | 384.21% / 164.31 MB | 290.02% / 336.17 MB | 21.51% / 8.38 MB | 15.29% / 52.21 MB |
| 30 VU / 10s (reused) | 74.77% / 68.18 MB | 328.42% / 211.39 MB | 2.11% / 13.68 MB | 7.74% / 70.64 MB |

## Result and limits

All included runs completed successfully with zero HTTP failures, zero failed HTTP 202
checks, and equal accepted-HTTP-202 and final-persisted-row counts. The result verifies
the corrected runner's completion criterion for this local configuration; it does not
establish a target, SLO, delivery guarantee, or root cause for the differing HTTP
acceptance rates. The 30-VU reused run has only 30 completed requests with long
latency and no sampled backlog, so it remains a measured observation rather than a
bottleneck conclusion or a reason to optimize.
