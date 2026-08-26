# Checkpoint: Define AWS staging Terraform and migration-first deployment gate

**Date:** 2026-08-26

## Starting point

AWS was accepted as the first real cloud deployment in ADR 0020 after the portability
audit. The repository contained a complete but unapplied historical Render Terraform
definition. There was no AWS Terraform, selected AWS resource configuration, AWS CLI
installation/profile, GHCR credential secret, real Terraform plan, state, account, or
cloud resource.

## What changed

- Added the flat AWS Terraform definition in `infra/aws/`, using the official
  HashiCorp AWS and Random providers; it covers VPC/subnets/routing, security groups,
  ALB, ECS/Fargate, IAM, CloudWatch Logs, RDS PostgreSQL, ElastiCache Serverless
  Valkey, Amazon MQ RabbitMQ, Secrets Manager references, outputs, and an operator
  runbook.
- Selected Frankfurt (`eu-central-1`) for the first reviewed plan; selected 0.25
  vCPU/512 MiB Fargate tasks, Single-AZ `db.t4g.micro` RDS with 20 GiB gp3,
  Serverless Valkey 8, and RabbitMQ 4.2 `mq.m7g.medium` single-instance Amazon MQ.
- Kept the intentionally no-NAT staging network. Fargate gets a public IP only for
  outbound image/API access; its application port accepts only ALB security-group
  traffic. Dependencies remain private.
- Added `scripts/deploy-aws-staging.ps1`. It writes runtime connection-string secrets
  without printing values, checks the broker state, runs the same selected GHCR image
  as a one-shot migration task, requires exit code zero before enabling one API task,
  requires liveness/readiness HTTP 200, sends one unique HTTP-202 NDJSON event, and
  verifies the exact row through a temporary in-VPC PostgreSQL task.
- Added a full cost, bootstrap, recreate, and destruction runbook; created ADR 0021
  and architecture documentation; updated the Stage 5 roadmap state without changing
  the historical Render record.

## Resulting repository state

The repository has a validated AWS staging configuration but **no deployment**. The
configuration is ready for account bootstrap, an account-specific real plan, a manual
cost review, and explicit owner approval. It does not introduce ECR, a NAT Gateway,
RabbitMQ clustering, a second PulseFlow image, API startup migrations, multiple API
replicas, a worker extraction, Azure resources, automated GitHub-to-AWS deployment,
or a load test.

`infra/render/` remains unchanged and unapplied. Terraform's local state will be
sensitive because the Terraform-generated Amazon MQ user password is stored there;
the RDS-managed password and external GHCR token are not put in state.

## Verification

- Downloaded the official Terraform 1.15.9 binary only to a temporary local tool
  directory because Terraform was not installed on PATH.
- `terraform -chdir=infra/aws fmt -check -recursive` passed.
- `terraform -chdir=infra/aws init -backend=false` passed and selected AWS provider
  6.61.0 and Random provider 3.9.0. The committed lock file records both.
- `terraform -chdir=infra/aws validate` passed.
- PowerShell parser validation for `scripts/deploy-aws-staging.ps1` passed.
- `git diff --check` passed.
- A real `terraform plan` was not possible: Terraform and AWS CLI were absent from
  PATH, no AWS identity/profile or GHCR secret ARN was configured, and no secret value
  was requested or supplied.

## Decisions made

- [ADR 0021](../decisions/0021-define-first-aws-staging-resource-configuration.md)
  accepts the first-plan AWS region, resource configuration, local-state boundary,
  and explicit migration-first manual deployment mechanism.
- Existing AWS selection and staging no-NAT boundary remain governed by
  [ADR 0020](../decisions/0020-use-aws-for-first-cloud-deployment.md).

## Intentionally unresolved

- AWS account/profile bootstrap, selected immutable GHCR image SHA, and bootstrap
  GHCR secret ARN.
- Account-specific RDS PostgreSQL 17 patch, full real plan review, current final cost
  calculation from that plan, and explicit paid-resource approval.
- Actual creation, migration result, API health responses, Valkey/broker evidence,
  end-to-end ingestion evidence, exact smoke-test-row cleanup, and actual bill.
- Remote state, production egress/networking, HTTPS/domain, RDS certificate
  verification, RabbitMQ least-privilege post-provisioning user, automatic deployment,
  and Azure portability proof.

## Next recommended step

Install AWS CLI v2, configure and verify a dedicated AWS staging profile, bootstrap a
least-privilege GHCR package-read secret without exposing its token, publish/select a
full-SHA image, and run a real Terraform plan. Inspect every planned resource and
update the required Frankfurt cost checkpoint before requesting one explicit approval
to run `terraform apply`. Do not apply before that approval.
