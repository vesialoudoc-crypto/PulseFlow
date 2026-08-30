# Checkpoint: Destroy old EC2 performance environment

**Date:** 2026-08-27

## Starting point

Checkpoint 076 recorded an old partial EC2 performance environment in a verified AWS
account and credential context, region `eu-central-1`. Its only EC2 instance was the
Redis `t3.small` node, while the VPC, subnet, Internet gateway, five security groups,
four IAM roles and instance profiles, IAM policy attachments and inline policies,
and two runtime Secrets Manager secrets remained Terraform-managed. The Scheduler
group and schedules had never been created.

The Redis instance was then manually terminated in the AWS Console before this
cleanup began. The shared `pulseflow-staging/bootstrap/ghcr` secret is owned by the
managed AWS staging environment and is explicitly out of scope for this teardown.

## What changed

- Verified the active AWS account and credential context in `eu-central-1`, operating
  as the `pulseflow-staging` operator.
- Confirmed that the tagged Redis `t3.small` instance was in the `terminated` state.
  Its delete-on-termination root volume was already absent.
- Inspected the old Terraform state: it had 26 managed resources plus four
  data-source entries. The manually terminated instance remained only in state.
- Ran the repository destroy workflow. Its state-account guard correctly rejected
  this historical state because it had no saved `aws_account_id` output. A direct
  equivalent Terraform destroy was then used after independently matching the
  state resource ARNs to the verified active account and region.
- Supplied the Redis AMI ID recorded in the old state as `ami_id` solely to avoid
  the active principal's denied public SSM-parameter read. No new replacement plan,
  apply, bootstrap, or runtime deployment was created or run.
- Terraform reported `25 destroyed`; it dropped the manually terminated Redis
  instance during refresh. The shared GHCR bootstrap secret was retained.

## Resulting repository and AWS state

The `infra/aws-ec2-performance` Terraform state is empty: `terraform state list`
has no entries and the state file has zero resources. No code or infrastructure
definition changed; the documentation changes are this historical checkpoint and the
Roadmap's factual cleanup-status correction.

Independent AWS CLI checks found no active tagged PulseFlow performance EC2
instances, no tagged EBS volumes, VPCs, subnets, route tables, Internet gateways,
or security groups, and no runtime secrets under
`pulseflow-ec2-performance/`. The four environment IAM roles and four instance
profiles are absent. Their inline policies and AWS managed-policy attachments were
removed with their roles. The named scheduler role is also absent.

The shared `pulseflow-staging/bootstrap/ghcr` secret remains present and is not a
performance-environment resource.

The active principal is still denied all Scheduler read APIs
(`ListScheduleGroups`, `ListSchedules`, `GetScheduleGroup`, and `GetSchedule`).
Therefore Scheduler-resource absence cannot be independently verified through AWS
in this credential context. Terraform state contained no Scheduler resources before
destroy, checkpoint 076 records the Scheduler creation denial, and the successful
destroy had no Scheduler actions; these are supporting facts, not an AWS-side
read verification.

## Verification

- `aws sts get-caller-identity --region eu-central-1 --output json` verified the
  active AWS account and credential context for the `pulseflow-staging` operator.
- Pre-destroy AWS inspection confirmed the Redis `t3.small` instance was `terminated`,
  its tagged EBS-volume query was empty, and the remaining VPC, subnet, Internet
  gateway, five security groups, four IAM roles/profiles, and two runtime secrets
  existed. The shared GHCR secret existed.
- `pwsh ./scripts/deploy-aws-ec2-performance.ps1 -Destroy` stopped safely at the
  missing historical state-output guard and did not mutate AWS.
- Equivalent guarded-input Terraform destroy with the state-recorded AMI override
  completed: `Destroy complete! Resources: 25 destroyed.`
- `terraform -chdir=infra/aws-ec2-performance state list` returned no entries;
  `terraform output -json` returned `{}`; the state file reports zero resources.
- Post-destroy AWS CLI tag/name checks returned empty lists for active instances,
  EBS volumes, VPCs, subnets, route tables, Internet gateways, security groups, and
  runtime secrets; all named roles and instance profiles returned absent.
- `aws secretsmanager describe-secret ... pulseflow-staging/bootstrap/ghcr`
  confirmed the shared secret remains present and is not scheduled for deletion.
- Scheduler list and direct named-resource reads were denied by the active IAM
  principal, so no stronger AWS-side Scheduler assertion is made.
- `terraform -chdir=infra/aws-ec2-performance fmt -check -recursive` and
  `terraform -chdir=infra/aws-ec2-performance validate` passed.
- `dotnet csharpier check .` passed (63 files checked) and
  `dotnet build PulseFlow.slnx -warnaserror` passed with zero warnings and errors.
- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-build`
  passed: 135 tests. `Invoke-Pester -Script tests/scripts -PassThru` passed: 8
  tests. `pwsh ./scripts/check-project-docs.ps1` passed.
- `pwsh ./scripts/test.ps1` could not run its Testcontainers integration tests
  because Docker Engine was unavailable. `git diff --check` and
  `git diff --cached --check` passed.

## Decisions made

- The historical checkpoint 076 remains immutable. This checkpoint records the
  later manual termination and successful cleanup.
- The destroy script was not changed solely to accommodate an old state that lacks
  saved outputs. The direct destroy used the same configured inputs after explicit
  account and region checks, and retained the bootstrap secret.
- No architectural decision or roadmap stage status changed. The Roadmap narrative
  now records the factual cleanup completion. The low-cost replacement topology
  remains unplanned and unapplied.

## Intentionally unresolved

- The principal lacks Scheduler read permissions, so independent AWS verification
  of the known Scheduler group/schedule names remains unavailable.
- The principal also lacks the public Amazon Linux SSM parameter read used when no
  reviewed AMI override is supplied; this did not block cleanup after use of the
  AMI recorded in the old state.
- Actual `c7i-flex.large` capacity and any new cloud runtime/performance proof
  remain untested.

## Next recommended step

If Scheduler absence must be established through an AWS API rather than the
existing state/history evidence, obtain a read-only Scheduler verification from an
appropriately authorized principal. Otherwise, the old partial environment is
cleaned up and the repository is ready to create and review a fresh low-cost
replacement plan. Do not apply that plan until it has been reviewed and explicitly
approved.
