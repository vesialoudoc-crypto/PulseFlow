# Checkpoint: Audit AWS and Azure cloud portability

**Date:** 2026-08-26

## Starting point

Local Compose worked, GitHub Actions could publish an immutable GHCR API image that
also contained the EF Core migration bundle, and `infra/render/` contained a complete
but unapplied Render staging definition. Render was historically accepted as the first
disposable staging platform.

## What changed

- Audited every significant Render Terraform concept and distinguished portable
  deployment contracts from Render provider implementation details.
- Investigated AWS and Azure against current official cloud and HashiCorp provider
  documentation without creating any resources or Terraform files.
- Added the focused [Cloud Portability Audit](../architecture/cloud-portability-audit.md),
  including topology diagrams, runtime/dependency evaluation, Terraform mappings,
  migration lifecycles, networking trade-offs, cost drivers, and source links.
- Recorded that the complete Render topology cannot meet the intended free/cheap
  boundary and that no Render account bootstrap, state, plan, apply, or resources were
  created.
- Accepted AWS as the first real deployment target in
  [ADR 0020](../decisions/0020-use-aws-for-first-cloud-deployment.md). It maps directly
  to ALB, ECS Fargate, RDS PostgreSQL, ElastiCache Serverless for Valkey, and Amazon
  MQ for RabbitMQ without application code changes.
- Marked ADR 0018 superseded while retaining it unchanged in substance as the
  historical record of the unapplied Render experiment. Kept `infra/render/` intact.
- Updated the Stage 5 roadmap state to describe the resulting accepted AWS direction
  and Azure's later portability role.

## Resulting repository state

The repository now has an evidence-backed cloud portability decision, but it still has
no AWS or Azure resources, Terraform directories, credentials, cloud deployment, or
application-code changes. GHCR, the immutable SHA strategy, the migration bundle,
health endpoints, runtime configuration contract, data clients, and API/consumer
boundary remain portable.

AWS will keep GHCR using a Secrets Manager-backed private registry credential; ECR is
not required. An explicit same-image ECS Fargate migration task will gate service
rollout. The cheap first AWS staging proposal keeps ALB public and PostgreSQL, Valkey,
and Amazon MQ private; it avoids a NAT Gateway by giving API tasks public IPs while
their security group allows inbound HTTP only from the ALB.

Azure Container Apps, Flexible Server, Managed Redis, and a Container Apps Job can
run the application without code changes. Azure is deferred as the second-cloud proof
because it has no first-party managed RabbitMQ equivalent; self-hosting RabbitMQ adds
broker operations beyond the first deployment objective.

## Verification

- `dotnet csharpier check .` passed: 58 files checked.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.
- Cloud research used current official AWS, Azure, HashiCorp AWS provider, and
  HashiCorp AzureRM provider documentation, linked from the audit.
- No Terraform plan/apply, deployment, resource creation, or application test suite
  was run because this milestone changes documentation only.

## Decisions made

- AWS is the first real PulseFlow cloud deployment target. See
  [ADR 0020](../decisions/0020-use-aws-for-first-cloud-deployment.md).
- Render's historical staging decision is superseded for actual deployment because
  its complete topology fails the intended free/cheap cost boundary. The source and
  Terraform remain retained history; nothing was deleted.
- The first AWS migration mechanism is an explicit same-image ECS Fargate task, not
  per-replica startup migration.
- The first AWS cost boundary does not introduce a NAT Gateway solely for GHCR pulls.
  This is a deliberately documented disposable-staging compromise, not a final
  production-networking decision.

## Intentionally unresolved

- Exact AWS region, RDS/Valkey/broker SKUs, task sizing, availability settings,
  Terraform provider version, state backend, secret policy, GHCR credential bootstrap,
  cost-calculator estimate, and teardown runbook.
- AWS Terraform implementation, cloud credentials, plan/apply, deployment automation,
  and deployed load measurement.
- Azure Terraform and its explicit RabbitMQ hosting decision.
- RabbitMQ HA/backup, consumer scaling, prefetch, and worker extraction.

## Next recommended step

Create the smallest AWS Terraform design PR: document protected state and input
contracts, then implement the selected ALB/ECS/RDS/Valkey/Amazon MQ topology, secrets,
security groups, and controlled migration-task release gate. Run static Terraform and
project verification before any authenticated plan. Do not modify `infra/render/` in
that PR.
