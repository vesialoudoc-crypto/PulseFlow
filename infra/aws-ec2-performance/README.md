# EC2 Performance Environment

**Status:** Accepted low-cost Terraform configuration. The failed first apply did not
reach bootstrap or runtime deployment, and its partial environment was removed in
[checkpoint 077](../../docs/progress/2026-08-27-077-destroy-old-ec2-performance-environment.md).
A fresh 48 GiB saved plan exists for the one-time proof. This is not an end-to-end
proof: no apply or runtime deployment has occurred.

This Terraform root is a second AWS environment, separate from the successful
managed-service proof in [`../aws/`](../aws/). The managed proof remains historical
evidence that PulseFlow works with ECS, RDS, ElastiCache, Amazon MQ, and an ALB. This
environment exists for short, lower-fixed-cost performance experiments and does not
replace it.

The accepted architecture is described in
[AWS EC2 Performance Environment](../../docs/architecture/aws-ec2-performance-environment.md)
and [ADR 0023](../../docs/decisions/0023-prioritize-low-cost-ec2-performance-proof.md).
[ADR 0024](../../docs/decisions/0024-minimize-one-time-ec2-proof-storage.md) records
the final one-time proof storage and scheduling correction. ADR 0022 remains the
historical record of the original EC2 decision.

## Topology

```text
EC2 app (c7i-flex.large)
├─ HAProxy :80
├─ PulseFlow.Api #1
└─ PulseFlow.Api #2
        │ private IPv4 only
        ├── EC2 rabbitmq (t3.small) ─ RabbitMQ :5672
        ├── EC2 redis (t3.small) ─ Redis :6379
        └── EC2 postgres (t3.small) ─ PostgreSQL :5432

EC2 loadgen (t3.small) ─ k6 ──private HTTP──> EC2 app :80
```

All runtime services are Docker containers. The nodes use the same x86_64 immutable
GHCR API image and the same migration bundle as the managed AWS proof. Docker Compose
and the application configuration deliberately contain no ECS, RDS, Amazon MQ, or
ElastiCache dependency; another Linux VM provider can use the same layouts with
different addresses and operator access.

## Resources and sizing

| Role | Instance type | Root gp3 disk | Reason |
| --- | --- | ---: | --- |
| `app` | `c7i-flex.large` (2 vCPU, 4 GiB) | 8 GiB | Runs HAProxy and two API containers for a short cloud deployment proof. |
| `rabbitmq` | `t3.small` (2 vCPU, 2 GiB) | 8 GiB | Holds bounded broker state without sizing for long cloud benchmarks. |
| `redis` | `t3.small` (2 vCPU, 2 GiB) | 8 GiB | Redis holds only distributed rate-limit state. |
| `postgres` | `t3.small` (2 vCPU, 2 GiB) | 16 GiB | Holds the minimum short-proof PostgreSQL data and WAL allowance. |
| `loadgen` | `t3.small` (2 vCPU, 2 GiB) | 8 GiB | Runs the short k6 smoke or controlled load scenario. |

This topology prioritizes AWS cost over clean long-duration benchmark inputs.
The x86_64 architecture and requested `c7i-flex.large` shape were inspected, and an
EC2 `RunInstances` dry-run returned `DryRunOperation`. That result checks the request
without launching an instance; an actual `c7i-flex.large` launch remains unproven
until a future reviewed apply. The four support nodes use `t3.small`; their CPU-credit
behavior is acceptable for short cloud proofs. Long sustained performance experiments
belong to the local/home environment. This is not a capacity claim.
The allocation is 48 GiB of gp3 roots in total. Every root size is at least the
selected Amazon Linux 2023 AMI snapshot's 8 GiB minimum; it deliberately adds no
production storage headroom.
Every root disk is encrypted gp3 and is the only EBS disk for its node. Terraform
terminates it with the instance; stopping an instance preserves it. Docker named
volumes on the host root filesystem persist PostgreSQL, RabbitMQ, and Redis data.
No additional data-volume resource exists.

## Network and access

All five instances have fixed primary private IPv4 addresses in one single-AZ VPC
subnet:

| Role | Private address |
| --- | --- |
| `app` | `10.43.10.10` |
| `rabbitmq` | `10.43.10.20` |
| `redis` | `10.43.10.30` |
| `postgres` | `10.43.10.40` |
| `loadgen` | `10.43.10.50` |

