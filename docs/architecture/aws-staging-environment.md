# AWS Staging Environment Architecture

**Status:** Historical managed AWS proof; first disposable lifecycle verified and
Terraform-managed staging resources destroyed. Its implementation was removed from
the active repository tree and is preserved by Git history, ADRs, pull requests, and
historical checkpoints.

This document records the first managed AWS staging environment. It does not claim
that an AWS account contains these resources or that the former implementation remains
in the active tree. The historical Render definition remains documented in
[First Staging Environment Architecture](staging-environment.md) as an unapplied
experiment; it has not been deleted or rewritten.

The accepted mapping and resource configuration are recorded in
[ADR 0020](../decisions/0020-use-aws-for-first-cloud-deployment.md) and
[ADR 0021](../decisions/0021-define-first-aws-staging-resource-configuration.md).

## Target topology

```text
Internet
  |
Application Load Balancer, public subnets, HTTP :80
  |
  | ALB security group -> API security group :8080 only
  v
ECS Fargate: one PulseFlow.Api task, public subnet, public IP
  |\
  | \-- HTTPS/DNS egress for GHCR pull and AWS APIs
  |
  +-- RDS PostgreSQL 17, private data subnets, :5432
  +-- ElastiCache Serverless Valkey 8, private VPC endpoints, TLS :6379
  `-- Amazon MQ RabbitMQ 4.2, private data subnet, AMQPS :5671
```

`eu-central-1` (Frankfurt) is selected for the first plan. Two public and two data
subnets span the first two available AZs. The public route table has an internet
gateway route; the data subnets have no internet route. Terraform contains no NAT
Gateway resource.

The Fargate task has a public IP to pull the private GHCR image without a NAT Gateway.
That does not expose the API directly: its security group has no public inbound rule,
and permits TCP 8080 only from the ALB security group. RDS, Valkey, and RabbitMQ have
no public endpoints and receive only SG-to-SG application traffic.

This network is intentionally a disposable-staging compromise. A later production
design must separately decide private task subnets and egress/image-distribution
strategy.

## Compute, ingress, and lifecycle

The API task definition uses the selected immutable
`ghcr.io/<owner>/pulseflow-api:sha-<40-character-sha>` image, Linux x86_64, 256 CPU
units, 512 MiB, `awsvpc`, Fargate, and container port 8080. The service ultimately
runs one task; raising that number also raises the co-hosted RabbitMQ consumer count.
`/health/ready` is the ALB target health check and `/health/live` remains a separate
liveness endpoint.

The historical Terraform configuration created the service with zero desired tasks so
normal API startup could not race a pending migration. The former deployment tooling
ran a separate migration task definition using exactly the same immutable image and
the command:

```text
/app/migrations/pulseflow-migrations --connection "$ConnectionStrings__PulseFlow"
```

The script checks the one-shot task exit code. Only exit code zero permits it to set
the API service to desired count one and the same candidate API task definition. A
failed migration stops the script before service update.

## Managed dependencies

RDS is a private, encrypted, Single-AZ `db.t4g.micro` instance with 20 GiB gp3
storage and one-day backup retention. The provider resolves the latest available
PostgreSQL 17 patch at plan time. RDS manages its master password; the deployment
script creates the complete application Npgsql connection string without importing
the password into Terraform state.

ElastiCache Serverless Valkey uses two data subnets, a 1 GB maximum data guardrail,
and a 1,000 ECPU/s maximum guardrail. Serverless Valkey always uses encryption in
transit. `StackExchange.Redis` receives the TLS-enabled endpoint through the existing
`ConnectionStrings__Redis` key; it remains operational rate-limit state rather than
durable primary storage.

Amazon MQ is a private RabbitMQ 4.2 `mq.m7g.medium` in `SINGLE_INSTANCE` mode with
its 200 GB default EBS volume. The API gets a normal `amqps://` URI through its existing
`ConnectionStrings__RabbitMq` contract and connects on port 5671. It is an
evaluation-class, non-HA broker; it does not introduce a RabbitMQ cluster, SQS, SNS,
EventBridge, or a separate worker.

## Secrets, roles, and state

An external AWS Secrets Manager secret contains the GHCR package-read credential. The
repository-root ignored `.env` supplies the local AWS environment variables,
`GHCR_USERNAME`, `GHCR_TOKEN`, and immutable `PULSEFLOW_IMAGE` to the plan bootstrap
script. It creates or updates the external GHCR secret without passing its token to
Terraform, then Terraform receives only its ARN. RDS owns the master-password secret;
Terraform creates empty metadata for the application PostgreSQL and RabbitMQ
connection-string secrets; the post-apply deployment script fills their values in
memory immediately before the migration task.

The API/migration execution role had the managed execution policy and narrowly scoped
`secretsmanager:GetSecretValue` access to the exact registry and runtime secrets. The
temporary database verifier had a separate execution role with only the RDS master
secret. The application task role deliberately had no AWS permissions. The former
local Terraform state was excluded from Git and was sensitive because the Amazon MQ
provisioned user password was generated by Terraform and held in state.

## Observed first lifecycle

The first approved disposable lifecycle has been observed without recording sensitive
values or resource identifiers. Terraform apply reported 49 added, 0 changed, and 0
destroyed. The controlled deployment then completed the migration ECS task, received
HTTP 200 from the public ALB for both `/health/live` and `/health/ready`, received HTTP
202 from the ingestion smoke POST, verified the exact event in PostgreSQL through the
in-VPC verifier task, and removed that exact smoke row.

Terraform destroy reported 49 destroyed. Its post-destroy verification encountered a
PowerShell empty-pipeline `.Count` defect, but a subsequent non-mutating
`terraform state list` was empty. The Terraform-managed staging environment is
therefore gone. The external GHCR bootstrap secret intentionally remains reusable
because normal `-Destroy` was used without `-DeleteBootstrapSecret`.

The first deployment corrected a tooling assumption: the RDS-managed master secret
contains credentials only. The deployment script resolves the endpoint, port, and
database name from `rds describe-db-instances` and reads only the username and password
from the master secret.

## Remaining work

This historical lifecycle is not a current runbook. Stage 5 remains in progress:
automatic deployment/CI/CD, deployed observability and performance measurement, a
measured bottleneck, and one justified before/after optimization are not complete.
