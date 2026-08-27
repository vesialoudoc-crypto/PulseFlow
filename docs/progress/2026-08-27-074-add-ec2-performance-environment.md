# Checkpoint: Add EC2 performance environment definition

**Date:** 2026-08-27

## Starting point

The first disposable AWS lifecycle had already been proven with the managed topology
in `infra/aws/`: ECS Fargate, RDS PostgreSQL, ElastiCache Serverless Valkey, Amazon MQ
RabbitMQ, and an ALB. Its Terraform-managed resources were subsequently destroyed.
That topology was costly to recreate repeatedly for short performance experiments,
mainly because Amazon MQ remains an expensive fixed managed service.

The repository had no EC2-based AWS performance environment. Local Compose had an
accepted HAProxy-fronted two-API topology and the existing k6 ingestion baseline, but
those components all shared one Docker Desktop host.

## What changed

- Added a separate `infra/aws-ec2-performance/` Terraform root. It leaves the proven
  managed Terraform root unchanged.
- Defined five explicit Amazon Linux 2023 x86_64 EC2 roles: `app`, `rabbitmq`,
  `redis`, `postgres`, and `loadgen`.
- Selected ordinary `m7i.large` instances for the app, broker, database, and k6
  nodes and `t3.small` for Redis. The four primary measurement nodes therefore avoid
  both T-family CPU credits and `m7i-flex` CPU-performance scaling; Redis remains the
  deliberate low-cost operational-state exception.
- Added role-specific Compose/bootstrap templates. HAProxy and two `PulseFlow.Api`
  containers run together on the app node; RabbitMQ, Redis, PostgreSQL, and k6 run on
  their respective nodes. The API uses the existing immutable GHCR image and its
  image-contained migration bundle.
- Added fixed private-address service configuration, least-privilege inter-node
  security rules, root-disk-only Docker volume storage, IMDSv2, and role-specific SSM
  and Secrets Manager access.
- Added EventBridge Scheduler StartInstances/StopInstances schedules for weekdays
  08:00–17:00 with the `Europe/Warsaw` IANA timezone and no flexible window.
- Added `scripts/deploy-aws-ec2-performance.ps1` with explicit plan, apply/bootstrap
  stop, deploy/migration/smoke, short baseline, stop, and destroy modes. Its k6 smoke
  runs from the isolated load-generator node, proves an exact PostgreSQL row, and
  removes that row. Before any non-plan lifecycle mutation, it now verifies the active
  STS account against the saved plan for `-Apply` or against Terraform state's
  `aws_account_id` for `-Deploy`, `-RunBaseline`, `-Stop`, and `-Destroy`.
- Added architecture documentation, an EC2 runbook, and ADR 0022. Updated the Stage
  5 roadmap so the managed proof and the new EC2 configuration remain clearly
  distinct.

## Resulting repository state

The new topology is accepted Terraform configuration, not evidence of an EC2 AWS
deployment. It contains no ECS, Fargate, RDS, ElastiCache, Amazon MQ, ALB, NAT
Gateway, Elastic IP, separate EBS data volume, SSH/bastion, or broad public ingress.

Nodes receive an auto-assigned public IPv4 only while running for SSM, package, and
image-pull egress. Every service connection uses static private VPC addresses and
security groups allow only loadgen-to-HAProxy HTTP and app-to-dependency ports. No
security group allows public application, broker, cache, or database ingress.

Stateful container data lives in Docker named volumes on encrypted gp3 root disks.
It persists when instances stop and is removed when Terraform terminates the nodes.
The shared existing read-only GHCR secret is reused, avoiding another persistent
secret charge. Before planning, the script first verifies the current `.env`
credential against GHCR, then compares it with the secret and creates a new version
when it differs. This prevents an image verification/deploy credential mismatch while
leaving the existing secret untouched when verification fails. Generated database and
broker credentials remain sensitive local Terraform state and are read at runtime only
through narrow instance roles.

`-Apply` intentionally confirms first-boot readiness then stops all nodes. `-Deploy`
is the separate migration-first runtime release gate; a weekday scheduled start only
restarts containers already deployed. This prevents a fresh out-of-hours Terraform
apply from turning into an unattended compute bill or an implicit production release.

## Verification

- `terraform fmt -recursive infra/aws-ec2-performance` completed successfully.
- `terraform -chdir=infra/aws-ec2-performance init -backend=false -input=false`
  completed successfully and created the committed provider lock file with AWS 6.62.0
  and Random 3.9.0.
