# AWS Staging Environment Architecture

**Status:** Accepted Terraform configuration; account-specific plan saved, not applied
or deployed.

This document describes the first AWS staging environment implemented in
[`infra/aws/`](../../infra/aws/). It does not claim that an AWS account contains these
resources. The historical Render definition remains documented in
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

Terraform creates the service with zero desired tasks so normal API startup cannot
race a pending migration. [`scripts/deploy-aws-staging.ps1`](../../scripts/deploy-aws-staging.ps1)
runs a separate migration task definition using exactly the same immutable image and
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

The API/migration execution role has the managed execution policy and narrowly scoped
`secretsmanager:GetSecretValue` access to the exact registry and runtime secrets. The
temporary database verifier has a separate execution role with only the RDS master
secret. The application task role deliberately has no AWS permissions. Terraform's
local state is at `infra/aws/terraform.tfstate`, is excluded from Git, and is
sensitive because the Amazon MQ provisioned user password is generated by Terraform
and held in state.

## Verification still required

The root `.env` validation/bootstrap/plan flow has verified an AWS identity, created or
identified the external GHCR secret, verified the immutable image, and saved an
account-specific plan. No Terraform-managed AWS resource has been applied or deployed.
Complete the mandatory cost checkpoint in [`infra/aws/README.md`](../../infra/aws/README.md),
then obtain explicit approval before `terraform apply`. After approval and apply, run
`scripts/deploy-aws-staging.ps1 -Deploy` and record the actual resource IDs, migration
task result, health responses, broker state, Valkey readiness evidence, exact ingestion
event and its cleanup, and observed cost in a new checkpoint. Do not treat this document
as proof of deployment until then.
