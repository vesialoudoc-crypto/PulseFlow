# Checkpoint: Record the first Render staging architecture

**Date:** 2026-08-25

## Starting point

The local Docker Compose scaling/load milestone was complete. `PulseFlow.Api` could
be manually published as an immutable SHA-tagged GHCR image, but no staging platform,
staging topology, or migration-deployment boundary had been accepted.

## What changed

- Accepted Render as the first disposable staging platform while retaining AWS as the
  final cloud target.
- Added the first staging-environment architecture document and ADR 0018.
- Recorded immutable GHCR image consumption, Render-managed PostgreSQL and Key Value
  boundaries, separately operated persistent RabbitMQ, Render-owned HTTPS ingress,
  private normal dependency traffic, and restricted operator/debug access.
- Recorded `/health/ready` as the staging traffic-readiness endpoint and preserved
  `/health/live` as process/application liveness.
- Recorded the one-time pre-rollout migration invariant and the current lack of a
  migration artifact/job in the published runtime image.
- Updated the Stage 5 roadmap with the accepted staging-architecture state.

## Resulting repository state

The repository now has an accepted, implementation-ready first staging topology:

```text
Internet -> Render HTTPS ingress -> PulseFlow.Api (GHCR sha image)
                                  -> private PostgreSQL, Key Value, and RabbitMQ
```

Render is explicitly an intermediate staging environment, not the final AWS
architecture. HAProxy remains part of the local Multi Docker Compose topology only.
An intentional future API-replica increase is possible, but every API process still
hosts RabbitMQ consumers, so replica count and consumer count increase together.

No infrastructure, deployment behavior, application runtime code, credentials, or
local Compose topology changed.

## Verification

- `dotnet csharpier check .` passed: 54 files checked.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- Render is the accepted first disposable staging platform. See
  [ADR 0018](../decisions/0018-use-render-for-first-disposable-staging-environment.md).
- Migrations must execute exactly once before the selected immutable API version
  becomes active; Render pre-deploy is the preferred lifecycle boundary. Migration
  packaging/execution remains an implementation prerequisite, not a selected
  mechanism.

## Intentionally unresolved

- Terraform/OpenTofu resources and Render deployment configuration.
- The migration bundle or dedicated immutable migration image/job.
- Automatic staging deployment, CI/CD integration, and operator runbooks.
- Final AWS services and architecture.
- Independent API and RabbitMQ consumer scaling, RabbitMQ HA/backup/prefetch, and
  deployed load measurements or bottleneck optimization.

## Next recommended step

Implement the accepted Render topology through infrastructure as code, including the
one-time pre-deploy migration mechanism, private service connectivity, restricted
operator access, immutable image selection, and `/health/ready` configuration.
