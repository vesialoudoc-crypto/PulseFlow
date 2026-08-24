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
3. Runs `ingestion-baseline.js` with k6 and saves the k6 summary.
4. Samples the RabbitMQ ingestion queue backlog every two seconds while k6 runs.
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

`rabbitmq.csv` has the following columns:

- `timestamp_utc` — UTC time at which the sample was captured, in ISO 8601 format.
- `messages_ready` — messages ready for delivery to a consumer.
- `messages_unacknowledged` — delivered messages that have not yet been acknowledged.
- `messages_total` — total queue messages at the sample time (ready plus
  unacknowledged).

## Interpreting this scenario

The scenario measures the HTTP ingestion acceptance path:

```text
API -> Redis rate limiter -> RabbitMQ publisher confirmation -> HTTP 202
```

PostgreSQL persistence occurs asynchronously after RabbitMQ. The k6 summary and
RabbitMQ samples do not, by themselves, establish PostgreSQL persistence throughput
or identify a bottleneck. No official performance baseline, performance target, or
optimization decision has been made yet.
