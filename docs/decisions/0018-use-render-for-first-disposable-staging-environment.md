# ADR 0018: Use Render for the first disposable staging environment

**Date:** 2026-08-25

## Status

Superseded by [ADR 0020](0020-use-aws-for-first-cloud-deployment.md) on 2026-08-26.
This remains the historical record of the Render staging experiment; no Render
resources were created.

## Context

The local Docker Compose scaling/load milestone is complete, and Stage 5 needs an
inexpensive, replaceable deployed environment before the final AWS architecture is
selected and implemented. The staging environment must consume the existing immutable
GHCR API image, keep runtime configuration external, provide managed PostgreSQL and
Redis-compatible operational state, run RabbitMQ separately with persistence, and
support private dependency connectivity plus controlled operator access.

The current application process hosts both its HTTP API and RabbitMQ consumer. That
coupling constrains how API-replica changes are interpreted, but it is not being
solved as part of the first staging-platform decision.

## Alternatives considered

1. Use Render as a disposable staging platform with managed PostgreSQL, managed Key
   Value storage, and separately operated RabbitMQ.
2. Use Railway for the first staging environment.
3. Use DigitalOcean for the first staging environment.
4. Start directly with the final AWS architecture.

## Decision

Use Render for the first disposable staging environment. The environment model is:

```text
Local: Docker Compose
Staging: Render
Final cloud target: AWS, later
```

Render staging consumes `ghcr.io/<repository-owner>/pulseflow-api:sha-<commit-sha>`
without rebuilding the API source in the normal deployment path. Render owns public
HTTPS ingress and load balancing, so HAProxy is not deployed there. The API runs first
as one instance and continues to expose its normal HTTP port; `/health/ready` is the
traffic-readiness endpoint.

Use Render managed PostgreSQL and Render Key Value / Redis-compatible managed storage
through private application endpoints. Operate RabbitMQ as a separate Render service
with a persistent disk, using private AMQP `5672`. A protected RabbitMQ management UI
may be exposed for operator use without making AMQP public. PostgreSQL and Redis
external access, when needed for debugging, is restricted to a trusted developer IP.

Render was selected because it provides a sufficiently small, replaceable platform
for the immediate deployment-practice and deployed-measurement goals while supporting
the accepted service boundaries. Railway and DigitalOcean remain viable platforms but
were not selected for this first staging slice; choosing either would add a different
platform and provisioning path without improving the accepted learning outcome. AWS
remains the final cloud target, but starting there would combine the first disposable
deployment with the larger final-cloud architecture and its cost/complexity decisions
before they are required.

The full accepted topology and lifecycle boundaries are documented in
[First Staging Environment Architecture](../architecture/staging-environment.md).

## Consequences

- Staging uses a portable, immutable GHCR image and external runtime configuration;
  it does not establish a Render source-build workflow.
- Public traffic terminates at Render ingress. PostgreSQL, Redis-compatible Key Value,
  and RabbitMQ AMQP are private by default.
- PostgreSQL remains durable across ordinary API redeployments and is portable through
  logical PostgreSQL backup/dump and restore. Redis and RabbitMQ data are operational
  staging state and may be discarded when the entire environment is destroyed.
- Migrations must execute exactly once before a new API version becomes active.
  Render pre-deploy is the preferred lifecycle boundary, but the migration
  artifact/job mechanism remains an immediate implementation decision.
- Increasing API replicas also increases RabbitMQ consumer count under the current
  co-hosted process model; later scaling experiments cannot treat it as isolated API
  scaling.
- Terraform/OpenTofu, deployment automation, and actual Render resources are
  deliberately deferred to the next infrastructure task.

## Intentionally deferred

- Terraform/OpenTofu implementation and Render resource deployment.
- Automatic staging deployment and CI/CD integration.
- The final migration bundle or dedicated migration image/job.
- RabbitMQ HA, clustering, backup, prefetch tuning, and consumer changes.
- `PulseFlow.Worker` extraction and independent API/consumer scaling.
- AWS service selection, final AWS topology, load testing, and bottleneck
  optimization.