The application Compose file uses those addresses directly, avoiding a Route 53
private-zone cost and preventing service discovery from changing after a stop/start.
The VPC uses a public subnet only for outbound HTTPS: Amazon Linux package downloads,
Docker image pulls, GHCR, and Systems Manager. Each running instance receives an
auto-assigned public IPv4 address but security groups create **no public inbound
rule**. The address is released by EC2 on stop; no Elastic IP is allocated.

The only inter-node inbound rules are:

- `loadgen` security group → `app` TCP 80 (HAProxy);
- `app` security group → `rabbitmq` TCP 5672;
- `app` security group → `redis` TCP 6379; and
- `app` security group → `postgres` TCP 5432.

Every node can make HTTPS egress for SSM and container/bootstrap downloads, plus VPC
DNS. There is no SSH key pair, bastion, ALB, NAT gateway, public database endpoint,
broker management endpoint, or broad application ingress. Operators execute lifecycle
and inspection commands through AWS Systems Manager Run Command. Amazon Linux 2023
includes AWS CLI v2 and normally has SSM Agent installed; bootstrap makes the agent
and Docker service explicit. See the [AWS SSM AMI guidance](https://docs.aws.amazon.com/systems-manager/latest/userguide/ami-preinstalled-agent.html).

## Bootstrapping and secrets

Terraform resolves the current Amazon Linux 2023 x86_64 public AMI parameter unless
an operator explicitly supplies `ami_id`. On first boot each node:

1. installs Docker Engine, `curl`, and `jq` from Amazon Linux 2023;
2. installs the pinned Docker Compose plugin;
3. writes only its role-specific Compose and helper scripts under `/opt/pulseflow`;
4. enables Docker and SSM; and
5. writes `/opt/pulseflow/.bootstrap-complete` after the local runtime tooling is
   ready.

Without an AMI override, the Terraform operator needs `ssm:GetParameter` for the
public `/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64`
parameter. This is a plan-time read permission, separate from the SSM instance role.
When `PULSEFLOW_EC2_AMI_ID` is set to a reviewed Amazon Linux 2023 x86_64 AMI ID, the
SSM data source is not declared and this permission is not needed for that plan.

The app node retrieves its immutable GHCR image credential and the two runtime
credentials from AWS Secrets Manager through a narrowly scoped instance role. It logs
in only to pull the selected immutable image, then logs out. PostgreSQL and RabbitMQ
nodes can read only their own credentials. The existing
`pulseflow-staging/bootstrap/ghcr` secret is reused, so `-DeleteBootstrapSecret` in
either AWS lifecycle must be used only after **both** environments are gone.

Before Terraform planning, the lifecycle script first verifies the `.env` GHCR
credential against the selected immutable image. It then reads the shared secret and
writes a new version when its username or token differs, so a successful plan cannot
deploy with a stale registry credential. This requires `secretsmanager:GetSecretValue`
and, on a credential change, `secretsmanager:PutSecretValue`; neither secret value is
written to Terraform state or command-line output.

The generated PostgreSQL and RabbitMQ passwords are sensitive Terraform state. Keep
the local state and its backup encrypted and outside Git, just as for `infra/aws/`.

## Business-hours schedule

EventBridge Scheduler invokes the EC2 `StartInstances` and `StopInstances` APIs with
the schedule expression timezone `Europe/Warsaw`:

```text
Monday–Friday 08:00 — start all five nodes
Monday–Friday 17:00 — stop all five nodes
```

The schedule has no flexible window. EventBridge Scheduler uses IANA time zones and
automatically adjusts cron schedules for daylight-saving time; it is therefore used
instead of fixed-UTC cron expressions. See [AWS Scheduler time-zone and DST
behavior](https://docs.aws.amazon.com/scheduler/latest/UserGuide/schedule-types.html).

Terraform `-Apply` waits for bootstrapping and then stops the new nodes deliberately.
This avoids an out-of-hours apply becoming an unattended compute charge. Scheduling
will restart only containers that have already been deployed; it does not turn a
freshly provisioned host into an implicit release.

For the current one-time deployment proof, set the existing
`enable_business_hours_schedule` input to `false` when creating the saved plan. This
omits the Scheduler group and schedules because the active principal cannot create
them and the lifecycle is manually stopped immediately after validation. The default
remains `true` so the Terraform design retains the accepted Scheduler support for a
later recurring environment.

If an apply fails before Terraform can return all five node IDs, its emergency-stop
path discovers only active instances with the exact `Project=PulseFlow`,
`Environment=performance`, and `ManagedBy=Terraform` tags, then attempts to stop
those discovered IDs. It never performs an account-wide stop.

## Lifecycle

Copy [`.env.example`](../../.env.example) to the repository-root `.env` and provide
the documented AWS credentials, read-only GHCR token, and immutable image tag. Do not
put a token into a Terraform variable file.

To use a named shared-credentials profile, supply `-AwsProfile` on the plan command.
The script passes that exact profile to both every AWS CLI call and Terraform's
`aws_profile` input. The saved plan records its profile and AWS account; `-Apply`
adopts that profile when omitted or rejects a different supplied profile/account.
Do not set `aws_profile` separately in a local Terraform variable file.

Every invocation first resolves the active AWS account through STS. The account guard
then differs by command before it can change infrastructure: `-Apply` requires the
saved plan account to match; `-Deploy`, `-RunBaseline`, `-Stop`, and `-Destroy` require
the `aws_account_id` recorded in Terraform state to match. `-Destroy` therefore cannot
reach Terraform destroy or shared-secret deletion under a different account context.

Run commands from the repository root.

1. Create and review a real plan. This may create the shared external GHCR secret on
   first use, but does not create EC2 resources.

   ```powershell
   pwsh ./scripts/deploy-aws-ec2-performance.ps1
   ```

   Add `-AwsProfile <profile-name>` when using a named profile.

2. Review the saved plan, current regional prices, and expected experiment duration.
   Obtain explicit approval before the next command.

3. Apply only the saved plan. The script confirms each host bootstrap then stops all
   nodes.

   ```powershell
   pwsh ./scripts/deploy-aws-ec2-performance.ps1 -Apply
   ```

4. Start the hosts, pull the runtime images, start PostgreSQL/RabbitMQ/Redis, run the
   image-contained migration bundle, start HAProxy and both API replicas, verify
   `/health/ready` from the k6 node, run one k6 smoke event, verify its exact row in
   PostgreSQL, and remove only that smoke row.

   ```powershell
   pwsh ./scripts/deploy-aws-ec2-performance.ps1 -Deploy
   ```

5. Trigger the existing 10-second ingestion baseline from the isolated k6 node. This
   copies the existing `ingestion-baseline.js` semantics and lets the operator choose
   only the virtual-user count.

   ```powershell
   pwsh ./scripts/deploy-aws-ec2-performance.ps1 -RunBaseline -VirtualUsers 10
   ```

   This command is an input-pressure trigger, not a completed bottleneck conclusion.
   Capture Docker statistics and dependency evidence through SSM during an approved
   experiment, then record the methodology and result separately.

6. Stop early when the experiment is finished instead of waiting for 17:00.

   ```powershell
   pwsh ./scripts/deploy-aws-ec2-performance.ps1 -Stop
   ```

7. Remove the entire environment when its evidence has been recorded.

   ```powershell
   pwsh ./scripts/deploy-aws-ec2-performance.ps1 -Destroy
   ```

`-Destroy -DeleteBootstrapSecret` is deliberately exceptional: the secret is shared
with `infra/aws/`, and deletion is permanent. It is appropriate only when neither AWS
topology will be recreated with the existing credential.

## Cost review

Recheck current Frankfurt prices in the AWS Pricing Calculator before every plan. The
main recurring categories are:

- running EC2 instance-hours (one `c7i-flex.large` node and four `t3.small` nodes);
- gp3 root-volume GiB-month charges, which continue while nodes are stopped;
- auto-assigned public IPv4 hours while nodes are running;
- Secrets Manager storage for the shared GHCR secret and two runtime credential
  secrets;
- CloudWatch/SSM command output, EBS snapshots if an operator adds them, and network
  transfer/image-pull traffic; and
- any manual resources outside Terraform.

Stopping ends EC2 compute and the temporary public-IPv4 charge, but not root EBS or
Secrets Manager. A NAT Gateway, Elastic IP, ALB, interface VPC endpoints, additional
EBS volumes, and managed broker/database/cache services are intentionally absent
because their fixed charges would undermine short experiments. Public auto-assigned
IPv4 addresses are released on stop, while the nodes' private addresses and EBS root
volumes persist. See [AWS EC2 stop/start billing and persistence](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/how-ec2-instance-stop-start-works.html).
