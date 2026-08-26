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
  accessible, with its normal 5 GiB default EBS volume. It exposes private AMQPS on
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

## Bootstrap

Install Terraform and AWS CLI v2 locally, configure a dedicated staging AWS profile,
and verify its target account before continuing. Do not paste credentials into this
repository or into ChatGPT.

```powershell
aws configure --profile pulseflow-staging
aws sts get-caller-identity --profile pulseflow-staging
```

Create a GitHub credential restricted to package read access for the private GHCR
package. The following prompt-based command writes it directly to Secrets Manager and
does not echo the token. Replace only the non-secret GitHub username and AWS account
ID placeholders. Use an account/profile that is permitted to create this bootstrap
secret.

```powershell
$region = "eu-central-1"
$githubUser = "<github-user>"
$secureToken = Read-Host "GHCR package-read token" -AsSecureString
$tokenBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenBstr)
    $credentialJson = @{ username = $githubUser; password = $token } | ConvertTo-Json -Compress
    aws secretsmanager create-secret `
      --profile pulseflow-staging `
      --region $region `
      --name "pulseflow-staging/bootstrap/ghcr" `
      --secret-string $credentialJson `
      --query ARN `
      --output text
}
finally {
    if ($tokenBstr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenBstr)
    }
    Remove-Variable token -ErrorAction SilentlyContinue
    Remove-Variable credentialJson -ErrorAction SilentlyContinue
}
```

Copy `terraform.tfvars.example` to a secure non-committed `.tfvars` location and set
the immutable image and the command's returned secret ARN. Publish the selected image
first through the manual **Publish PulseFlow.Api image** GitHub workflow. Never use
`develop` or `latest` for `pulseflow_image`.

## Validation, plan, and required approval

From `infra/aws/`:

```powershell
terraform fmt -check
terraform init -backend=false
terraform validate

# Do not apply until the complete plan and cost checkpoint are reviewed.
terraform plan -var-file <secure-path-to-staging.tfvars> -out pulseflow-staging.tfplan
```

Review every planned resource. It must contain no NAT gateway, no RDS public access,
no public Valkey or broker, no Multi-AZ RDS, no RabbitMQ cluster, and no API replicas.
The plan will also show the exact RDS PostgreSQL 17 patch selected for Frankfurt.

### Candidate cost checkpoint — must be rechecked against the reviewed plan

These are current public AWS Price List figures retrieved 2026-08-26 for the selected
Frankfurt resources and a 730-hour month. They are an approval estimate, **not an
actual bill or an applied-environment record**.

| Fixed-ish item | Calculation | Approx. USD/month |
| --- | --- | ---: |
| Amazon MQ | `mq.m7g.medium` single instance: `$0.1638 × 730`, plus 5 GiB default EBS at `$0.119/GiB-month` | 120.17 |
| RDS PostgreSQL | `db.t4g.micro`: `$0.019 × 730`, plus 20 GiB gp3 at `$0.137/GiB-month` | 16.61 |
| Valkey Serverless minimum storage | 0.1 GB minimum × `$0.101/GB-hour × 730` | 7.37 |
| ECS Fargate API | 0.25 vCPU at `$0.04656/vCPU-hour` + 0.5 GB at `$0.00511/GB-hour`, × 730 | 10.36 |
| ALB hourly charge | `$0.027 × 730` | 19.71 |
| Public IPv4 | two ALB addresses plus one running Fargate task: `3 × $0.005 × 730` | 10.95 |
| Secrets Manager | four persistent secrets × `$0.40` | 1.60 |
| **Fixed-ish baseline** | Excludes variable use below | **about 186.77** |

Usage-dependent charges are not included: ALB LCU-hours (`$0.008` each), Valkey
ECPUs (`$0.0027` per million), CloudWatch Logs ingestion/storage, Secrets Manager API
calls, image pull/network/data-transfer effects, temporary migration/verifier task
runtime, backup/snapshot use beyond included RDS backup storage, and any future
resource or price change. The Amazon MQ broker is the dominant always-on cost.

Before the first `terraform apply`, present the saved plan's exact resources and this
updated cost checkpoint to the owner, then obtain one explicit approval for these paid
resources. Do not apply merely because this definition validates.

## Controlled migration, rollout, and smoke proof

After approval, apply only the reviewed saved plan. The initial ECS service remains at
zero tasks so normal API startup cannot race the migration bundle.

```powershell
terraform apply pulseflow-staging.tfplan
pwsh ./scripts/deploy-aws-staging.ps1 -AwsProfile pulseflow-staging
```

Run the script from the repository root. It performs, in order:

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

To recreate, repeat: configure profile → bootstrap/reference GHCR secret → publish a
SHA image → init/validate → plan/cost approval → apply → run the deployment script.

To destroy, first inspect the destructive plan:

```powershell
terraform plan -destroy -var-file <secure-path-to-staging.tfvars>
terraform destroy -var-file <secure-path-to-staging.tfvars>
```

Destroying this disposable environment removes RDS, its data (there is no final
snapshot), Valkey state, Amazon MQ broker data, generated RabbitMQ credentials, ECS
resources, ALB, public IP allocation, and the Terraform-managed runtime-secret
metadata. Back up any PostgreSQL data worth retaining before destruction. The
externally bootstrapped GHCR credential secret is deliberately not Terraform-owned;
delete it separately when no longer needed.
