# ADR 0021: Define the first AWS staging resource configuration

**Date:** 2026-08-26

## Status

Accepted

## Context

[ADR 0020](0020-use-aws-for-first-cloud-deployment.md) selected AWS for the first
real PulseFlow cloud deployment but intentionally deferred region, service sizes,
state handling, and the deployment mechanism. The implemented application boundary
is unchanged: one `PulseFlow.Api` process hosts both the HTTP API and one or more
RabbitMQ consumers, and the immutable GHCR API image includes the EF Core migration
bundle.

The first environment must preserve that boundary, cost materially less than a
production topology, avoid a NAT Gateway, keep dependencies private, and ensure a
failed migration cannot roll out a new API revision. A plan/apply has not yet been
authorized or run; this ADR accepts the configuration to be reviewed in the first
real plan, not a claim that resources already exist.

## Alternatives considered

1. Private Fargate tasks behind a NAT Gateway, with conventional private application
   networking.
2. Public-IP Fargate tasks in public subnets, protected from inbound internet traffic
   by an ALB-only security-group rule, with no NAT Gateway.
3. Node-based Valkey or Redis instead of ElastiCache Serverless Valkey.
4. A larger or clustered Amazon MQ RabbitMQ broker.
5. Running migrations from API startup or allowing Terraform's normal ECS service
   update to roll out a candidate task definition before migration verification.

## Decision

Use `eu-central-1` (Frankfurt) for the first reviewed staging plan. Define the AWS
environment with flat HashiCorp AWS-provider resources in `infra/aws/managed-legacy/`:

- a VPC with two public subnets and two private/data subnets; no NAT Gateway;
- a public HTTP Application Load Balancer, with `/health/ready` target health checks;
- public-IP Fargate API and one-shot task ENIs, where API TCP 8080 accepts traffic
  only from the ALB security group;
- one Linux x86_64 API task at 0.25 vCPU / 512 MiB and one API replica after a
  successful deployment;
- RDS PostgreSQL 17 on `db.t4g.micro`, 20 GiB encrypted gp3, Single-AZ, private
  access, and RDS-managed master credentials;
- ElastiCache Serverless Valkey 8 with a 1 GB data and 1,000 ECPU/s maximum guardrail;
- Amazon MQ RabbitMQ 4.2 on `mq.m7g.medium` in `SINGLE_INSTANCE` mode, private AMQPS
  access only; and
- local Terraform state for this one-developer disposable environment. State is
  sensitive, ignored by Git, and must be stored and backed up securely.

Keep private GHCR. Its package-read credential is manually bootstrapped in AWS
Secrets Manager and Terraform receives only its ARN. The ECS execution role can read
only the registry and runtime secrets; the application task role has no AWS policy.

Create the ECS service at zero tasks. The deployment script first writes complete
application connection-string secrets, verifies the broker state, runs the migration
bundle from the selected immutable GHCR image in a one-shot Fargate task, checks exit
code zero, and only then updates the service to one task running that same image.
The script requires liveness/readiness 200 responses and proves one unique HTTP-202
ingestion event exists in PostgreSQL using a temporary in-VPC verifier task.

## Consequences

- A public task IP is a low-cost egress mechanism, not public API exposure. The
  ALB is the only permanent public application entry point.
- Amazon MQ is the dominant fixed staging cost. The 2026-08-26 public-price recheck
  estimates about USD 210/month in Frankfurt before variable LCU, Valkey, log, network,
  and other usage charges. A real plan and explicit owner approval are required before
  any paid resource creation.
- ElastiCache Serverless Valkey requires TLS. The existing StackExchange.Redis
  connection contract supports the resulting `ssl=true` connection string without
  application changes.
- Amazon MQ's first provisioned RabbitMQ user is administrative. Creating a more
  restricted application user requires a separate private-management operation and is
  intentionally deferred for this disposable first staging environment.
- RDS transport encryption initially uses `Ssl Mode=Require` with server certificate
  trust enabled. Validated server-certificate handling is deferred production
  hardening.
- Terraform intentionally ignores later API service task-definition and desired-count
  changes. The script owns those two values so Terraform cannot bypass the migration
  gate. This makes the staging release sequence explicit but is not automatic CI/CD.

## Intentionally deferred

- A real AWS plan, exact resolved RDS patch, resource creation, and deployment proof.
- NAT/private application subnets, HTTPS/custom domain, remote Terraform state,
  production multi-AZ/HA, restricted post-provisioning RabbitMQ user management,
  verified RDS server certificates, and operator access through ECS Exec or SSM.
- Automatic GitHub Actions to AWS deployment and the later Azure portability proof.
