# Checkpoint: Separate EC2 provisioning from runtime operations

**Date:** 2026-08-27

## Starting point

Checkpoint 078 described the retired, coupled five-node EC2 performance environment:
Terraform included bootstrap/runtime configuration, lifecycle orchestration, runtime
secrets, a load-generator node, and Scheduler support. Its AWS resources had already
been manually removed and its local Terraform state was empty; no new plan had been
applied.

## What changed

- Removed `infra/aws-ec2-performance/` and its EC2-only lifecycle/deployment helper,
  active-tag discovery helper, and corresponding test.
- Added `infra/aws-ec2-low-performance/`, a small Terraform root that provisions only
  VPC networking, security groups, SSM IAM, and four clean Amazon Linux 2023 x86_64
  EC2 hosts.
- Added `infra/ops/aws-ec2-low-performance/` with a Session Manager shell connector
  and an explicit Amazon Linux Docker installation script.
- Replaced the current EC2 architecture document and added ADR 0025. ADRs 0022–0024
  remain historical records and now identify ADR 0025 as their superseding decision.
- Updated the Stage 5 roadmap and removed the obsolete EC2-only AMI environment
  variable example.

## Resulting repository and AWS state

The repository now uses this explicit boundary:

```text
Terraform provisioning -> four clean EC2 hosts -> SSM/Linux operations -> future Docker/runtime deployment
```

The new Terraform root would create one dedicated VPC, one public subnet and Internet
route, four security groups, one SSM instance role/profile, and four `t3.small`
instances named `pulseflow-app`, `pulseflow-rabbitmq`, `pulseflow-redis`, and
`pulseflow-postgres`. Each has one 8-GiB encrypted gp3 root volume. No load-generator
EC2 host, user data, runtime secret, Scheduler, Docker setup, deployment, migration,
or readiness/test automation remains in Terraform.

The prior EC2 environment remains manually removed from AWS and its local Terraform
state remains discarded. This refactor created no new AWS infrastructure.

## Verification

- `terraform -chdir=infra/aws-ec2-low-performance fmt -check` passed.
- `terraform -chdir=infra/aws-ec2-low-performance init -backend=false -input=false`
  completed with the AWS provider only, and `terraform -chdir=infra/aws-ec2-low-performance validate` passed.
  No plan, apply, destroy, or AWS mutation was run.
- PowerShell parser validation for `connect.ps1` and `bash -n` validation for
  `install-docker.sh` passed.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and errors.
- `pwsh ./scripts/test.ps1` could not run tests because the local Docker daemon is not
  available; its Testcontainers prerequisite check failed before `dotnet test`.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- A repository-wide obsolete-reference inspection found no live dependency. Remaining
  matches are intentional historical records or the new retirement documentation.

## Decisions made

- Terraform owns infrastructure lifecycle only; Linux and runtime configuration are
  explicit, independent SSM operations. See [ADR 0025](../decisions/0025-separate-ec2-provisioning-from-runtime-operations.md).
- The temporary environment uses four `t3.small` hosts and small root-only disks for
  learning and short-lived deployment proof, not performance benchmarking or
  production capacity.

## Intentionally unresolved

- Terraform apply, pricing review, and any real AWS resource creation.
- Docker installation on an EC2 host.
- The runtime deployment topology, credentials, image retrieval, service
  configuration, migrations, smoke validation, and external k6 traffic.
- Longer-term operations, storage, network hardening, observability, and availability
  decisions.

## Next recommended step

Review the new Terraform plan only after providing a narrow external test CIDR and
confirming current regional prices. After a separately approved apply, use the SSM
operations layer to install Docker and design the runtime deployment as its own
explicit vertical slice.
