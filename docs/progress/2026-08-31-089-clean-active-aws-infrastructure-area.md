# Checkpoint: Clean active AWS infrastructure area

**Date:** 2026-08-31

## Starting point

The active EC2 deployment proof had been documented, but the repository still had a
tracked `infra/aws/managed-legacy/` implementation scheduled for removal, generated
Terraform cache/state/plan artifacts under `infra/aws/` and `infra/aws/ec2/`, and
active documentation links to the removed managed implementation.

## What changed

- Removed the tracked `infra/aws/managed-legacy/` Terraform implementation and its
  configuration from the active repository tree.
- Removed generated Terraform provider caches, state files, backups, and the saved
  plan from `infra/aws/` and `infra/aws/ec2/`.
- Preserved `infra/aws/ec2/.terraform.lock.hcl`, current EC2 Terraform source, the
  ignored local `infra/aws/ec2/terraform.tfvars`, AWS SSM operations scripts, and the
  provider-independent `infra/runtime/` definitions.
- Added an exact `tfplan` ignore rule while retaining the existing global Terraform
  cache/state and local `terraform.tfvars` ignore policy.
- Reworded active documentation so the managed AWS proof is historical evidence in
  Git history, ADRs, pull requests, and checkpoints rather than live code.

## Resulting repository state

`infra/aws/ec2/` is the only active AWS Terraform root. Its `ops/` directory remains
the AWS-specific SSM/host operations layer, while `infra/runtime/` remains the
provider-independent Docker runtime layer. Terraform continues to own AWS
infrastructure only; credentials, runtime deployment, migrations, and smoke tests
remain outside Terraform.

The managed AWS proof remains historically documented, but its inactive Terraform and
deployment code no longer exists in the active repository tree. No AWS resources were
created, changed, or removed during this cleanup.

## Verification

- `terraform -chdir=infra/aws/ec2 fmt -check -recursive` ran and reported the
  preserved ignored local `terraform.tfvars` as unformatted. It was intentionally not
  modified because it is the current environment-specific local input.
- `git diff --check` passed.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and zero
  errors.
- `pwsh ./scripts/test.ps1` could not run because Docker Desktop / Docker Engine is
  unavailable; the Testcontainers integration tests require a running Docker daemon.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- Verified no `.terraform/`, Terraform state, state lock, crash log, `tfplan`, or
  `*.tfplan` artifact remains under `infra/aws/`.
- Searched active repository content for `managed-legacy`,
  `aws-ec2-performance`, and `deploy-aws-managed-legacy`; remaining matches are
  historical ADRs and checkpoints only.

## Decisions made

No deployment architecture changed. Removing inactive managed AWS implementation code
does not erase its historical evidence or alter the active EC2 operations boundary.

## Intentionally unresolved

- Runtime secret delivery and automated deployment.
- Deployed observability, performance measurement, bottleneck investigation, and
  before/after optimization.

## Next recommended step

Use the EC2 root and explicit operations layer for any separately approved future AWS
work; retain historical managed-AWS evidence through repository history and documents.
