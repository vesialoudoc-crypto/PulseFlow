# ADR 0025: Separate EC2 provisioning from runtime operations

**Date:** 2026-08-27

## Status

Accepted. This supersedes the coupled EC2 implementation decisions in ADRs 0022,
0023, and 0024. Those ADRs remain historical records of the retired topology and its
cost iterations.

## Context

The former EC2 performance root combined infrastructure provisioning with Docker
bootstrap, generated runtime credentials, image authentication and pulls, Compose
deployment, migrations, smoke testing, k6 execution, and scheduled EC2 lifecycle.
The old environment was manually removed from AWS and its local Terraform state was
discarded. No new EC2 environment has been applied.

That coupling made an operating-system or runtime failure part of Terraform's
lifecycle, obscured the narrow infrastructure result, and added automation that did
not help the short-lived learning and deployment-proof objective.

## Alternatives considered

1. Retain the coupled Terraform lifecycle. It provides one command path but continues
   to combine infrastructure, credentials, host configuration, and application
   behavior in a fragile stateful workflow.
2. Use a managed AWS topology only. The managed proof remains useful historical
   evidence, but it does not provide the inexpensive four-host EC2 learning boundary.
3. Provision clean EC2 hosts with Terraform, then perform Linux and runtime work
   explicitly through SSM. This keeps each layer independently understandable and
   operable.

## Decision

Replace `infra/aws-ec2-performance/` with a minimal
`infra/aws/ec2/` Terraform root. Terraform provisions one VPC, one
public subnet and egress route, security groups, a minimal SSM IAM role/profile, and
four Amazon Linux 2023 x86_64 EC2 hosts: app, RabbitMQ, Redis, and PostgreSQL.

Terraform has no runtime/bootstrap responsibility. It does not use `user_data` or
install Docker; configure service software; create secrets or credentials; retrieve
images; perform migrations, readiness checks, or traffic tests; schedule instances;
or start, stop, or deploy hosts.

Add `infra/aws/ec2/ops/` as the separate explicit operations layer.
It begins with an SSM shell connector and a standalone Docker installation script.
Runtime deployment remains a future decision and implementation.

## Consequences

- Terraform success means four clean, SSM-accessible hosts and required networking,
  not a running PulseFlow system.
- Linux and runtime failures can be diagnosed and corrected without complicating
  Terraform state or resource lifecycle.
- The Docker/service configuration remains readable and directly operable by a human
  through SSM.
- The four small `t3.small` hosts with 8-GiB encrypted gp3 roots favor learning, low
  cost, and short-lived deployment proof over performance isolation or production
  automation; their Standard CPU credit mode avoids surplus CPU-credit charges under
  sustained load.
- A future deployment procedure must make its runtime credentials, service topology,
  migration sequence, validation, and shutdown behavior explicit.

## Intentionally deferred

- Terraform apply and any AWS infrastructure creation.
- Docker installation on a real host and all runtime deployment work.
- HAProxy, application replicas, RabbitMQ, Redis, PostgreSQL, image registry access,
  migrations, smoke testing, and external k6 traffic.
- Production-grade network, storage, HA, monitoring, backup, and lifecycle design.
