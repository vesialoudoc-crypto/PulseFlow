# Checkpoint: Restructure AWS deployment documentation

**Date:** 2026-09-02

## Starting point

The active EC2 implementation had a proven manual lifecycle in
`docs/runbooks/aws-ec2-deployment.md`, but the infrastructure README files mixed
navigation, implementation details, and operator actions. The AWS directory had no
entry README.

## What changed

- Added short infrastructure and AWS entry points at `infra/README.md` and
  `infra/aws/README.md`.
- Rewrote the EC2 README as the Terraform execution stage and moved its background
  material into `infra/aws/ec2/INFO.md`.
- Rewrote the EC2 operations README as the execution stage from successful Terraform
  apply to a verified four-host runtime, and moved operations implementation detail
  into `infra/aws/ec2/ops/INFO.md`.
- Kept the existing full historical manual runbook intact; the README path is now
  self-contained for the active deployment procedure.

## Resulting repository state

The active navigation path is `infra/README.md` → `infra/aws/README.md` →
`infra/aws/ec2/README.md` → `infra/aws/ec2/ops/README.md`. It documents the existing
manual boundary: Terraform provisions clean hosts, operations bootstrap and transfer
files through SSM, and the operator manually supplies secrets and starts the runtime.

No Terraform, PowerShell, shell, Compose, application, or AWS resource behavior
changed. No AWS resources were created, changed, or removed.

## Verification

- `git diff --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- All Markdown paths in the changed documentation resolve.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and errors.
- `pwsh ./scripts/test.ps1` could not run because Docker Desktop / Docker Engine is
  unavailable; the Testcontainers integration tests require a running Docker daemon.

## Decisions made

No architecture or automation decision changed. This is a documentation-structure
change; [ADR 0025](../decisions/0025-separate-ec2-provisioning-from-runtime-operations.md)
remains the accepted boundary.

## Intentionally unresolved

- Runtime secret delivery and an automated deployment pipeline.
- Revalidation of the documentation against a new disposable EC2 lifecycle.
- Deployed observability, performance measurement, bottleneck investigation, and a
  before/after optimization.

## Next recommended step

When a new disposable EC2 lifecycle is separately approved, follow the README path
and record observed results and any required documentation corrections.
