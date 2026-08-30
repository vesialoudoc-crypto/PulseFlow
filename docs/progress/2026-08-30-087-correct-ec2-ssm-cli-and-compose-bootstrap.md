# Checkpoint: Correct EC2 SSM CLI and Compose bootstrap

**Date:** 2026-08-30

## Starting point

The EC2 SSM operations foundation had been added, but its AWS CLI v2 validation used
an over-escaped .NET regular expression and its Docker installer did not explicitly
install the Docker Compose CLI plugin required by `copy-runtime.ps1`.

## What changed

- Corrected the AWS CLI v2 regular expression in `instances.ps1` from
  `^aws-cli/2\\.` to `^aws-cli/2.`, which matches normal AWS CLI v2 version output
  such as `aws-cli/2.28.0`.
- Extended `install-docker.sh` to install the pinned Docker Compose `v2.32.4` x86_64
  CLI plugin system-wide at
  `/usr/local/lib/docker/cli-plugins/docker-compose` using the official manual-plugin
  download location.
- Kept bootstrap repeat-safe: Docker Engine is started/enabled and the same pinned
  Compose plugin is installed again safely on each run.
- Added explicit `docker compose version` verification alongside the existing Docker
  server-version check, without installing legacy `docker-compose`.
- Updated the operations README, active EC2 architecture description, and Stage 5
  roadmap text so they explicitly describe Docker Compose plugin installation.

## Resulting repository and AWS state

`bootstrap.ps1` can now provide both Docker Engine and the `docker compose` command
needed by `copy-runtime.ps1` before runtime files are validated remotely. The runtime
Compose topology, Terraform, secret boundary, and deployment boundary remain
unchanged.

No Terraform apply or destroy ran. No AWS resources or account settings were created,
changed, or removed. No AWS CLI command contacted a live environment, no bootstrap
ran remotely, and no containers were started.

## Verification

- PowerShell parser validation passed for all `infra/aws/ec2/ops/*.ps1` scripts.
- `bash -n infra/aws/ec2/ops/install-docker.sh` passed.
- `git diff --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.

## Decisions made

No architectural decision changed. The pinned plugin installation is a correction
required by the already-implemented `docker compose config` operation.

## Intentionally unresolved

- Applying EC2 Terraform and running bootstrap on real hosts.
- Runtime secret/config delivery, image access, `docker compose up`, migrations, and
  smoke validation.

## Next recommended step

After the secret/config-delivery mechanism is separately approved, validate this
operations foundation on the intended EC2 hosts before starting any runtime services.
