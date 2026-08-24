# Ingestion Performance Baseline 001

**Measured:** 2026-08-24 (local time, Europe/Warsaw)

## Scenario and configuration

The existing local runner was executed three times. Each invocation created a fresh
`pulseflow-performance` Compose stack and removed it, including its volumes, after
the artifacts were captured.

```powershell
$env:VUS = '10'
$env:DURATION = '30s'
pwsh .\tests\performance\run.ps1
```

The k6 scenario is the closed-model `ingestion-baseline.js` workload: 10 virtual
users continuously submit one valid Event Contract v2 NDJSON record and check for
HTTP 202.

## Environment

- Repository commit: `1d6da5d4deeca858ae6dc7064092250cd3aa19fd`.
- Windows 10 Pro, version 2009, build 26200.
- CPU: AMD Ryzen 7 5800X3D 8-Core Processor.
- Total system RAM: 63.92 GiB.
- Docker Desktop 4.79.0 (Engine 29.5.3).
- k6 2.2.0 (`k6.exe v2.2.0`, Windows amd64).
- API instances: 1.
- `RabbitMq:ConsumerCount`: 1.
- `Ingestion:ChunkCapacity`: 100.
- Redis rate limit in Compose: 10,000,000 requests per `00:01:00` fixed window.

## Run results

All latency values are milliseconds. `Failed status checks` means failed `status is
202` checks from the generated k6 summary.

| Run artifact directory | Total requests | Requests/sec | Avg | p90 | p95 | Max | HTTP failures | Failed status checks | Persisted rows / accepted requests |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| `20260824-154704-453` | 36,592 | 1,219.42 | 8.13 | 9.06 | 10.03 | 157.66 | 0.00% (0) | 0 | 36,592 / 36,592 |
| `20260824-154953-489` | 34,683 | 1,155.79 | 8.56 | 9.78 | 10.91 | 166.09 | 0.00% (0) | 0 | 34,683 / 34,683 |
| `20260824-155229-796` | 34,796 | 1,159.55 | 8.54 | 9.69 | 10.77 | 166.63 | 0.00% (0) | 0 | 34,796 / 34,796 |

## RabbitMQ and PostgreSQL observations

- Backlog grew during each load run. `messages_ready` remained 0 in every sample;
  the sampled peaks were entirely `messages_unacknowledged`:
  - Run 1: ready 0, unacknowledged 28,673, total 28,673.
  - Run 2: ready 0, unacknowledged 26,839, total 26,839.
  - Run 3: ready 0, unacknowledged 26,929, total 26,929.
- The main queue drained after load in all runs: the final generated samples show
  zero total messages. From the final sampled peak plateau to the first sampled zero,
  the approximate drain times were 99.1 s, 94.9 s, and 97.0 s respectively. The
  two-second sampling cadence bounds this approximation.
- The runner queried PostgreSQL only after the queue drained. Each final persisted
  row count matched the number of requests accepted by the HTTP 202 status check.

## Container-resource observations

Values are sampled peak CPU usage and sampled peak memory usage from `containers.csv`.

| Run | API | RabbitMQ | Redis | PostgreSQL |
| --- | --- | --- | --- | --- |
| 1 | 239.04% / 177.94 MB | 360.65% / 380.53 MB | 12.13% / 12.08 MB | 14.76% / 84.54 MB |
| 2 | 238.86% / 177.94 MB | 388.24% / 342.05 MB | 12.23% / 9.70 MB | 16.03% / 59.06 MB |
| 3 | 269.82% / 173.54 MB | 355.88% / 349.39 MB | 12.23% / 8.89 MB | 16.28% / 63.00 MB |

## Conclusion

This is the first reproducible local Stage 4 baseline for the configured workload.
The HTTP acceptance path completed without measured HTTP or status-check failures,
while the asynchronous RabbitMQ backlog drained before the final persistence check.
These measurements describe this local configuration only; they do not yet establish
a component bottleneck or justify an optimization.
