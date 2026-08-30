# Checkpoint: Add portable runtime layer

**Date:** 2026-08-30

## Starting point

The repository had a proven local all-in-one Compose topology and separate clean-host
EC2 provisioning, but no provider-independent Compose definitions for the intended
distributed four-host runtime.

## What changed

- Added `infra/runtime/` with independent Compose projects for PostgreSQL, Redis,
  RabbitMQ, and the application host.
- Preserved the established PostgreSQL, Redis, RabbitMQ, and HAProxy image versions
  and component health checks.
- Configured cross-host application dependencies through required environment
  variables instead of local Compose service names.
- Added a one-shot migration service that explicitly overrides the API image entrypoint
  with `/app/migrations/pulseflow-migrations`; both API replicas wait for it to exit
  successfully.
- Added runtime documentation and updated active EC2 architecture and operations
  documentation to distinguish portable runtime definitions from provider-specific
  host operations.

## Resulting repository and AWS state

`infra/runtime/` describes what runs on four Linux hosts without containing cloud
resource identity, provider CLI usage, or provider deployment automation. The active
EC2 Terraform and operations scripts are unchanged. The local all-in-one Compose
topology remains unchanged.

No Terraform apply or destroy ran. No AWS resources or account settings were created,
changed, or removed. No containers were started.

## Verification

- `docker compose -f infra/runtime/postgres/compose.yaml config` passed with
  placeholder environment values.
- `docker compose -f infra/runtime/redis/compose.yaml config` passed.
- `docker compose -f infra/runtime/rabbitmq/compose.yaml config` passed with
  placeholder environment values.
- `docker compose -f infra/runtime/app/compose.yaml config` passed with placeholder
  image, host, and credential values.
- `git diff --check` and `git diff --cached --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and zero
  errors.
- `pwsh ./scripts/test.ps1` could not run because Docker Desktop / Docker Engine is
  unavailable; its Testcontainers integration-test prerequisite is a running Docker
  daemon.

## Decisions made

No cloud infrastructure decision changed. The runtime layer is intentionally separate
from provider provisioning and does not add provider-specific deployment automation.

## Intentionally unresolved

- Installing Docker and running these projects on real Linux hosts.
- Real secret provisioning, image access, migration execution, and smoke validation.
- Portable RabbitMQ metrics exposure and distributed runtime observability.

## Next recommended step

After a separately approved host-operations procedure exists, supply real host
addresses and secrets through the operator environment, then validate and start the
runtime projects in their documented dependency order.
