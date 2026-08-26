# PulseFlow AWS staging Terraform

This directory is the one Terraform definition for the first real PulseFlow AWS
staging environment. It supersedes the historical, unapplied Render definition as
the deployment target; [`../render/`](../render/) remains intact as a portability
reference and must not be applied or extended as part of this flow.

The selected first-plan region is **`eu-central-1` (Frankfurt)**. All sizes and the
price checkpoint below use that region. Changing the region is a paid-architecture
change: generate a new plan and a new cost review before applying it.

## Topology and intentional staging boundary

```text
Internet
  |
ALB :80 (two public subnets)
  |
PulseFlow.Api ECS/Fargate :8080 (one task, public subnet, public IP)
  |-- RDS PostgreSQL 17, single AZ, private data subnet group
  |-- ElastiCache Serverless Valkey 8, private VPC endpoints
  `-- Amazon MQ RabbitMQ 4.2, SINGLE_INSTANCE, private data subnet
```

There is **no NAT Gateway** and no Terraform resource that creates one. The API task
has `assign_public_ip = true` solely so it can pull the selected private GHCR image
and reach AWS public control-plane APIs without a NAT Gateway. Its security group has
no public application ingress: TCP 8080 is accepted only from the ALB security group.
PostgreSQL, Valkey, and RabbitMQ are not public. This is a disposable staging cost
optimization, not a production-networking recommendation.

The public ALB listens on HTTP port 80 and forwards to Fargate port 8080. Its target
group requires `GET /health/ready` to return 200; `/health/live` remains the separate
liveness endpoint. HTTPS is deliberately deferred because PulseFlow has no accepted
domain or certificate yet.

The ECS service runs one API task after deployment. The Terraform service starts at
`desired_count = 0`, then `scripts/deploy-aws-staging.ps1` runs the same-image
migration task and changes it to exactly one only after that task exits 0. Terraform
ignores subsequent service task-definition and desired-count changes so it cannot
bypass the migration gate on later revisions. This is deliberate deployment
orchestration, not an autoscaling configuration. Increasing replicas would also
increase the co-hosted RabbitMQ consumers and is outside this staging slice.

## Selected resources

- AWS provider `~> 6.0`, locked at `6.61.0`; Random provider `~> 3.7`, locked at
  `3.9.0`; Terraform `>= 1.6.0` (validated with 1.15.9).
- A VPC, two public subnets across two AZs, two private/data subnets across the same
  AZs, an internet gateway, and the public route table. Private/data subnets receive
  only VPC-local routing.
- One public Application Load Balancer, IP target group, and HTTP listener.
- One ECS cluster, one API task definition/service, one same-image migration task
  definition, one controlled PostgreSQL verifier task definition, and one CloudWatch
  Logs group with 14-day retention.
- API/migration Fargate: Linux x86_64, **256 CPU units (0.25 vCPU) and 512 MiB**.
  The verifier has the same short-lived Fargate size. The API service ultimately has
  exactly one task.
- RDS PostgreSQL: `db.t4g.micro`, 20 GiB encrypted gp3, Single-AZ, one-day automated
  backup retention, private access, and no final snapshot on destroy. Terraform asks
  the provider for the latest available PostgreSQL **17** patch in Frankfurt at plan
  time; record the resolved `engine_version` from the reviewed plan before apply.
- ElastiCache Serverless Valkey 8: private IPv4 VPC endpoints in the data subnets,
  a 1 GB maximum data guardrail, and a 1,000 ECPU/s maximum guardrail. It has no
  paid minimum pre-scaling. Serverless Valkey always enables encryption in transit,
  so the API receives `ConnectionStrings__Redis=<endpoint>,ssl=true`.
- Amazon MQ RabbitMQ: version 4.2, `mq.m7g.medium`, `SINGLE_INSTANCE`, not publicly
  accessible, with its 200 GB default EBS volume for this single-instance M7g broker.
  It exposes private AMQPS on
  5671. The existing `RabbitMQ.Client` URI parsing receives an `amqps://` URI;
  no application code changes are required.

Security groups use explicit SG-to-SG paths: internet to ALB HTTP, ALB to API 8080,
API to PostgreSQL 5432, API to Valkey 6379, and API to Amazon MQ 5671. The API task
also has outbound HTTPS and DNS for GHCR image pulls and AWS APIs. It does not receive
direct public traffic. The one-shot database verifier is an internal deployment
operation using the API task security group; it has no listener or inbound rule.

## Runtime configuration, secrets, and IAM

All deployment-specific application configuration is supplied as normal ECS
environment variables: the ingestion, RabbitMQ, PostgreSQL, Redis, startup, and
health-check settings currently in `src/PulseFlow.Api/appsettings.json`. The
non-secret `Logging` and `AllowedHosts` defaults remain in the immutable image because
this staging definition does not override them. Connection strings are secrets:

