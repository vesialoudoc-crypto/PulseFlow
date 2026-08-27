# Checkpoint: Attempt first EC2 performance deployment proof

**Date:** 2026-08-27

## Starting point

Checkpoint 074 recorded an accepted but unapplied EC2 performance topology: five
Amazon Linux 2023 x86_64 instances, four `m7i.large` nodes and one `t3.small` Redis
node, with all workload services running in Docker containers. The prior managed AWS
proof in `infra/aws/` remained a separate historical success and its Terraform-managed
resources were destroyed.

The current operator lacked `ssm:GetParameter` for the default public AL2023 AMI
parameter. The repository had no observed EC2 bootstrap, runtime deployment, or smoke
proof.

## What changed

- Completed a read-only configuration and lifecycle review before the real plan.
- Corrected narrow lifecycle-proof defects found by that review without changing the
  accepted topology:
  - rendered HAProxy configuration and k6 JavaScript assets are now readable by their
    unprivileged container users while root-executed helper scripts remain mode `0700`;
  - saved-plan and Terraform-state lifecycle guards now require the expected AWS region
    as well as the existing account guard;
  - `-Apply` resolves its saved plan in the Terraform root, where its provider schemas
    are installed;
  - stopping waits for every known node to reach `stopped`, and bootstrap/deploy
    failures make a best-effort stop attempt;
  - deployment verifies both API replicas from HAProxy's network, private liveness and
    readiness, and exact smoke-row absence after cleanup;
  - `-DeleteBootstrapSecret` cannot select a destroy operation without `-Destroy`.
- Verified account `378436334087`, region `eu-central-1`, the default credential chain,
  the immutable GHCR image, and a matching existing shared GHCR credential secret
  without changing the secret.
- Selected and inspected the explicit Amazon-owned AL2023 x86_64 EBS AMI
  `ami-02d33cbf17ed94bf3` because the accepted default SSM lookup remains unavailable
  to this operator. It was available in the selected region and both required instance
  types were offered in the selected availability zone.
- Created and reviewed a real saved Terraform plan: 41 additions, 0 changes, and 0
  destroys. It contained five expected instances, 124 GiB of encrypted root-only gp3
  storage, one VPC/subnet/Internet Gateway/route, five restricted security groups,
  IAM profiles and policies, two runtime secrets, and the three Scheduler resources.
  It contained no ALB, NAT Gateway, Elastic IP, separate EBS volume, ECS/Fargate, RDS,
  ElastiCache, or Amazon MQ.
- The first `-Apply` stopped before Terraform mutation because its saved-plan guard ran
  `terraform show` outside the Terraform root. The guard was corrected and parser plus
  saved-plan schema validation passed.
- The retried saved-plan apply partially created Terraform resources. The Redis
  `t3.small` instance was created, but each required `m7i.large` launch was rejected as
  not Free Tier eligible and Scheduler group creation was denied. Terraform therefore
  failed before bootstrap and before it could obtain the complete five-node output map.

## Resulting repository state

The accepted EC2 topology remains unchanged, but it is **not** proven. Terraform state
now contains the completed supporting resources and one Redis EC2 instance. The four
`m7i.large` instances and Scheduler group/schedules do not exist. No Docker bootstrap,
Docker Compose verification, migration, HAProxy/API startup, health proof, k6 smoke,
RabbitMQ consumer evidence, PostgreSQL exact-row verification, or smoke cleanup ran.

The sole Redis `t3.small` instance was confirmed `running` after the failed apply. An
immediate exact-instance `ec2:StopInstances` request was denied. The intended SSM
shutdown fallback could not be attempted because `ssm:DescribeInstanceInformation` was
also denied. No instance IDs, secret values, or credentials are recorded in this
checkpoint.

## Verification

- `terraform fmt -check -recursive` passed.
- `terraform -chdir=infra/aws-ec2-performance init -backend=false -input=false` and
  `terraform -chdir=infra/aws-ec2-performance validate` passed.
- PowerShell parser validation passed before and after lifecycle-script corrections.
- `dotnet csharpier check .` passed.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-build` passed:
  135 tests.
- `Invoke-Pester -Script tests/scripts -PassThru` passed: 6 tests.
- `pwsh ./scripts/test.ps1` could not run Testcontainers integration tests because no
  Docker daemon was available locally. This did not block the attempted AWS lifecycle.
- Read-only AWS identity, AMI, Availability Zone offering, image-manifest, secret-match,
  Terraform-plan, and partial-state inspections passed as described above.
- AWS Pricing `GetProducts` is denied to the operator, so an exact operator-side EC2
  price query could not be completed. The cost categories were reviewed: EC2 and public
  IPv4 while running; root EBS and Secrets Manager while stopped; plus SSM/CloudWatch
  output and network/image-pull charges. The running Redis node continues to incur EC2
  and public-IPv4 cost until an administrator stops it.
- The real apply failed with `InvalidParameterCombination` for the four `m7i.large`
  instances and `AccessDeniedException` for `scheduler:CreateScheduleGroup`. A direct
  stop attempt failed with `UnauthorizedOperation` for `ec2:StopInstances`; SSM
  inspection failed with `AccessDeniedException` for `ssm:DescribeInstanceInformation`.

## Decisions made

- The AMI override is an operator-level workaround for missing public-parameter read
  permission, not an architecture decision. The reviewed current AL2023 x86_64 AMI was
  used only in the saved plan and must be rechecked before a future plan.
- The container-file modes, region guards, stop/failure behavior, and deterministic
  proof checks are implementation corrections within ADR 0022's accepted lifecycle.
  No ADR is required.
- Do not replace the accepted `m7i.large` nodes with Free Tier instance types. The
  failure is an AWS account-policy constraint and does not demonstrate that the accepted
  EC2 design is invalid.
- Do not destroy the partial Terraform state automatically. The request requires the
  environment to remain for inspection and expressly forbids automatic destruction.

## Intentionally unresolved

- Immediate external intervention is required to stop the running Redis instance.
- The operator needs account-policy/IAM permission for the accepted `m7i.large` launch,
  EventBridge Scheduler create operations, EC2 start/stop, and SSM inspection/Run
  Command. `pricing:GetProducts` and public-AMI `ssm:GetParameter` remain optional
  convenience permissions when a reviewed AMI override is used.
- The partial state requires a fresh, reviewed saved plan after permissions are fixed;
  the original 41-create plan must not be applied again.
- The full bootstrap → stop → deploy → health → k6 → RabbitMQ → PostgreSQL → cleanup
  → stop proof, observability, performance measurement, bottleneck analysis, and
  production-readiness work remain uncompleted.

## Next recommended step

An AWS account administrator should immediately stop the one instance with the
`Project=PulseFlow`, `Environment=performance`, `ManagedBy=Terraform`, and
`Name=pulseflow-ec2-performance-redis` tags. Before retrying, grant or adjust the
operator policy so the accepted `m7i.large` instances may launch and the lifecycle can
create Scheduler resources, use EC2 start/stop, and use Systems Manager Run Command.
Then run a new real plan against the retained partial state, review it and its costs,
apply only that new reviewed plan, and resume the proof at bootstrap verification.
