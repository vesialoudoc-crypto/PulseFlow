# Checkpoint: Restructure provider-first infrastructure layout

**Date:** 2026-08-28

## Starting point

The active, minimal EC2 Terraform root was at
`infra/aws-ec2-low-performance/`, and its separate operations scripts were at
`infra/ops/aws-ec2-low-performance/`. The historical managed AWS implementation
occupied `infra/aws/`.

## What changed

- Moved the active EC2 provisioning files to `infra/aws/ec2/` without changing its
  Terraform files or dependency lock file.
- Moved the EC2 operations scripts to `infra/aws/ec2/ops/`, preserving their
  separation from Terraform provisioning.
- Moved the tracked managed AWS implementation files to
  `infra/aws/managed-legacy/` without modernizing or repairing that implementation.
- Added `infra/README.md` to document the provider-first
  `infra/<provider>/<deployment-model>/` convention without creating empty future
  provider directories.
- Updated active paths in documentation, readmes, and the managed-staging script.
  `scripts/aws/EnvFileLoader.ps1` remains a general AWS helper outside the EC2
  operations directory.

## Resulting repository and AWS state

The active EC2 Terraform root is now `infra/aws/ec2/`; its operational scripts are
now `infra/aws/ec2/ops/`. The managed AWS implementation is retained at
`infra/aws/managed-legacy/` for historical reference. Existing ignored Terraform
working directories, state files, and plan files were not moved or modified.

No Terraform apply or destroy ran. No AWS resources or account settings were
created, changed, or removed.

## Verification

- SHA-256 hashes before and after the move matched for every active EC2 `*.tf` file
  and `.terraform.lock.hcl`.
- `terraform -chdir=infra/aws/ec2 fmt -check` passed.
- `terraform -chdir=infra/aws/ec2 init -backend=false -input=false` passed with
  `TF_DATA_DIR` outside the repository and the existing local provider package used
  read-only.
- `terraform -chdir=infra/aws/ec2 validate` passed.
- An AWS CLI STS caller-identity check failed because AWS credentials were not
  available, so the required plan-only command was not run.
- `git diff --check` and `git diff --cached --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and zero
  errors.
- `pwsh ./scripts/test.ps1` could not run because Docker Desktop / Docker Engine is
  unavailable; its Testcontainers integration-test prerequisite is a running Docker
  daemon.

## Decisions made

No infrastructure architecture decision changed. This is a repository-structure
cleanup that establishes the documented provider-first convention.

## Intentionally unresolved

- Terraform apply and any AWS infrastructure creation.
- Linux/Docker configuration and all EC2 runtime deployment work.
- Future Azure and Railway deployment models.

## Next recommended step

When AWS credentials are available, run a plan-only review from `infra/aws/ec2` with
the approved AMI and narrow HTTP source CIDR before any separately approved apply.