- `ConnectionStrings__PulseFlow` is a complete Npgsql connection string;
- `ConnectionStrings__RabbitMq` is a complete AMQPS URI; and
- `ConnectionStrings__Redis` is non-secret because this staging Valkey configuration
  uses VPC/security-group access with TLS, not a password. It still requires
  `ssl=true`.

RDS manages its own master password in a Secrets Manager secret. Terraform creates
the metadata for the two application-connection secrets but intentionally never reads
or stores their values. The deployment script reads the RDS-managed secret locally,
creates the complete Npgsql connection string in memory, and writes it directly to
the application secret. It uses TLS with `Ssl Mode=Require`; certificate-chain
verification is deferred for this private disposable environment via `Trust Server
Certificate=true` and must be hardened before production.

Amazon MQ requires its initial RabbitMQ user while Terraform creates the broker.
Terraform generates that password, so **local Terraform state is sensitive**: it
contains the generated Amazon MQ password and the provider's sensitive broker-user
data. The RDS managed password and GHCR token are not in state. State lives by
default at `infra/aws/terraform.tfstate`; the repository `.gitignore` already excludes
it, state backups, plans, provider work directories, and real `.tfvars` files. Keep
the state and any backup in encrypted developer-controlled storage. Losing it makes
future Terraform management substantially harder.

The GHCR package-read credential is deliberately outside Terraform and outside state.
ECS `repositoryCredentials` references its existing Secrets Manager ARN. The API and
migration execution role has only the managed ECS execution policy plus
`GetSecretValue` for the GHCR credential and the two runtime connection secrets. The
temporary database verifier has a separate execution role that can read only the RDS
master secret. The application task role has no AWS permissions. The first provisioned
Amazon MQ user is administrative because Amazon MQ RabbitMQ permits only that one user
during creation;
creating a separate restricted application user through its private management API is
deferred rather than silently adding a management path.

## Local bootstrap, plan, and required approval

The repository-root `.env` is the one local bootstrap source. Copy
[`.env.example`](../../.env.example) to `.env` and replace its placeholders. `.env` is
ignored by Git: do not commit it, paste it into chat, or copy its values into `.tfvars`
files.

The following variables are required:

- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `AWS_REGION`
- `AWS_DEFAULT_REGION` (must equal `AWS_REGION`)
- `GHCR_USERNAME`
- `GHCR_TOKEN`
- `PULSEFLOW_IMAGE`

`PULSEFLOW_IMAGE` must be a published immutable
`ghcr.io/<owner>/pulseflow-api:sha-<40-lowercase-hex-characters>` image. Use a GitHub
credential that has only the package-read access needed for that image.

From the repository root, install Terraform and AWS CLI v2, then run:

```powershell
pwsh ./scripts/deploy-aws-staging.ps1
```

The command uses the standard AWS environment-variable credential chain; no AWS CLI
profile is required. An existing profile remains an optional override for operators
who explicitly need one:

```powershell
pwsh ./scripts/deploy-aws-staging.ps1 -AwsProfile <optional-profile-name>
```

The default command performs this reproducible, plan-only sequence:

1. loads root `.env` into the current process and validates every required variable;
2. runs `aws sts get-caller-identity` in `AWS_REGION`;
3. identifies `pulseflow-staging/bootstrap/ghcr` in AWS Secrets Manager, then creates
   it with the current GHCR credential only if it is absent;
4. verifies that `PULSEFLOW_IMAGE` is reachable in GHCR with the supplied package-read
   credential;
5. runs `terraform fmt -check -recursive`, `terraform init`, and `terraform validate`;
6. runs the real Terraform plan, passing only `aws_region`, `pulseflow_image`, and the
   resulting GHCR secret ARN as Terraform variables; and
7. saves the plan to `infra/aws/pulseflow-staging.tfplan` and stops.

The GHCR JSON value (`username` and `password`) is written to a temporary local file
only so AWS CLI does not receive the token as a command-line argument; the file is
removed immediately. Terraform receives only the secret ARN. The GHCR token is never
placed in Terraform state, a plan, or a `.tfvars` file. The command does not run
`terraform apply`.

The dotenv parser accepts blank lines, full-line comments, `KEY=value`, values that
contain `=`, and matching optional single or double quotes around values. It does not
write credential or token values to output.

## Explicit disposable staging lifecycle

The first intended use is deliberately short-lived: create the staging environment,
deploy and prove the API end to end, record the evidence, then remove the paid
infrastructure. Keep every lifecycle phase separate and inspectable:

