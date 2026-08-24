# ADR 0015: Use HAProxy for local multi-instance API ingress

**Date:** 2026-08-25

## Status

Accepted

## Context

Stage 4 needs a reproducible local topology for exercising more than one
`PulseFlow.Api` instance and a control topology for comparing Single and Multi
measurements. The Multi topology needs one client-facing HTTP endpoint that can
distribute requests between equivalent API replicas without exposing those replicas
directly.

PostgreSQL, RabbitMQ, and Redis are shared dependencies in both topologies. Each
`PulseFlow.Api` process also hosts a RabbitMQ consumer. With
`RabbitMq:ConsumerCount = 1`, adding an API replica therefore also adds one consumer;
API ingress capacity and consumer capacity are not independently scaled by the
current local topology.

## Alternatives considered

1. Retain both a direct Single topology and an HAProxy-fronted Multi topology.
2. Expose both Multi API replicas publicly and make the client select a replica.
3. Keep only the Multi topology and use it for every local measurement.
4. Select the future cloud/AWS production ingress as part of this local decision.

## Decision

Retain two explicit local topologies:

```text
Single: client -> api
Multi:  client -> HAProxy -> api-1 / api-2
```

HAProxy is the single public HTTP entry point for Multi. `api-1` and `api-2` are
internal-only and have no direct host ports. HAProxy routes requests round-robin
across replicas that pass its active `/health/live` check.

Both topologies continue to use the same shared PostgreSQL, RabbitMQ, and Redis
dependencies. Single is intentionally retained as the reproducible control for
controlled Single-vs-Multi measurements.

This is the accepted local production-style topology. It does not select or imply
the future cloud/AWS production ingress.

## Consequences

- Multi has one stable client-facing HTTP endpoint while its API replicas remain
  internal to the local Compose network.
- HAProxy distributes Multi requests only across replicas that pass `/health/live`.
- Single remains available without HAProxy so it can serve as the control in local
  Single-vs-Multi measurements.
- PostgreSQL, RabbitMQ, and Redis remain shared rather than being duplicated per API
  replica.
- A Multi measurement changes both API process count and RabbitMQ consumer count:
  with `RabbitMq:ConsumerCount = 1`, `api-1` and `api-2` each host one consumer.
  Results therefore cannot be interpreted as isolated API-ingress scaling.
- The local HAProxy choice does not constrain the later cloud/AWS ingress decision.

## Intentionally deferred

- The future cloud/AWS production ingress.
- Independent API and RabbitMQ consumer scaling.
- Measured Single-vs-Multi results, bottleneck conclusions, and performance targets.
