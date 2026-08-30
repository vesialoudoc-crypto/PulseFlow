# Checkpoint: Clarify active EC2 and managed legacy status

**Date:** 2026-08-28

## Starting point

The provider-first layout placed the active EC2 Terraform root in
`infra/aws/ec2/` and preserved the former managed AWS implementation in
`infra/aws/managed-legacy/`. Some active EC2 documentation retained the obsolete
"low-performance" name, and the former managed-staging script could be mistaken for
the current AWS workflow.

## What changed

- Renamed the active EC2 architecture document to
  `docs/architecture/aws-ec2-environment.md` and updated its active reference.
- Renamed the active EC2 provisioning README heading to `AWS EC2 Provisioning`.
- Added an explicit legacy-status notice to `infra/aws/managed-legacy/README.md` and
  corrected its deployment-target wording without changing its technical history.
- Renamed `scripts/deploy-aws-staging.ps1` to
  `scripts/deploy-aws-managed-legacy.ps1`, added an execution-time legacy warning,
  and updated active references. It is not repurposed for EC2.

## Resulting repository and AWS state

`infra/aws/ec2/` remains the active AWS infrastructure root. The former managed
implementation and its script are clearly identified as legacy historical material
and are not the current deployment path.

No Terraform apply or destroy ran. No AWS resources or account settings were
created, changed, or removed.

## Verification

- SHA-256 hashes before and after the corrective pass matched for every active EC2
  `*.tf` file and `.terraform.lock.hcl`.
- `terraform -chdir=infra/aws/ec2 fmt -check` passed.
- `terraform -chdir=infra/aws/ec2 init -backend=false -input=false` passed with
  `TF_DATA_DIR` outside the repository and the existing local provider package used
  read-only.
- `terraform -chdir=infra/aws/ec2 validate` passed.
- `git diff --check` and `git diff --cached --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and zero
  errors.
- `pwsh ./scripts/test.ps1` could not run because Docker Desktop / Docker Engine is
  unavailable; its Testcontainers integration-test prerequisite is a running Docker
  daemon.

## Decisions made

No infrastructure architecture decision changed. This pass clarifies the existing
repository structure and legacy boundary.

## Intentionally unresolved

- Terraform apply and any AWS infrastructure creation.
- Linux/Docker configuration and all EC2 runtime deployment work.
- A current EC2 deployment workflow beyond Terraform provisioning and explicit
  operations scripts.

## Next recommended step

Run a plan-only review from an authenticated operator shell against
`infra/aws/ec2/` before any separately approved apply.