1. **PLAN** — bootstrap the external GHCR credential if needed, validate the image and
   Terraform configuration, then save a plan. This command does not apply it.

   ```powershell
   pwsh ./scripts/deploy-aws-staging.ps1
   ```

2. **Review plan/cost and approve** — inspect
   `infra/aws/pulseflow-staging.tfplan`, its planned resources, and the cost checkpoint
   below. Obtain explicit approval before continuing.

3. **APPLY** — load root `.env`, validate the AWS identity, and apply only the existing
   reviewed saved plan. It does not create a replacement plan and does not run a
   deployment. After success, the ECS service remains at desired count zero.

   ```powershell
   pwsh ./scripts/deploy-aws-staging.ps1 -Apply
   ```

4. **DEPLOY + VERIFY** — run the migration, roll out the API, require health checks,
   and execute the ingestion smoke proof. This phase is never started by `-Apply`.

   ```powershell
   pwsh ./scripts/deploy-aws-staging.ps1 -Deploy
   ```

5. **DESTROY** — load root `.env`, validate the AWS identity, resolve the existing
   external GHCR secret ARN, destroy Terraform-managed staging resources, and verify
   that Terraform state is empty.

   ```powershell
   pwsh ./scripts/deploy-aws-staging.ps1 -Destroy
   ```

   Normal destroy removes the expensive staging environment but deliberately keeps the
   small external `pulseflow-staging/bootstrap/ghcr` credential secret. It is not
   Terraform-managed and can be reused for a later disposable run.

   To delete that external bootstrap secret as well, request it explicitly after the
   Terraform destruction succeeds:

   ```powershell
   pwsh ./scripts/deploy-aws-staging.ps1 -Destroy -DeleteBootstrapSecret
   ```

   `-DeleteBootstrapSecret` is valid only with `-Destroy`; it never runs implicitly.
   It uses AWS's force-delete-without-recovery behavior, so treat this complete
   bootstrap cleanup as permanent.

Review every planned resource. It must contain no NAT gateway, no RDS public access,
no public Valkey or broker, no Multi-AZ RDS, no RabbitMQ cluster, and no API replicas.
The plan will also show the exact RDS PostgreSQL 17 patch selected for Frankfurt.

### Candidate cost checkpoint — must be rechecked against the reviewed plan

These are current public AWS Price List figures retrieved 2026-08-26 for the selected
Frankfurt resources and a 730-hour month. They are an approval estimate, **not an
actual bill or an applied-environment record**.

| Fixed-ish item | Calculation | Approx. USD/month |
| --- | --- | ---: |
| Amazon MQ | `mq.m7g.medium` single instance: `$0.1638 × 730`, plus 200 GB default EBS at `$0.119/GB-month` | 143.37 |
| RDS PostgreSQL | `db.t4g.micro`: `$0.019 × 730`, plus 20 GiB gp3 at `$0.137/GiB-month` | 16.61 |
| Valkey Serverless minimum storage | 0.1 GB minimum × `$0.101/GB-hour × 730` | 7.37 |
| ECS Fargate API | 0.25 vCPU at `$0.04656/vCPU-hour` + 0.5 GB at `$0.00511/GB-hour`, × 730 | 10.36 |
| ALB hourly charge | `$0.027 × 730` | 19.71 |
| Public IPv4 | two ALB addresses plus one running Fargate task: `3 × $0.005 × 730` | 10.95 |
| Secrets Manager | four persistent secrets × `$0.40` | 1.60 |
| **Fixed-ish baseline** | Excludes variable use below | **about 209.98** |

Usage-dependent charges are not included: ALB LCU-hours (`$0.008` each), Valkey
ECPUs (`$0.0027` per million), Amazon MQ VPC-resource consumer data (`$0.01/GB`),
CloudWatch Logs custom ingestion (`$0.63/GB`) and retained storage (`$0.0324/GB-month`),
Secrets Manager API calls (`$0.05` per 10,000), internet/data-transfer effects, temporary
migration/verifier task runtime, backup/snapshot use beyond included RDS backup storage,
and any future resource or price change. AWS's account-level data-transfer-out free tier
and tiered rates must be applied to the account's actual usage. The Amazon MQ broker is
the dominant always-on cost.

Before the first `terraform apply`, present the saved plan's exact resources and this
updated cost checkpoint to the owner, then obtain one explicit approval for these paid
resources. Do not apply merely because this definition validates.

## Controlled migration, rollout, and smoke proof

After approval, use the explicit `-Apply` phase to apply only the reviewed saved plan.
The initial ECS service remains at zero tasks so normal API startup cannot race the
migration bundle. `-Apply` stops after infrastructure creation; `-Deploy` must be
requested separately.

