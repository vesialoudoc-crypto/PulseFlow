# Ingestion Performance Baseline 001

**Measured:** 2026-08-24 (local time, Europe/Warsaw)

## Scenario and configuration

The existing local runner was executed once at each load level below. Every successful
invocation created a fresh `pulseflow-performance` Compose stack and removed it,
including its volumes, after the artifacts were captured.

```powershell
$env:VUS = '10'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1

$env:VUS = '20'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1

$env:VUS = '30'
$env:DURATION = '10s'
pwsh .\tests\performance\run.ps1
```

The k6 scenario is the closed-model `ingestion-baseline.js` workload. Each virtual
user continuously submits one valid Event Contract v2 NDJSON record per request and
checks for HTTP 202. The 30-VU measurement listed below is a successful rerun after
one separate invocation stopped at API liveness because of a local transport-connection
abort; that failed startup recorded no workload result and is not a baseline sample.

## Environment

- Repository commit: `ad1da229afeb36dbdf17e7a18d780196a137abd6`.
- Windows 10 Pro, version 2009, build 26200.
- CPU: AMD Ryzen 7 5800X3D 8-Core Processor.
- Total system RAM: 63.92 GiB.
- Docker Desktop 4.79.0 (Engine 29.5.3).
- k6 2.2.0 (`k6.exe v2.2.0`, Windows amd64).
- API instances: 1.
- `RabbitMq:ConsumerCount`: 1.
- `Ingestion:ChunkCapacity`: 100.
- Redis rate limit in Compose: 10,000,000 requests per `00:01:00` fixed window.

The measurements above predate the later RabbitMQ publisher-channel-pool change and
the performance-runner correctness fix that verifies PostgreSQL persistence against
the k6 HTTP-202 count. Neither later change is part of this baseline configuration or
its recorded results.

## Load-sweep results

All latency values are milliseconds. `Failed status checks` means failed `status is
202` checks from the generated k6 summary. Approximate drain time is measured from
the final sample at the peak total RabbitMQ backlog to the first later zero-backlog
sample; the two-second sampling cadence bounds the approximation.

| Load level | Run artifact directory | Total HTTP requests | Requests/sec | Avg | p90 | p95 | Max | HTTP failures | Failed status checks | Peak ready | Peak unacknowledged | Peak total backlog | Approximate drain time | Final PostgreSQL persisted row count |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- | ---: |
| 10 VU / 10s | `20260824-161353-929` | 11,687 | 1,166.99 | 8.49 | 9.48 | 10.09 | 155.59 | 0.00% (0) | 0 | 0 | 9,296 | 9,296 | 32.3 s | 11,687 |
| 20 VU / 10s | `20260824-161525-313` | 7,368 | 735.64 | 27.07 | 19.13 | 20.13 | 3,664.81 | 0.00% (0) | 0 | 0 | 4,986 | 4,986 | 16.2 s | 7,368 |
| 30 VU / 10s | `20260824-161701-289` | 30 | 2.11 | 14,188.86 | 14,198.01 | 14,198.78 | 14,199.54 | 0.00% (0) | 0 | 0 | 0 | 0 | Not observed (all samples were zero) | 0 |

For the 10-VU and 20-VU rows, the final PostgreSQL count matched the number of HTTP
202 responses. At 30 VU, all 30 status checks passed but the final PostgreSQL count
was zero. The runner also sampled no RabbitMQ backlog. This is an observed outcome,
not evidence that HTTP 202 established end-to-end persistence at that load level.

## Container-resource observations

Values are sampled peak CPU usage and sampled peak memory usage from `containers.csv`.

| Load level | API | RabbitMQ | Redis | PostgreSQL |
| --- | --- | --- | --- | --- |
| 10 VU / 10s | 244.20% / 130.55 MB | 282.92% / 255.64 MB | 10.37% / 8.66 MB | 13.51% / 49.62 MB |
| 20 VU / 10s | 217.86% / 129.39 MB | 287.38% / 225.55 MB | 10.31% / 8.59 MB | 13.03% / 47.73 MB |
| 30 VU / 10s | 34.13% / 51.98 MB | 181.55% / 151.83 MB | 1.95% / 7.73 MB | 2.38% / 41.03 MB |

## Conclusion

Baseline 001 is a three-level local load sweep measured at repository commit
`ad1da229afeb36dbdf17e7a18d780196a137abd6`, not three repeated 10-VU / 30-second
measurements. It records that configuration's HTTP acceptance, sampled queue backlog,
resource peaks, and final persistence result at 10, 20, and 30 VU.

The 30-VU observation is a focused candidate for further investigation: successful
HTTP 202 checks were not accompanied by persisted rows in this run. The measurements
do not identify its cause, establish a target or SLO, or justify an optimization.
