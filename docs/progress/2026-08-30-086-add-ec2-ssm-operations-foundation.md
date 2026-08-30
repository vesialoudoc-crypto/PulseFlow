# Checkpoint: Add EC2 SSM operations foundation

**Date:** 2026-08-30

## Starting point

The EC2 Terraform root provisioned only clean Amazon Linux 2023 hosts with SSM IAM,
and `infra/runtime/` already contained provider-independent four-host Compose
definitions. The operations directory had only an interactive Session Manager
connector and a Docker installation shell script.

## What changed

- Added a reusable `instances.ps1` helper that discovers exactly one running EC2
  instance for each supported role (`app`, `postgres`, `rabbitmq`, and `redis`) by
  its exact `Name` tag in the explicit `eu-central-1` region.
- Refactored `connect.ps1` to use that discovery helper and its explicit-region AWS
  CLI path.
- Added `bootstrap.ps1`, which transports the local Docker installer through SSM Run
  Command as Base64, waits for each invocation, and removes the temporary remote
  installer file. The installer now works under root-run SSM Run Command and remains
  safe to repeat.
- Added `copy-runtime.ps1`, which transports the relevant portable Compose files as
  Base64 to `/opt/pulseflow/runtime/<role>/`, verifies their readability, and runs
  `docker compose config` using temporary placeholder values only.
- Added `status.ps1`, which reports EC2 identity, private IP, SSM status, Docker
  status, and runtime-directory status for all four roles.
- Updated the AWS EC2 operations documentation, active EC2 architecture document,
  and Stage 5 roadmap state to describe the implemented operations foundation and
  its deferred security/deployment boundary.

## Resulting repository and AWS state

`infra/aws/ec2/ops/` now provides AWS-specific discovery, SSM transport, Docker
bootstrap, runtime-file copy, and basic status inspection. `infra/runtime/` remains
provider-independent and unchanged. The scripts do not use Terraform state, SSH, S3,
Secrets Manager, or Parameter Store.

No Terraform apply or destroy ran. No AWS resources or account settings were created,
changed, or removed. No live host was queried, no container was started, and no real
PostgreSQL password, RabbitMQ password, or GHCR credential was transmitted or stored.

## Verification

- PowerShell parser validation passed for every `infra/aws/ec2/ops/*.ps1` script.
- `bash -n infra/aws/ec2/ops/install-docker.sh` passed.
- `docker compose -f infra/runtime/<role>/compose.yaml config --no-interpolate`
  passed locally for `postgres`, `redis`, `rabbitmq`, and `app` without starting or
  pulling containers.
- `git diff --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and zero
  errors.
- `pwsh ./scripts/test.ps1` could not run because Docker Desktop / Docker Engine is
  unavailable; the Testcontainers integration tests require a running Docker daemon.

## Decisions made

No new cloud architecture decision was made. This implements the explicit SSM/Linux
operations boundary accepted in ADR 0025 without extending Terraform or selecting a
runtime secret-delivery mechanism.

## Intentionally unresolved

- Applying the EC2 Terraform root and operating against real AWS hosts.
- Runtime secret/config delivery, including PostgreSQL/RabbitMQ credentials and GHCR
  image access.
- `docker compose up`, image pulls, migrations, readiness/smoke validation, and
  external performance traffic.

## Next recommended step

After separately choosing and approving a runtime secret/config-delivery mechanism,
provision or identify the EC2 hosts, run `bootstrap.ps1 all` and `copy-runtime.ps1
all`, then perform a deliberately scoped runtime deployment procedure.
