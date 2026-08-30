# Checkpoint: Require an explicit EC2 AMI input

**Date:** 2026-08-27

## Starting point

Checkpoint 079 introduced the minimal EC2 provisioning and explicit operations
boundary. The Terraform root still resolved its default Amazon Linux 2023 AMI through
the public Systems Manager Parameter Store parameter, and the Docker installer added
the hard-coded `ec2-user` account to the `docker` group.

## What changed

- Removed the Amazon Linux Systems Manager Parameter Store data source.
- Made `ami_id` a required input with AMI ID format validation and passed it directly
  to the EC2 instances.
- Updated provisioning and architecture documentation to state that no
  `ssm:GetParameter` permission or AMI lookup is required.
- Updated `install-docker.sh` to add the actual invoking non-root session user to the
  `docker` group, including the original user when invoked with `sudo`.
- Updated the operations README to document that behavior.

## Resulting repository and AWS state

The minimal EC2 Terraform root now requires the operator to select and supply an
Amazon Linux 2023 x86_64 AMI ID explicitly. It has no AMI lookup dependency on AWS
Systems Manager Parameter Store and no added IAM permission.

`install-docker.sh` operates correctly for the Session Manager shell user instead of
assuming `ec2-user`; it rejects a root-only invocation because there is no non-root
user to configure.

No Terraform plan, apply, or destroy ran, and no AWS resource or AWS account setting
was mutated.

## Verification

- `terraform -chdir=infra/aws-ec2-low-performance fmt -check` passed.
- `terraform -chdir=infra/aws-ec2-low-performance validate` passed without a plan or
  remote infrastructure operation.
- PowerShell parser validation for `connect.ps1` and `bash -n` validation for
  `install-docker.sh` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.

## Decisions made

- This is a correction to the accepted provisioning boundary, not a new architecture
  decision: AMI selection is an explicit operator input rather than a Terraform
  Systems Manager lookup.

## Intentionally unresolved

- Selecting a reviewed AMI ID for a real region and apply.
- All Linux/Docker/runtime deployment work beyond the standalone installation script.

## Next recommended step

When a real environment is separately approved, select a reviewed AL2023 x86_64 AMI
ID for the target region and provide it together with the required narrow HTTP source
CIDR before reviewing a Terraform plan.
