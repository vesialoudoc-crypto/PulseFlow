# Ingestion baseline performance run

`run.ps1` runs the Stage 4 Grafana k6 ingestion baseline against a fresh local
runtime. The scenario continuously posts one valid Event Contract v2 NDJSON record
to `POST /api/events` and checks for HTTP 202 responses. It is a closed-model
ingestion-pressure scenario with no performance thresholds.

## Run

Install [Docker](https://docs.docker.com/get-docker/) and
[Grafana k6](https://grafana.com/docs/k6/latest/set-up/install-k6/), then run this
command from the repository root:

```powershell
pwsh .\tests\performance\run.ps1
```

The runner uses the scenario defaults of 10 virtual users for 30 seconds. To adjust
the load for a local experiment, set `VUS` and `DURATION` in the current shell before
running the command:

```powershell
$env:VUS = '20'
$env:DURATION = '1m'
pwsh .\tests\performance\run.ps1
```

For every run, the runner:

1. Removes any prior `pulseflow-performance` Compose stack and its volumes, then
   creates a fresh stack under that isolated Compose project name.
2. Waits until `http://localhost:5254/health/live` returns HTTP 200 before starting
   the load scenario.
3. Starts RabbitMQ backlog and container-resource sampling, then runs
   `ingestion-baseline.js` with k6 and saves the k6 summary.
4. Waits for the RabbitMQ ingestion queue to drain, then reads the final persisted
   row count from PostgreSQL.
5. Removes the `pulseflow-performance` Compose stack and its volumes when the run
   ends, including when startup or k6 fails after the stack has been started.

The local Compose configuration keeps the Redis limiter enabled with a local-only
quota high enough that HTTP 429 is not expected to limit the default scenario.

## Result artifacts

Each run creates a timestamped directory:

```text
tests/performance/results/<timestamp>/
```

It contains:

- `k6-summary.json` — the k6 end-of-run summary.
- `rabbitmq.csv` — periodic backlog samples for the
  `pulseflow.ingestion-batches` queue.
- `database.txt` — the final PostgreSQL persisted row count and the UTC time at
  which it was captured.
- `containers.csv` — periodic CPU and memory samples for the local Compose
  containers.

`rabbitmq.csv` has the following columns:

- `timestamp_utc` — UTC time at which the sample was captured, in ISO 8601 format.
- `messages_ready` — messages ready for delivery to a consumer.
- `messages_unacknowledged` — delivered messages that have not yet been acknowledged.
- `messages_total` — total queue messages at the sample time (ready plus
  unacknowledged).

After k6 exits successfully, the runner waits for the RabbitMQ main queue to be
fully drained (`messages_ready = 0` and `messages_unacknowledged = 0`) before it
reads PostgreSQL. Consumer acknowledgements happen only after persistence completes,
so the resulting `database.txt` row count represents the completed asynchronous
persistence work for that run.

`containers.csv` has the following columns:

- `timestamp_utc` — UTC time at which the sample was captured, in ISO 8601 format.
- `service` — the Compose service that produced the sample: `api`, `rabbitmq`,
  `redis`, or `postgres`.
- `cpu_percent` — the container CPU utilization reported by Docker, as a percentage.
- `memory_usage_mb` — the container memory usage reported by Docker, in megabytes.

Container sampling starts with k6 and continues through RabbitMQ drain. This captures
resource use from asynchronous persistence after HTTP load stops.

## Interpreting this scenario

The scenario measures the HTTP ingestion acceptance path:

```text
API -> Redis rate limiter -> RabbitMQ publisher confirmation -> HTTP 202
```

PostgreSQL persistence occurs asynchronously after RabbitMQ. The k6 summary and
RabbitMQ samples do not, by themselves, establish PostgreSQL persistence throughput
or identify a bottleneck. No official performance baseline, performance target, or
optimization decision has been made yet.
