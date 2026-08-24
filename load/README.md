# Ingestion baseline load test

`ingestion-baseline.js` is the first Stage 4 Grafana k6 scenario. Each virtual user
continuously posts one valid Event Contract v2 NDJSON record to `POST /api/events`.
It has no sleeps, so it is a simple closed-model ingestion-pressure baseline.

## Run a fresh local baseline environment

Install [Grafana k6](https://grafana.com/docs/k6/latest/set-up/install-k6/), then
start the complete local runtime from the repository root:

```powershell
docker compose down -v
docker compose up --build
```

`docker compose down -v` resets the PostgreSQL, RabbitMQ, and Redis volumes. Use it
before a fresh baseline so repeated measurements do not accidentally reuse prior
database records, broker messages, or Redis quota state.

The Compose runtime keeps the Redis limiter enabled, but uses a local-only quota high
enough that HTTP 429 responses are not the expected limiter for the default 10 VU,
30-second scenario. It exposes the API at `http://localhost:5254` and runs k6 on the
host, not in Compose.

In another PowerShell window, run the default scenario:

```powershell
k6 run .\load\ingestion-baseline.js
```

Override the target and load without hardcoding a production host:

```powershell
$env:BASE_URL = 'http://localhost:5254'
$env:VUS = '20'
$env:DURATION = '1m'
k6 run .\load\ingestion-baseline.js
```

Stop the stack when finished:

```powershell
docker compose down
```

Use `docker compose down -v` instead when you also want to remove the local state.

## Read the result

Record the request count and throughput (`http_reqs`), `http_req_duration` average,
`p(90)`, `p(95)`, and maximum, `http_req_failed`, and any failures of the `status is
202` check as unexpected HTTP statuses. This initial scenario has no performance
thresholds because no measured baseline or accepted SLO exists.

This first run measures the HTTP ingestion acceptance path:

```text
API -> Redis rate limiter -> RabbitMQ publisher confirmation -> HTTP 202
```

PostgreSQL persistence happens asynchronously after RabbitMQ, so this scenario alone
does not establish PostgreSQL persistence throughput or identify a bottleneck.
