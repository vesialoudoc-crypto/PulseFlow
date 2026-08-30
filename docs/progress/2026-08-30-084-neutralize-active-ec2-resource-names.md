# Checkpoint: Neutralize active EC2 resource names

**Date:** 2026-08-30

## Starting point

The active EC2 Terraform root was already located at `infra/aws/ec2/`, but its AWS
resource name tags, environment tag, and SSM IAM names still contained the obsolete
`ec2-low-performance` label.

## What changed

- Changed the active Terraform environment tag from `ec2-low-performance` to `ec2`.
- Changed active VPC, internet-gateway, public-subnet, public-route-table, and SSM
  IAM role/profile names from the `pulseflow-ec2-low-performance-*` prefix to the
  neutral `pulseflow-ec2-*` prefix.
- Renamed the active operations README heading to `AWS EC2 Operations`.
- Corrected the active roadmap link label for the EC2 architecture document.

## Resulting repository and AWS state

The active Terraform root keeps the same resource blocks, topology, providers,
variables, outputs, and infrastructure behavior. New resources created by a future
apply will use neutral EC2 names and the `Environment = "ec2"` tag.

No Terraform apply or destroy ran. No AWS resources or account settings were
created, changed, or removed.

## Verification

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

No infrastructure architecture decision changed. This is active resource naming
cleanup before the first EC2 apply.

## Intentionally unresolved

- Terraform apply and any AWS infrastructure creation.
- Linux/Docker configuration and all EC2 runtime deployment work.

## Next recommended step

From an authenticated operator shell, review a plan-only result for `infra/aws/ec2/`
with the approved AMI ID and narrow HTTP source CIDR before any separately approved
apply.