```powershell
pwsh ./scripts/deploy-aws-staging.ps1 -Apply
pwsh ./scripts/deploy-aws-staging.ps1 -Deploy
```

Run both commands from the repository root. The post-apply rollout performs, in order:

1. builds the two runtime connection-string secrets without printing them;
2. verifies Amazon MQ is running, then runs `/app/migrations/pulseflow-migrations`
   from the selected immutable API image in one Fargate task;
3. stops immediately if that task has no successful exit code, so the API service is
   not updated;
4. updates the service to the same immutable image with desired count one, waits for
   ECS stability, and requires both `/health/live` and `/health/ready` to return 200;
5. sends one unique Contract v2 NDJSON event through the ALB, requires HTTP 202, then
   runs a non-persistent `postgres:17-alpine` Fargate verifier inside the VPC until
   that exact `(source, eventId)` row exists in PostgreSQL; it then removes only that
   smoke-test record with a second one-shot verifier task.

The verifier writes task evidence to the common CloudWatch log group and creates no
permanent operator path. A successful readiness endpoint proves the existing startup
initialization and current health checks can reach PostgreSQL, Valkey, and RabbitMQ;
the exact-row verifier completes the client → ALB → API → RabbitMQ consumer → RDS
proof. Keep the task ARNs printed by the script in the deployment checkpoint.

For direct operator inspection, PostgreSQL, Valkey, and Amazon MQ remain private.
No bastion or permanent management endpoint is created. ECS Exec/SSM-based temporary
operator access is deferred because it would add command-audit and IAM design work
without being required to prove this slice.

## Official references checked for this configuration

- [HashiCorp AWS provider: ECS task definition](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/ecs_task_definition), [ECS service](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/ecs_service), [Amazon MQ broker](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/mq_broker), [RDS instance](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/db_instance), and [ElastiCache Serverless cache](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/elasticache_serverless_cache)
- [AWS ECS task execution role and private registry authentication](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/task_execution_IAM_role.html)
- [Amazon MQ RabbitMQ engine versions](https://docs.aws.amazon.com/amazon-mq/latest/developer-guide/rabbitmq-version-management.html), [instance types](https://docs.aws.amazon.com/amazon-mq/latest/developer-guide/rmq-broker-instance-types.html), and [TLS client connectivity](https://docs.aws.amazon.com/amazon-mq/latest/developer-guide/rabbitmq-on-amazon-mq.html)
- [RDS PostgreSQL version selection](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/PostgreSQL.Concepts.General.DBVersions.html) and [RDS-managed master password provider support](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/db_instance)
- [ElastiCache Serverless TLS](https://docs.aws.amazon.com/AmazonElastiCache/latest/dg/in-transit-encryption.html) and [serverless cache VPC network type/subnets](https://docs.aws.amazon.com/AmazonElastiCache/latest/dg/serverless-network-type.html)
- [AWS Fargate pricing](https://aws.amazon.com/fargate/pricing/), [Amazon MQ pricing](https://aws.amazon.com/amazon-mq/pricing/), [ElastiCache pricing](https://aws.amazon.com/elasticache/pricing/), [Secrets Manager pricing](https://aws.amazon.com/secrets-manager/pricing/), and [public IPv4 pricing](https://aws.amazon.com/vpc/pricing/)

## Destruction and recreation

The successful environment is intentionally left running until the owner decides
otherwise. Amazon MQ, ALB, RDS, public IPv4, and Fargate all incur charges while they
exist or run; review the current AWS bill regularly.

To recreate, repeat the lifecycle above: root `.env` → bootstrap/identify GHCR secret
→ immutable-image verification → plan → cost review → explicit approval → `-Apply` →
`-Deploy` rollout.

To remove the disposable environment after evidence has been recorded, run:

```powershell
pwsh ./scripts/deploy-aws-staging.ps1 -Destroy
```

`-Destroy` uses the same non-secret Terraform inputs (`aws_region`, `pulseflow_image`,
and the existing GHCR credential-secret ARN). It does not pass the GHCR token to
Terraform. The command waits for Terraform destruction to finish and then requires
Terraform state to contain no managed resources.

Destroying this disposable environment removes RDS, its data (there is no final
snapshot), Valkey state, Amazon MQ broker data, generated RabbitMQ credentials, ECS
resources, ALB, public IP allocation, and the Terraform-managed runtime-secret
metadata. Back up any PostgreSQL data worth retaining before destruction. The
externally bootstrapped GHCR credential secret is deliberately not Terraform-owned and
remains after normal destroy. Use `-Destroy -DeleteBootstrapSecret` only when that
reusable bootstrap configuration should also be removed.
