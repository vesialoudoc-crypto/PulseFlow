# AWS EC2 Environment

**Status:** Accepted provisioning and operations boundary. No new AWS infrastructure
has been created by this change.

The managed AWS proof in [`infra/aws/managed-legacy/`](../../infra/aws/managed-legacy/) remains separate historical
evidence and is not changed here. This document describes the replacement for the
retired coupled EC2 implementation; its decision is recorded in
[ADR 0025](../decisions/0025-separate-ec2-provisioning-from-runtime-operations.md).

## Boundary

```text
Terraform
  -> VPC + public subnet + security groups + minimal SSM IAM
  -> four clean Amazon Linux 2023 EC2 hosts
  -> explicit SSM / Linux operations
  -> provider-independent Docker runtime definitions
  -> host execution and runtime deployment (deferred)
```

[`infra/aws/ec2/`](../../infra/aws/ec2/) owns only
the first two lines. It has no user data and does not install Docker, configure
runtime services, manage credentials, pull images, run migrations, test readiness,
schedule instance lifecycle, or orchestrate deployment.

[`infra/aws/ec2/ops/`](../../infra/aws/ec2/ops/)
contains the small, explicit operations foundation. Terraform never invokes it.

## Provisioned infrastructure

Terraform would create one dedicated VPC, one public subnet in one availability zone,
an Internet gateway and public route, four security groups, one EC2 role/profile with
`AmazonSSMManagedInstanceCore`, and these clean Amazon Linux 2023 x86_64 hosts:

| Name tag | Default type | Root disk |
| --- | --- | --- |
| `pulseflow-app` | `t3.small` | 8-GiB encrypted gp3 |
| `pulseflow-rabbitmq` | `t3.small` | 8-GiB encrypted gp3 |
| `pulseflow-redis` | `t3.small` | 8-GiB encrypted gp3 |
| `pulseflow-postgres` | `t3.small` | 8-GiB encrypted gp3 |

All T3 hosts use Standard CPU credit mode. This keeps the deliberately low-cost
environment from incurring surplus CPU-credit charges under sustained load.

There is no load-generator EC2 host, data disk, NAT gateway, load balancer, Elastic
IP, public SSH ingress, Scheduler, Secrets Manager secret, or runtime bootstrap.
k6 traffic is a later external operation.

The Amazon Linux AMI ID is an explicit required Terraform input. Terraform does not
look up an AMI through Systems Manager Parameter Store and does not need
`ssm:GetParameter` permission.

Instances can receive auto-assigned public IPv4 addresses for outbound access, while
service communication remains private within the VPC. The only prepared inbound
application/service paths are:

- the required, non-global `allowed_http_source_cidr` → app TCP 80;
- app security group → RabbitMQ TCP 5672;
- app security group → Redis TCP 6379; and
- app security group → PostgreSQL TCP 5432.

RabbitMQ, Redis, and PostgreSQL have no public ingress. The `allowed_http_source_cidr`
input makes the future test origin explicit and rejects `0.0.0.0/0`.

## Deferred runtime

The intended runtime layout is not yet configured:

```text
app EC2:       HAProxy, PulseFlow.Api #1, PulseFlow.Api #2
rabbitmq EC2:  RabbitMQ
redis EC2:     Redis
postgres EC2:  PostgreSQL
```

Provider-independent Compose definitions for these four hosts now live in
[`infra/runtime/`](../../infra/runtime/). They receive dependency addresses and
credentials from host environment variables and do not perform provider-specific host
operations. Linux package installation, Docker configuration on a real host, image
retrieval, service execution, migrations, and smoke checks remain explicit future
operations work.
