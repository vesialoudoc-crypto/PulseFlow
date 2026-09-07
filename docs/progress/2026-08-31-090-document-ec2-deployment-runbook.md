# Checkpoint: Document EC2 deployment runbook

**Date:** 2026-08-31

## Starting point

The active EC2 root, SSM operations foundation, portable runtime definitions, and
successful disposable lifecycle proof existed, but the complete manual procedure was
distributed across code, readmes, architecture documentation, and checkpoint 088.

## What changed

- Added `docs/runbooks/aws-ec2-deployment.md`, a location-labelled manual lifecycle
  from Terraform initialization through SSM/bootstrap, runtime deployment,
  migration-first startup, readiness, Contract v2 ingestion proof, exact smoke-row
  cleanup, optional runtime shutdown, and Terraform destroy.
- Grounded its Terraform variables, outputs, SSM operations commands, runtime paths,
  Compose variables, migration command, and database query in the active repository
  implementation.
- Corrected the runbook after review to use normal Terraform backend initialization,
  the ops scripts' fixed eu-central-1 context, root-owned runtime .env files with
  root Docker Compose calls, and a bounded asynchronous persistence poll.
- Linked the active EC2 and EC2 operations readmes to the runbook without duplicating
  the operational procedure.

## Resulting repository state

The repository now has one reproducible, explicit manual runbook for the active EC2
implementation. It preserves the accepted boundary: Terraform provisions clean AWS
hosts; the ops scripts perform explicit SSM bootstrap and transport; the operator
supplies credentials and manually starts the provider-independent runtime.

No AWS resources were created, changed, or removed. No deployment was rerun. This
runbook records the lifecycle already proven in PR #19 and checkpoint 088; the newly
written documentation has not itself been revalidated by another AWS deployment.

## Verification

- `git diff --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and errors.
- `pwsh ./scripts/test.ps1` could not run because Docker Desktop / Docker Engine is
  unavailable; its Testcontainers integration tests require a running Docker daemon.
- Searched the runbook for historical instance IDs, public IPs, passwords, tokens,
  and generated credentials; only placeholders are present.
- Reviewed every operational command to ensure its execution location is explicitly
  stated as local PowerShell, app EC2, PostgreSQL EC2, RabbitMQ EC2, or Redis EC2.

## Decisions made

No architecture or automation decision changed. The runbook documents existing,
already-proven behavior and does not create a monolithic deploy script, secret
automation, migration automation, or smoke-test automation.

## Intentionally unresolved

- A runtime secret-delivery mechanism and automated deployment pipeline.
- Revalidation of this documentation against a new disposable EC2 deployment.
- Deployed observability, performance measurement, bottleneck investigation, and
  before/after optimization.

## Next recommended step

When a new disposable EC2 lifecycle is separately approved, follow this runbook and
record its observed results, including any corrections required by current AWS or
runtime behavior.
