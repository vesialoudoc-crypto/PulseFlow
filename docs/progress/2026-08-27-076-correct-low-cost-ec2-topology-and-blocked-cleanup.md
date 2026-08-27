# Checkpoint: Correct low-cost EC2 topology and record blocked cleanup

**Date:** 2026-08-27

## Starting point

Checkpoint 075 is the unchanged historical record of the first `m7i.large` EC2
performance-environment apply. That apply partially created the environment: only the
Redis `t3.small` instance launched; the four `m7i.large` launches were rejected and
EventBridge Scheduler group creation was denied. No bootstrap or runtime deployment
ran.

The prior configuration allocated 124 GiB across encrypted root-only gp3 volumes. Its
performance-oriented sizing did not fit the purpose of a short-lived AWS proof or the
primary cost constraint.

## What changed

- Accepted the low-cost five-node topology in ADR 0023 and updated the Terraform
  defaults: `app` is `c7i-flex.large`; RabbitMQ, Redis, PostgreSQL, and load generation
  are `t3.small`.
- Reduced the intended root-only encrypted gp3 allocation from 124 GiB to 80 GiB:
  16 GiB app, 16 GiB RabbitMQ, 8 GiB Redis, 32 GiB PostgreSQL, and 8 GiB load
  generation. This is the replacement configuration; it does not retroactively resize
  the old Redis root volume.
- Added the partial-apply emergency-stop fallback. When Terraform has not produced a
  complete node-output map, it discovers only non-terminated EC2 instances with all
  three exact tags: `Project=PulseFlow`, `Environment=performance`, and
  `ManagedBy=Terraform`. It then attempts to stop only those discovered IDs. The
  fallback does not depend on all five Terraform node outputs and cannot perform an
  account-wide or unrelated-instance stop.
- Inspected the x86_64 architecture and requested `c7i-flex.large` shape. EC2
  `RunInstances` dry-run returned `DryRunOperation`. This did not launch an instance;
  an actual `c7i-flex.large` launch remains unproven until a future reviewed apply.
- Attempted cleanup of the old failed environment through its reviewed destroy path.
  Cleanup stopped at the Redis termination authorization check. No new replacement
  Terraform plan was created, and no new apply or runtime deployment was performed.

## Resulting repository and AWS state

The local Terraform state has 30 addresses: four data-source entries and 26 managed
resources. The managed resources are the Redis `t3.small` instance; one VPC, subnet,
and Internet gateway; five security groups; four IAM roles, four instance profiles,
four managed-policy attachments, and three inline role policies; and two generated
runtime Secrets Manager secrets. No app, RabbitMQ, PostgreSQL, or load-generator EC2
instance exists. No EventBridge Scheduler group or schedules exist.

The retained Redis instance is still recorded as `running` in `eu-central-1a`, with
the fixed private address `10.43.10.30`, the exact PulseFlow performance Terraform
tags, and its original 12 GiB encrypted root-only gp3 volume. Its public IPv4 remains
attached while it is running. The original configured state, not the new 80 GiB
replacement topology, determines that existing root volume.

No Docker bootstrap, Compose runtime, migration, HAProxy/API startup, health proof,
k6 smoke, RabbitMQ consumer evidence, PostgreSQL persistence proof, or smoke cleanup
has run. The configuration is ready for review, but it is not a deployment proof.

## Verification

- Read-only local Terraform state inspection confirmed the exact retained resource
  inventory above.
- An EC2 termination dry-run for the retained Redis instance returned an authorization
  failure, `UnauthorizedOperation`, because the active principal lacks
  `ec2:TerminateInstances`. No termination, stop, destroy, new plan, apply, or other
  AWS mutation was attempted in this continuation.
- `terraform fmt -check -recursive` passed.
- `terraform -chdir=infra/aws-ec2-performance validate` passed.
- PowerShell parser validation for `scripts/` and `tests/scripts/` passed.
- `git diff --check` and `git diff --cached --check` passed.
- `dotnet csharpier check .` passed (63 files checked).
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-build`
  passed: 135 tests.
- `Invoke-Pester -Script tests/scripts -PassThru` passed: 8 tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- Docker Desktop's client was installed but its daemon was unavailable, so
  `pwsh ./scripts/test.ps1` was deliberately not run; its Testcontainers integration
  tests require a running Docker daemon.

## Decisions made

- ADR 0023 supersedes only ADR 0022's instance-sizing and benchmark-isolation
  rationale. ADR 0022 remains the historical decision for the five-node EC2 boundary,
  root-only storage approach, SSM operator path, and timezone-aware schedule.
- A successful `RunInstances` dry-run is not evidence of an instance launch. It is
  recorded only as the observed dry-run result for the inspected x86_64 request.
- The emergency fallback is deliberately constrained by the three environment tags;
  no broader tag search or output-dependent redesign is accepted.

## Intentionally unresolved

- The active principal cannot terminate the retained Redis instance. Scheduler create
  and list permissions also remain unavailable, and the public AL2023 SSM parameter
  read remains unavailable without the reviewed AMI override.
- The old partial environment and its state must be removed before a replacement plan
  can be created. Actual `c7i-flex.large` capacity and launch remain unproven.
- The complete cloud runtime and performance proof remains unstarted.

## Next recommended step

An AWS administrator must either terminate the single EC2 instance carrying all three
`Project=PulseFlow`, `Environment=performance`, and `ManagedBy=Terraform` tags, or
grant the active principal `ec2:TerminateInstances` for that exact environment. Then
run the existing Terraform destroy workflow to remove the retained Terraform-managed
resources, independently verify that both AWS and the Terraform state are empty, and
only after that create and review a fresh low-cost saved plan. Do not apply it until it
has been reviewed and explicitly approved.
