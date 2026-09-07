# AWS EC2 Environment

**Status:** Accepted provisioning and operations boundary. A disposable EC2 lifecycle
was proven end to end and then fully destroyed.

The managed AWS proof remains separate historical evidence preserved by Git history,
ADRs, pull requests, and checkpoints; it is not live infrastructure code. This
document describes the replacement for the retired coupled EC2 implementation; its
decision is recorded in
[ADR 0025](../decisions/0025-separate-ec2-provisioning-from-runtime-operations.md).

## Boundary

```text
Terraform
  -> VPC + public subnet + security groups + minimal SSM IAM
  -> four clean Amazon Linux 2023 EC2 hosts
  -> explicit SSM / Linux operations
  -> provider-independent Docker runtime definitions
  -> explicit runtime deployment (proven manually; never Terraform automation)
```

[`infra/aws/ec2/`](../../infra/aws/ec2/) owns only
the first two lines. It has no user data and does not install Docker, configure
runtime services, manage credentials, pull images, run migrations, test readiness,
schedule instance lifecycle, or orchestrate deployment.

[`infra/aws/ec2/ops/`](../../infra/aws/ec2/ops/) contains the small, explicit
operations foundation. It discovers exactly one running host per role from its EC2
`Name` tag in `eu-central-1`, uses SSM Run Command to install Docker Engine and the
Docker Compose CLI plugin, transport the matching portable runtime definition, and
inspect SSM, Docker, and runtime-directory state. It does not deliver real runtime
secrets or start the runtime.
Terraform never invokes it.

## Provisioned infrastructure

The disposable proof created one dedicated VPC, one public subnet in one availability
zone, an Internet gateway and public route, four security groups, one EC2 role/profile
with `AmazonSSMManagedInstanceCore`, and these four Amazon Linux 2023 x86_64 hosts:

| Name tag | Default type | Root disk |
| --- | --- | --- |
| `pulseflow-app` | `t3.small` | 8-GiB encrypted gp3 |
| `pulseflow-rabbitmq` | `t3.small` | 8-GiB encrypted gp3 |
| `pulseflow-redis` | `t3.small` | 8-GiB encrypted gp3 |
| `pulseflow-postgres` | `t3.small` | 8-GiB encrypted gp3 |

All T3 hosts use Standard CPU credit mode. This keeps the deliberately low-cost
environment from incurring surplus CPU-credit charges under sustained load.

There is no load-generator EC2 host, data disk, NAT gateway, load balancer, Elastic
IP, public SSH ingress, Scheduler, Secrets Manager secret, or Terraform runtime
bootstrap. No k6 or other performance benchmark ran in this proof; future traffic
originates externally.

The Amazon Linux AMI ID is an explicit required Terraform input. Terraform does not
look up an AMI through Systems Manager Parameter Store and does not need
`ssm:GetParameter` permission.

Instances can receive auto-assigned public IPv4 addresses for outbound access, while
service communication remains private within the VPC. The only prepared inbound
application/service paths are:

- `allowed_http_source_cidr` → app TCP 80; the default `0.0.0.0/0` exposes the app HTTP port publicly, while a narrower CIDR is optional;
- app security group → RabbitMQ TCP 5672;
- app security group → Redis TCP 6379; and
- app security group → PostgreSQL TCP 5432.

SSH has no public ingress. RabbitMQ, Redis, and PostgreSQL accept service traffic only
from the app security group through the private VPC.

## Proven runtime deployment

The successful proof used this runtime layout:

```text
app EC2:       HAProxy, PulseFlow.Api #1, PulseFlow.Api #2
rabbitmq EC2:  RabbitMQ
redis EC2:     Redis
postgres EC2:  PostgreSQL
```

Provider-independent Compose definitions for these four hosts now live in
[`infra/runtime/`](../../infra/runtime/). They receive dependency addresses and
credentials from host environment variables and do not perform provider-specific host
operations. The AWS operations foundation installed Docker Engine and Docker Compose,
copied these files to the matching hosts, and supported the explicit manual procedure.

All four hosts became reachable through SSM. PostgreSQL, RabbitMQ, and Redis
containers started successfully. The application host ran HAProxy, two PulseFlow API
replicas, and the one-shot EF migration bundle. The bundle required an explicit
`--connection` argument; with that correction, migrations completed successfully.
Internal HAProxy readiness returned HTTP 200 with `Healthy`, and the EC2 public app
endpoint also returned `Healthy`. An external NDJSON `POST /api/events` returned HTTP
202 Accepted; the exact event was then found in PostgreSQL and its smoke-test row was
removed.

This proves the manual EC2 runtime deployment and ingestion path once. It does not
establish a secret-delivery mechanism, Terraform deployment automation, observability,
or performance results.
