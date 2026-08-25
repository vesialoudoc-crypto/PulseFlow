# Ingestion Single vs Multi Comparison 001

**Measured:** 2026-08-25 (local time, Europe/Warsaw)

## Purpose

This controlled local comparison records the completed fixed-duration sweep of the
accepted Single and Multi ingestion topologies. It is distinct from the historical
[Baseline 001](ingestion-baseline-001.md) and
[Baseline 002](ingestion-baseline-002.md) measurements, which remain unchanged.

## Measurement methodology

All measurements used the same application and configuration state with the fixed
10-second k6 workload at 10, 20, and 30 VUs.

- **Single:** `k6 -> api -> shared Redis/RabbitMQ/PostgreSQL`.
- **Multi:** `k6 -> HAProxy -> api-1/api-2 -> shared Redis/RabbitMQ/PostgreSQL`.
- `RabbitMq:ConsumerCount = 1` was configured in each API process.
- The application-owned `/health/ready` endpoint had to succeed before measurement.
- The runner verified that the HTTP 202 accepted count equalled the final
  PostgreSQL persisted row count.
- RabbitMQ drain was verified after traffic.
- Every valid measured run had 0% HTTP failures.

Multi changes both the API process count from one to two and the RabbitMQ consumer
count from one to two. Therefore, this experiment is not an isolated measurement of
HTTP/API scaling.

HAProxy POST log counts were used only to prove that both replicas participated in
the measurement. They are not treated as complete accounting of all k6 requests.

All latency values are milliseconds. RabbitMQ peak-backlog and drain-time values are
sampled approximations; `~` denotes an explicitly approximate value.

## Comparison

| VU | Single req/s | Multi req/s | Multi delta | Single p95 | Multi p95 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 3022.60 | 2912.81 | -3.63% | 4.77 ms | 4.83 ms |
| 20 | 2997.80 | 3645.13 | +21.59% | 9.27 ms | 8.84 ms |
| 30 | 3049.91 | 3539.30 | +16.05% | 13.65 ms | 14.56 ms |

## Detailed results

| VU | Topology | Requests | Req/s | Avg | p90 | p95 | Max | Accepted / persisted | RabbitMQ peak total backlog | Drain |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| 10 | Single | 30,231 | 3,022.60 | 3.21 ms | 3.86 ms | 4.77 ms | 73.62 ms | 30,231 / 30,231 | 27,744 | 100.8 s |
| 10 | Multi | 29,137 | 2,912.81 | 3.32 ms | 4.22 ms | 4.83 ms | 74.37 ms | 29,137 / 29,137 | 25,869 | ~74.4 s |
| 20 | Single | 29,996 | 2,997.80 | 6.57 ms | 7.97 ms | 9.27 ms | 84.84 ms | 29,996 / 29,996 | 27,561 | 98.8 s |
| 20 | Multi | 36,466 | 3,645.13 | 5.37 ms | 7.48 ms | 8.84 ms | 89.40 ms | 36,466 / 36,466 | 33,197 | 98.6 s |
| 30 | Single | 30,524 | 3,049.91 | 9.73 ms | 11.89 ms | 13.65 ms | 90.85 ms | 30,524 / 30,524 | 28,074 | 100.9 s |
| 30 | Multi | 35,417 | 3,539.30 | 8.36 ms | 12.77 ms | 14.56 ms | 90.11 ms | 35,417 / 35,417 | 32,217 | 94.5 s |

## Conclusions and limits

1. Single stayed approximately around 3,000 req/s across 10, 20, and 30 VUs while
   latency increased as concurrency increased.
2. Multi did not improve throughput at 10 VU.
3. At 20 and 30 VUs, Multi produced approximately 16-22% higher accepted HTTP
   throughput than Single.
4. All valid runs had 0% HTTP failures.
5. In every run, the accepted HTTP 202 count equalled the final PostgreSQL persisted
   row count.
6. The 10-second HTTP acceptance burst created substantial RabbitMQ backlog that
   required roughly 74-101 seconds to finish downstream processing.
7. HTTP acceptance throughput is therefore not the same as sustainable end-to-end
   persistence throughput.
8. These results do not establish linear scaling, 2x scaling, production capacity,
   or a production instance requirement.
9. High RabbitMQ CPU and backlog are evidence for a future bottleneck investigation,
   not proof that RabbitMQ is the causal bottleneck.
10. Because Multi also doubles RabbitMQ consumers, its improvement cannot be
    attributed solely to HAProxy or API-process scaling.
