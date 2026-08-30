# Checkpoint: Use Standard T3 CPU credits

**Date:** 2026-08-27

## Starting point

The minimal EC2 provisioning root creates four `t3.small` hosts for a deliberately
low-cost, disposable learning environment. The shared Terraform instance resource
did not explicitly select the CPU credit mode.

## What changed

- Added `credit_specification` with `cpu_credits = "standard"` to the shared EC2
  instance resource.
- Updated the accepted EC2 architecture and provisioning-boundary ADR to record that
  Standard mode prevents surplus CPU-credit charges during sustained load.

## Resulting repository and AWS state

Every T3 host created by this Terraform root will use Standard CPU credit mode. No
Terraform plan, apply, or destroy ran, and no AWS resource or AWS account setting was
mutated.

## Verification

- `terraform -chdir=infra/aws-ec2-low-performance fmt -check` passed.
- `terraform -chdir=infra/aws-ec2-low-performance validate` passed without a plan or
  remote infrastructure operation.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed.

## Decisions made

- Standard CPU credit mode is required for the low-cost T3 environment to avoid
  surplus CPU-credit charges under sustained load. This is a cost-safety correction
  within the accepted EC2 provisioning boundary, not a new architectural decision.

## Intentionally unresolved

- Terraform apply and any AWS infrastructure creation.
- Linux/Docker configuration and all runtime deployment work.

## Next recommended step

When a real environment is separately approved, provide the reviewed Amazon Linux
2023 x86_64 AMI ID and required narrow HTTP source CIDR, then review a Terraform plan.
