# Checkpoint: Split local Compose topologies

**Date:** 2026-08-24

## Starting point

The local Compose configuration contained only the multi-instance topology:
HAProxy exposed port 5254 and distributed requests to `api-1` and `api-2`. The
performance runner assumed that topology and always collected resources from its six
runtime services. Baseline 001 and Baseline 002 remain historical single-instance
measurements and are intentionally unchanged.

## What changed

- Split local Compose into shared dependencies in `infra/local/compose.yaml`, an API
  service template in `infra/local/compose.api.yaml`, and topology-specific files:
  `compose.single.yaml` and `compose.multi.yaml`.
- Added a reproducible Single topology with one `api` service exposed as
  `localhost:5254 -> api`.
- Preserved the Multi topology as `localhost:5254 -> HAProxy -> api-1 / api-2`.
  The replicas have no direct host ports; HAProxy retains round-robin routing and
`/health/live` checks from `haproxy/haproxy.cfg`.
- Kept PostgreSQL, RabbitMQ, Redis, migrations, and the complete API environment in
  shared configuration. Every API instance has `RabbitMq__ConsumerCount=1`.
- Made `tests/performance/run.ps1` require `-Topology Single` or `-Topology Multi`.
  It now selects the matching resource-service set and provides
  `-ValidateTopology` for Compose/service-set validation without running k6.
- Assigned isolated runner ports: Single uses 5255 and 15693; Multi uses 5256 and
  15694. Normal local topology commands retain ports 5254 and 15692.

## Resulting repository state

Normal local Compose commands are explicit:

```powershell
docker compose -f infra/local/compose.yaml -f infra/local/compose.single.yaml up --build --detach
docker compose -f infra/local/compose.yaml -f infra/local/compose.multi.yaml up --build --detach
```

The Single performance resource set is `api`, `rabbitmq`, `redis`, and `postgres`.
The Multi resource set is `haproxy`, `api-1`, `api-2`, `rabbitmq`, `redis`, and
`postgres`. Queue-drain verification and the k6 workload are unchanged.

## Verification

Commands run from the repository root:

```powershell
docker compose -f infra/local/compose.yaml -f infra/local/compose.single.yaml config --quiet
docker compose -f infra/local/compose.yaml -f infra/local/compose.multi.yaml config --quiet
pwsh .\tests\performance\run.ps1 -Topology Single -ValidateTopology
pwsh .\tests\performance\run.ps1 -Topology Multi -ValidateTopology
```

Results:

- Both Compose configurations resolve successfully.
- The runner resolves the required Single and Multi resource-service sets without
  invoking k6.
- Dedicated `pulseflow-validation-single` and `pulseflow-validation-multi` projects
  each started successfully and returned HTTP 200 from
  `http://localhost:5254/health/live`.
- The Multi validation project showed host port 5254 only on HAProxy; `api-1` and
  `api-2` exposed only their internal `8080/tcp` ports.
- Both validation projects and their volumes were removed after validation. An
  already-running normal Multi project was temporarily stopped to release port 5254,
  then restored successfully; it was not removed.

## Decisions made

The accepted local topology is recorded in
[ADR 0016](../decisions/0016-use-haproxy-for-local-multi-instance-api-ingress.md):
Single remains the direct reproducible control, while HAProxy is the only public HTTP
entry point for Multi and distributes requests across the two internal API replicas.
This does not create a new performance baseline or alter application, Redis limiter,
or RabbitMQ behavior.

## Intentionally unresolved

- A measured multi-instance performance comparison and any resulting bottleneck
  investigation.
- Target throughput, latency metrics, thresholds, and SLOs.

## Next recommended step

Run a separately requested, documented measurement using the explicit topology that
answers a focused Stage 4 question; keep the existing baseline documents historical.
