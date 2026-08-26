# ADR 0020: Use AWS for the first real PulseFlow cloud deployment

**Date:** 2026-08-26

## Status

Accepted

## Context

The historically accepted Render disposable-staging topology was designed and
represented in Terraform, but no account bootstrap or resources were created. A later
cost/platform review rejected Render as the actual deployment target: the full required
topology does not fit the intended free/cheap boundary.

The application remains one `PulseFlow.Api` process containing an HTTP API and a
RabbitMQ consumer. The existing immutable GHCR image also contains an EF Core migration
bundle. The first real cloud must run the current PostgreSQL, Redis, and RabbitMQ
architecture without extracting a worker or replacing RabbitMQ.

The cloud portability investigation compared AWS and Azure. Its evidence and resource
mapping are recorded in [Cloud Portability Audit](../architecture/cloud-portability-audit.md).

## Alternatives considered

1. AWS: ALB, ECS Fargate, RDS for PostgreSQL, ElastiCache Serverless for Valkey, and
   Amazon MQ for RabbitMQ.
2. Azure: Azure Container Apps, PostgreSQL Flexible Server, Azure Managed Redis, and
   self-hosted or external RabbitMQ.
3. Continue the Render implementation.

## Decision

Use AWS for the first real PulseFlow cloud deployment. Keep the immutable GHCR image;
ECR is not required for the first deployment. Use a controlled one-shot ECS Fargate
task based on the same selected image to execute the migration bundle before updating
the ECS API service.

The first AWS staging network will use a public ALB and keep PostgreSQL, Valkey, and
RabbitMQ private. To avoid prematurely adding a material always-on NAT Gateway cost,
the initial disposable staging proposal permits Fargate tasks in public subnets with
public IPs for GHCR image pulls, while the task security group accepts HTTP only from
the ALB. This does not make the API directly reachable from the Internet. A future
private-task/NAT or image-distribution design requires separate evidence and review.

Azure remains the planned second-cloud portability proof. It is not rejected as a
cloud, but it is not the first implementation because it has no first-party managed
RabbitMQ equivalent. Self-hosting RabbitMQ in Container Apps would add broker storage,
upgrade, TLS, management-access, and recovery operations unrelated to the first
cloud-deployment objective.

## Consequences

- The first cloud implementation will use AWS-specific Terraform resources in a future
  `infra/aws/` directory. It must not modify or delete `infra/render/`.
- The same OCI artifact, health endpoints, connection-string contract, migration
  bundle, and deployment lifecycle concepts remain portable.
- The first environment's cost is materially dominated by Amazon MQ. Its use is
  justified by architecture fidelity and managed-broker learning value, not by a claim
  that AWS is the cheapest full-time host.
- The current API/consumer coupling remains unchanged: additional ECS API tasks add
  RabbitMQ consumers.
- The Render decision in ADR 0018 is superseded for actual deployment. It remains an
  accurate historical record of the unapplied experiment.
- A later Azure implementation must separately accept a RabbitMQ hosting choice before
  it creates resources.

## Intentionally deferred

- AWS Terraform implementation, resource creation, and deployment.
- AWS region, exact task size, RDS class, broker size, secret/state backend design,
  authenticated GHCR credential bootstrap, and final cost-calculator estimate.
- Automated deployment workflow, cloud credentials, and CI/CD changes.
- Any RabbitMQ high-availability, backup, prefetch, or worker-extraction decision.
- Azure Terraform implementation and the Azure RabbitMQ hosting decision.
