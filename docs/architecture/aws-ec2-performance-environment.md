# AWS EC2 Performance Environment

**Status:** Accepted Terraform configuration; not yet applied from this repository
state.

## Purpose

The first AWS lifecycle proof in [`infra/aws/`](../../infra/aws/) used ECS Fargate,
RDS PostgreSQL, ElastiCache Serverless Valkey, Amazon MQ RabbitMQ, and an ALB. It was
successfully deployed, smoke-tested end to end, and destroyed. That managed topology
remains useful evidence and is not modified by this document.

The separate [`infra/aws-ec2-performance/`](../../infra/aws-ec2-performance/) root
defines a temporary, cost-sensitive performance environment. Its purpose is to
isolate application, broker, cache, database, and load-generator resource use without
leaving the Amazon MQ managed-service cost running between experiments. It is not HA
or a production topology.

ADR 0022 records the accepted choice and alternatives.

## Topology

```text
EC2 app (m7i.large, private 10.43.10.10)
├─ HAProxy :80
├─ PulseFlow.Api #1
└─ PulseFlow.Api #2
        |
        +--> EC2 rabbitmq (m7i.large, private 10.43.10.20) ─ RabbitMQ :5672
        +--> EC2 redis (t3.small, private 10.43.10.30) ─ Redis :6379
        `--> EC2 postgres (m7i.large, private 10.43.10.40) ─ PostgreSQL :5432

EC2 loadgen (m7i.large, private 10.43.10.50)
└─ k6 ──private HTTP──> HAProxy :80
```

The app node intentionally has two API containers on one EC2 host. HAProxy uses the
existing `/health/ready` backend health check and round-robin policy. The current API
process also owns a RabbitMQ consumer, so the two API replicas create two consumer
processes; this is an existing application behavior, not a new worker topology.

All workload components are containers: HAProxy, both API replicas, RabbitMQ, Redis,
PostgreSQL, and k6. The same Docker Compose layouts and environment-variable
configuration can be moved to another Linux VM provider without application changes.

## Runtime lifecycle

Terraform creates five Amazon Linux 2023 x86_64 instances. First-boot user data
installs Docker and a pinned Compose plugin, but it does not start the workload. The
operator script uses SSM Run Command to make the state transitions explicit:

```text
PLAN → review/approval → APPLY → bootstrap confirmation → stop
     → DEPLOY → dependency containers → migration bundle
     → API/HAProxy → readiness → k6 smoke → PostgreSQL exact-row proof → cleanup
     → optional k6 baseline → STOP or scheduled stop → DESTROY
```

The migration runs from `/app/migrations/pulseflow-migrations` in the same selected
immutable API image. A migration failure stops deployment before either API replica is
started. The smoke test runs k6 from the load-generator node, sends one Contract v2
event through HAProxy, waits until PostgreSQL contains its exact `(source, eventId)`,
then deletes only that row.

The remote baseline runner reuses the existing ten-second `ingestion-baseline.js`
request scenario and its `VUS` configuration. It has no implicit long-duration test or
threshold and does not itself establish a bottleneck conclusion.

## Networking and security

The single-AZ VPC subnet supplies fixed private addresses used directly in the
container connection strings. A Route 53 private zone is not needed for five fixed
addresses, avoiding another small fixed cost. Private addresses remain attached to
the primary ENI across an EC2 stop/start.

Instances use public, auto-assigned IPv4 only for outbound HTTPS to Systems Manager,
Amazon Linux repositories, Docker Hub, and GHCR. There is no SSH, bastion, Elastic IP,
or public inbound security-group rule. The public IPv4 is released when an instance is
stopped. Service traffic always uses the listed private address; dependencies are not
publicly reachable.

Security groups permit only the following inter-node traffic:

- load generator to app node TCP 80;
- app node to RabbitMQ TCP 5672;
- app node to Redis TCP 6379; and
- app node to PostgreSQL TCP 5432.

All node roles include `AmazonSSMManagedInstanceCore`. The app role can read GHCR,
PostgreSQL, and RabbitMQ credentials; each dependency role can read only its own
credential. The application itself receives its existing connection-string settings
as normal container environment variables and receives no AWS SDK permission.

## Storage and schedule

Each instance uses only its encrypted gp3 root EBS volume. PostgreSQL, RabbitMQ, and
Redis have Docker named volumes on their own node root filesystems; no extra EBS volume
or managed data service is created. The data survives stop/start but is deleted when
Terraform terminates the instance.

EventBridge Scheduler starts and stops all five nodes Monday through Friday at 08:00
and 17:00 with the `Europe/Warsaw` IANA timezone. Its timezone-aware schedule handles
DST; Terraform contains no fixed-UTC cron assumption. A stopped node stops incurring
EC2 compute and auto-assigned public IPv4 charges. Root EBS volumes and Secrets
Manager secrets still incur charges.

## Deliberate limitations

- One availability zone, one node per dependency, no cluster, no replication, no
  backups, and no HA claim.
- No NAT gateway, ALB, HTTPS/domain, EIP, public management endpoint, or separate
  disk. The controlled egress design is suitable only for this temporary environment.
- Current images are x86_64 because the existing GHCR publication workflow builds that
  image architecture. A later multi-architecture image decision could make cheaper
  Graviton nodes an option.
- SSM Run Command is the operator path. CloudWatch metrics/log aggregation, a remote
  Terraform backend, scheduled-deployment orchestration, and retained performance
  artefacts are not introduced by this slice.