- `terraform -chdir=infra/aws-ec2-performance validate` completed successfully.
- PowerShell parser validation for `scripts/deploy-aws-ec2-performance.ps1` completed
  successfully.
- `pwsh ./scripts/deploy-aws-ec2-performance.ps1` ran the documented plan-only
  lifecycle through AWS identity validation, shared-GHCR credential lookup, GHCR image
  verification, Terraform format/init/validate, and the start of a real AWS plan. It
  made no apply request and created no EC2 resource. Terraform formed a partial
  33-resource create plan but could not finish because the current Terraform operator
  lacks `ssm:GetParameter` on AWS's public AL2023 AMI parameter. The configuration now
  conditionally omits that data source when a reviewed `PULSEFLOW_EC2_AMI_ID` override
  is supplied; verification of that path is recorded separately below.
- The initial plan run exposed a PowerShell native-argument quoting defect that wrote a
  failed local plan artifact literally named `$planPath`. The script now passes the
  expanded `-out` argument as one quoted string, and that generated failed-plan file
  was removed. PowerShell parser validation was rerun successfully after the fix.
- The reviewed corrections changed the four primary nodes from `m7i-flex.large` to
  ordinary `m7i.large`, forwarded `-AwsProfile` to Terraform as `aws_profile`, and
  made `-Apply` recover and verify the saved plan's profile/account context. Existing
  GHCR credentials are now read and synchronized before a plan instead of only being
  identified by ARN.
- A second no-apply `terraform plan` used placeholder image/secret inputs and a
  reviewed-shaped `ami_id` override. It completed a 41-resource create plan while the
  current credentials still lack `ssm:GetParameter`, confirming the conditional AMI
  data-source path does not read SSM. It made only normal plan-time AWS data reads and
  created no AWS resource or saved plan file.
- After the corrections, `terraform fmt -check -recursive`, `terraform validate`, and
  PowerShell parser validation passed. `dotnet csharpier check .` passed; `dotnet
  build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors; 135 unit
  tests and all 6 existing Pester script tests passed. `pwsh ./scripts/test.ps1`
  remains blocked before Testcontainers integration tests because no Docker daemon is
  available. `pwsh ./scripts/check-project-docs.ps1` passed; it checks the Git index,
  so its empty result does not replace review of this uncommitted documentation.
- The accidental staged `27082026.patch` review artifact was removed from both the Git
  index and working tree. It is not part of this implementation.
- With no local Terraform state in this repository, invoking `-Destroy` stopped at the
  state-account guard before it could read the GHCR secret or invoke Terraform destroy.
## Decisions made

- The successful managed AWS proof remains a separate historical configuration. The
  EC2 topology is an additional temporary performance environment, not a replacement.
- Use five EC2 nodes with Dockerized runtime services and HAProxy on the one app node.
  This is accepted in [ADR 0022](../decisions/0022-use-ec2-for-temporary-aws-performance-environment.md).
- Use EventBridge Scheduler with `Europe/Warsaw` rather than host cron or fixed-UTC
  expressions, so the weekday schedule uses AWS's timezone and DST handling.
- Use SSM Run Command and outbound-only public addressing rather than SSH, a bastion,
  public service endpoints, a NAT Gateway, or paid interface endpoints for this
  short-lived one-developer environment.

## Intentionally unresolved

- A reviewed real EC2 plan, an approved apply, and an observed end-to-end EC2
  lifecycle proof.
- Exact current regional cost estimate at approval time. The runbook records cost
  categories and requires an AWS Pricing Calculator recheck before every plan.
- The operator's AMI path: grant `ssm:GetParameter` for the default current-AL2023
  lookup, or record a reviewed AL2023 x86_64 AMI ID in
  `PULSEFLOW_EC2_AMI_ID` for the intended plan.
- Measured deployed signals, bottleneck analysis, a justified optimization, and a
  before/after performance result.
- Production networking, HA, backups, remote Terraform state, public HTTPS/domain
  access, RabbitMQ post-bootstrap permission hardening, and a Graviton/multi-arch
  image decision.

## Next recommended step

Select and record the intended AMI path, then make a real saved plan through
`deploy-aws-ec2-performance.ps1` using the selected image, synchronized GHCR
credential, and one credential/profile context. Review its five instances, root
disks, Scheduler resources, Secrets Manager resources, and expected business-hours
cost before seeking explicit approval for `-Apply`. After an approved apply, run the
short `-Deploy` smoke proof, stop the nodes when finished, and record the actual
lifecycle and cost observations in a new checkpoint.
