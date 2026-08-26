# Cloud Portability Audit: AWS and Azure

**Date:** 2026-08-26

## Purpose and scope

This investigation compares how the already implemented PulseFlow application
topology maps to AWS and Azure. It does not provision cloud resources, introduce
cloud Terraform, change application code, or alter the current process boundary.

The portable application boundary remains:

```text
Internet
   |
HTTP ingress / load balancer
   |
PulseFlow.Api instance(s)
   |-- PostgreSQL endpoint
   |-- Redis endpoint
   `-- RabbitMQ endpoint
```

`PulseFlow.Api` continues to host both HTTP handling and a RabbitMQ consumer. With
one consumer configured per process, two API replicas mean two HTTP processes and two
RabbitMQ consumers. This is an existing constraint, not a cloud-specific design.

The comparison supports the accepted first-cloud choice in
[ADR 0020](../decisions/0020-use-aws-for-first-cloud-deployment.md).

## Render outcome

Render was accepted historically as a disposable staging experiment, and a complete
but unapplied Terraform definition was produced in [`infra/render/`](../../infra/render/).
The later cost/platform review found that its complete topology could not be operated
within the intended free/cheap boundary. In particular, the required continuously
running API and persistent RabbitMQ service impose paid-service and disk costs, so the
free PostgreSQL and Key Value tiers do not make the whole topology inexpensive enough.

No Render account bootstrap, credentials, Terraform state, plan, apply, or resources
were created. The Render definition is retained unchanged as an historical,
reviewable experiment. It must not be extended or deleted as part of this work.

## What remains portable

The following artifacts and concepts move unchanged, or with only environment values
substituted at deployment time:

- the `PulseFlow.Api` Linux OCI image, its immutable `sha-<commit>` tag, Dockerfile,
  and GHCR publication workflow;
- the EF Core bundle at `/app/migrations/pulseflow-migrations` in the same image;
- `/health/live` and `/health/ready`, with the latter used for traffic readiness;
- external runtime configuration and secret inputs, including the existing connection
  string keys and RabbitMQ/ingestion/rate-limit settings;
- PostgreSQL access through Npgsql, Redis access through the existing client, and
  AMQP access through RabbitMQ.Client;
- the API-plus-consumer process boundary and replica semantics;
- durable PostgreSQL, disposable operational Redis state, persistent RabbitMQ broker
  state, controlled operator access, and protected Terraform-state concepts; and
- Terraform variable and output concepts, HCL organization, state protection, and
  deployment lifecycle concepts.

Neither cloud requires ECR or Azure Container Registry for PulseFlow to run. ECS
Fargate supports private non-AWS registries by reading registry credentials from AWS
Secrets Manager, and Azure Container Apps supports private registries using saved
registry credentials. Keeping GHCR is therefore the preferred first-cloud path. The
registry token must be read-only and treated as a deployment secret. A future move to
a cloud-native registry can be evaluated separately if image-pull networking or
operational evidence justifies it.

## What is replaced

Terraform's language and deployment concepts are reusable; Render provider resources
are not. The following are Render-specific and must be replaced in a cloud-specific
future directory:

| Render implementation | Replacement category |
| --- | --- |
| `render_web_service` and its plan/instance fields | Cloud runtime, ingress, health check, and desired replica resources |
| `render_postgres` | Managed PostgreSQL resource, private network rules, backup configuration |
| `render_keyvalue` | Managed Redis/Valkey resource and private endpoint/security rules |
| `render_private_service` and nested disk | Managed RabbitMQ or a separately operated RabbitMQ runtime and persistent storage |
| Render service slug/internal DNS | Cloud-provided private DNS endpoint |
| `registry_credential_id` | Secret-backed ECS registry credentials or Container Apps registry secret |
| Render plan and region names | Cloud SKU, task size, and region inputs |
| `pre_deploy_command` | Explicit one-shot migration task/job and rollout gate |
| Render operator CIDR fields | Security groups, private endpoints, NSGs, and/or controlled operator path |
| Render provider/state bootstrap | AWS/Azure provider authentication and separately protected remote state |

The existing image-reference validation, instance-count setting, health path, runtime
setting names, secret inputs, database/Redis/broker dependencies, persistent-storage
requirement, operator-CIDR concept, and state-protection requirement remain useful
input contracts. They should be recast for the selected provider rather than copied
as Render resource blocks.

## AWS evaluation

### Proposed first-cloud topology

```text
public subnets
Internet -> Application Load Balancer -> target group

private application and data subnets
target group -> ECS Fargate service -> PulseFlow.Api (one or more tasks)
                                      |-- RDS for PostgreSQL
                                      |-- ElastiCache Serverless for Valkey
                                      `-- Amazon MQ for RabbitMQ
```

The ALB is public. ECS tasks, RDS, ElastiCache, and a private Amazon MQ broker are
not public. Security groups permit ALB-to-API HTTP, API-to-PostgreSQL, API-to-Redis,
and API-to-AMQPS only. The API target group's health check is `GET /health/ready`.

This topology can run the current application without code changes. The operational
configuration changes are normal endpoint/credential substitutions. Amazon MQ uses a
TLS AMQP endpoint (`amqps` on port 5671), so the runtime `ConnectionStrings:RabbitMq`
value changes from local `amqp` to the broker endpoint and credentials; RabbitMQ.Client
already supports AMQP 0-9-1 over TLS. It is configuration work, not a broker
replacement or application redesign.

### Runtime and image

An ECS service on Fargate can run one or more tasks from the selected immutable GHCR
image. The task definition supplies the image name, CPU/memory, port 8080, environment
values, and secret references. A task execution role reads a read-only GHCR credential
secret and runtime secrets from AWS Secrets Manager. The service is registered with an
ALB target group and can set `desired_count` to one initially or two for a deliberate
replica experiment. ECR is not required.

ECS Fargate tasks use `awsvpc` networking. A task receives its own network interface,
so security-group rules can be scoped directly to the API process. The current API
consumer coupling remains visible: raising service `desired_count` also raises the
number of broker consumers.

### PostgreSQL and Redis

RDS for PostgreSQL is compatible with the normal Npgsql connection model. A small
Single-AZ RDS PostgreSQL instance in private DB subnets, with encrypted storage,
automated backups, a database security group, and credentials supplied through Secrets
Manager is the minimum sensible staging direction. `db.t4g.micro` is an economical
starting candidate, subject to region/version availability and the image's PostgreSQL
compatibility check. It is deliberately not a production high-availability claim.

ElastiCache Serverless for Valkey is the fitting managed operational-state option. It
is reachable through private VPC endpoints and security groups, and Valkey's Redis
protocol is compatible with the existing Redis client for this rate-limiter use case.
Use low usage limits and no unnecessary minimum pre-scaling. The rate-limit keys are
still disposable operational state, although snapshots/persistence may be configured
only if later evidence requires it.

### RabbitMQ

Amazon MQ for RabbitMQ is a direct managed-broker fit: it supports RabbitMQ, private
brokers in a VPC with security groups, TLS AMQP endpoints, broker users, persistent
storage, and a single-instance deployment mode. Start with the smallest currently
supported single-instance type, expected to be `mq.m7g.medium` after confirming
regional availability. `mq.t3.micro` is deprecated and unavailable for new broker
creation, so it is not a staging option. A single instance is an explicit low-cost,
non-HA staging compromise; a RabbitMQ cluster is a later availability decision.

Amazon MQ is the largest fixed cost in this otherwise small topology. Terraform can
manage the broker, but its RabbitMQ user handling has a state caveat: the provider
cannot read RabbitMQ users back for drift detection and broker-user changes can force
recreation. State access must therefore be tightly controlled and the eventual code
must avoid treating ordinary user rotation as an innocuous update.

### Migration lifecycle

Use the exact selected ECS task definition/image revision for a controlled one-shot
Fargate task. Override its command to run:

```text
/app/migrations/pulseflow-migrations --connection <runtime PostgreSQL connection>
```

The task receives the same database secret and private network access as the service.
The deployment operation waits for migration success, then updates the ECS service to
the selected image. It must not update the service after a failed migration, and API
tasks never run migrations at startup. ECS supports per-run command overrides, so no
second migration image is needed.

### Networking and NAT decision

Private Fargate tasks need outbound reachability to pull an image from GHCR and to
retrieve external secret/control-plane endpoints. AWS documents three relevant paths:

1. private tasks plus NAT Gateway: clean private-task networking but adds an
   approximately USD 33/month fixed NAT cost in `us-east-1`, plus data processing;
2. ECR plus VPC endpoints: can avoid NAT for image pulls, but would add an ECR copy
   and does not solve a GHCR pull, so it conflicts with the preferred first-cloud
   artifact path; or
3. Fargate tasks in public subnets with public IPs, while security groups accept API
   traffic only from the ALB: cheapest disposable staging path and permits GHCR pulls,
   but less isolated than a fully private task tier.

For the first cheap, disposable AWS deployment, use option 3 only after recording its
security boundary explicitly: the task security group must not allow any direct
internet inbound traffic, and the ALB is the sole HTTP ingress. RDS, Valkey, and
RabbitMQ remain private. A NAT Gateway is therefore not necessary for that first
staging shape. A later production-like hardening step can move tasks to private
subnets with NAT or a reviewed image-distribution arrangement; that is not silently
included in the first implementation.

## Azure evaluation

### Comparable topology

```text
Internet -> Azure Container Apps external HTTPS ingress -> PulseFlow.Api replicas
                                                        |-- PostgreSQL Flexible Server
                                                        |-- Azure Managed Redis
                                                        `-- self-hosted or external RabbitMQ
```

Azure Container Apps can run the immutable GHCR image, expose external HTTPS ingress,
set replica bounds, define explicit HTTP readiness probes, and inject runtime secrets.
It supports private registry credentials, so GHCR can remain the image source. The
API's readiness probe should call `/health/ready`; `/health/live` remains liveness.

Container Apps is a strong fit for the HTTP API itself. A Container Apps Job can run
the same image and bundle as an explicit migration execution, then a successful job
can be used as the release gate before updating the API revision. The task/job must
receive the same database secret. An explicit job is preferable to an API startup
migration or a best-effort init-container convention.

### PostgreSQL and Redis

Azure Database for PostgreSQL Flexible Server accepts ordinary PostgreSQL/Npgsql
connections. For a private deployment it can use VNet integration with a delegated
subnet and a linked Private DNS zone, or public access plus Private Link. The first
private-staging design should prefer VNet integration because it keeps database traffic
inside the VNet. It requires the explicit delegated subnet and private DNS setup.
Flexible Server supports backups; the B1ms burstable SKU is a cost-oriented starting
point, not a capacity conclusion.

Azure Managed Redis is the current recommended managed Redis offering. Azure Cache for
Redis is retiring, and new-customer creation of its legacy SKUs is blocked. Azure
Managed Redis supports the Redis protocol, private endpoints, TLS, and the existing
StackExchange.Redis client model. The non-HA B0 SKU is a possible staging cost floor;
it intentionally accepts cache/rate-limit state loss during maintenance or failure.

### RabbitMQ is the portability gap

Azure does not provide an Azure first-party managed RabbitMQ equivalent comparable to
Amazon MQ for RabbitMQ. Azure Service Bus is not RabbitMQ and is not a substitute for
this audit: adopting it would change the client, broker semantics, and architecture.

Two realistic Azure options preserve the application contract:

1. run RabbitMQ as a separately operated, single-replica Container App with a
   persistent Azure Files mount; or
2. use an external managed RabbitMQ provider reachable through a reviewed private or
   TLS-protected network path.

Option 1 is technically possible but is not a clean first deployment choice. Container
Apps supports internal TCP ingress, but TCP ingress requires a VNet-enabled environment.
RabbitMQ needs both stable persistent storage and careful management access. A mounted
Azure Files share provides file storage rather than a broker-specific managed disk;
it also requires a storage-account key for Container Apps storage mounting. The
operator must additionally design single-replica behavior, AMQP port exposure,
management UI access, TLS, backup/recovery, upgrade, and restart behavior. It is a
valid later portability exercise, but it adds broker operations to a cloud comparison
whose purpose is to deploy the current application, not to learn RabbitMQ hosting
first.

### Azure networking

The smallest acceptable Azure deployment can use external Container Apps HTTPS ingress
for the API and VNet integration for private dependencies. A VNet is required for
internal TCP RabbitMQ ingress and for a conventional private database path. PostgreSQL
VNet integration requires a delegated subnet and private DNS. Azure Managed Redis
uses a private endpoint and private DNS for network isolation. Key Vault references
can hold runtime secrets, with a managed identity granted `Key Vault Secrets User`.

This is more networking and DNS surface than the AWS public-ALB/private-dependency
staging shape. It is defensible for the later Azure proof, but the need to self-host
RabbitMQ means that it does not improve the first deployment outcome.

## Terraform resource comparison

These are the likely resource families, verified against the current HashiCorp AWS and
AzureRM provider documentation. They are a mapping only, not an instruction to create
Terraform now. Exact arguments and provider versions must be validated in the future
implementation PR.

| Concept | AWS | Azure |
| --- | --- | --- |
| API runtime | `aws_ecs_cluster`, `aws_ecs_task_definition`, `aws_ecs_service` | `azurerm_container_app_environment`, `azurerm_container_app` |
| HTTP ingress | `aws_lb`, `aws_lb_target_group`, `aws_lb_listener` | Container Apps `ingress` in `azurerm_container_app` |
| PostgreSQL | `aws_db_subnet_group`, `aws_db_instance` | `azurerm_postgresql_flexible_server`, `azurerm_postgresql_flexible_server_database` |
| Redis | `aws_elasticache_serverless_cache` | `azurerm_managed_redis`, `azurerm_private_endpoint` |
| RabbitMQ | `aws_mq_broker` | no first-party managed RabbitMQ; `azurerm_container_app` plus Azure Files resources for self-hosting, or external-provider resources |
| Migration runner | ECS `RunTask` deployment action using the same task definition; no persistent Terraform resource required | `azurerm_container_app_job` using the same image |
| Private network | `aws_vpc`, `aws_subnet`, `aws_route_table`, `aws_security_group` | `azurerm_virtual_network`, `azurerm_subnet`, `azurerm_private_dns_zone`, `azurerm_private_dns_zone_virtual_network_link`, `azurerm_private_endpoint` |
| Secrets | `aws_secretsmanager_secret`, `aws_secretsmanager_secret_version`, task IAM roles | `azurerm_key_vault`, `azurerm_key_vault_secret`, `azurerm_user_assigned_identity`, `azurerm_role_assignment` |
| Persistent storage | RDS storage/backups and Amazon MQ managed broker storage | PostgreSQL backups; Azure Files and `azurerm_storage_share` only if RabbitMQ is self-hosted |
| Health check | ALB target-group health check to `/health/ready` | Container App HTTP readiness probe to `/health/ready` |
| State protection | separate protected remote backend, access policy, locking plan to be selected | separate protected remote backend, access policy, locking plan to be selected |

Terraform HCL structure, modules, variable names, and lifecycle sequencing are
portable ideas. Provider resource declarations, endpoint construction, identity,
networking, and state-backend details are cloud-specific.

## Cost and lifecycle comparison

Prices vary by region, tax, storage, traffic, and selected availability mode. The
ranges below are deliberately planning ranges for one always-on disposable staging
environment, not quotations. Before any implementation, use the relevant cloud
pricing calculator for the chosen region and date.

| Cost driver | AWS first shape | Azure comparable shape |
| --- | --- | --- |
| API compute | Two small always-on Fargate tasks: roughly USD 35-45/month in `us-east-1`; one task roughly halves this | Two small always-on Container Apps replicas; consumption billing can be low, but API cannot scale to zero while it must consume RabbitMQ |
| ingress | ALB: about USD 16/month before LCU/data use | Container Apps external ingress is built in; requests/compute remain usage billed |
| PostgreSQL | RDS compute, gp storage, and backup storage: roughly USD 15-30/month without free-tier assumptions | Flexible Server B1ms is listed at USD 12.41/month, plus storage/backup |
| Redis | Serverless Valkey: at least storage/request metering; use a small range of roughly USD 5-15/month for this light rate-limit workload | Managed Redis B0 non-HA is listed at USD 13.14/month |
| RabbitMQ | Amazon MQ single instance is the dominant fixed floor: expect roughly USD 120-160/month plus broker storage, region dependent | Self-hosted RabbitMQ compute, Azure Files storage/transactions, and operational effort; roughly USD 10-30/month is plausible but is not managed-broker equivalence |
| networking | No NAT in the selected cheap staging shape; a NAT Gateway would add about USD 33/month plus processing in `us-east-1` | VNet itself has no comparable NAT baseline here; private endpoints and DNS add billed resources where used |
| secrets/logs/egress | Secrets Manager, CloudWatch, data transfer, and public IPv4 use can add small but real amounts | Key Vault operations, Log Analytics ingestion/retention, private endpoints, storage, and egress can add small but real amounts |

The likely AWS always-on baseline is approximately USD 190-260/month in a low-cost US
region before variable traffic and tax, dominated by Amazon MQ. Adding one NAT Gateway
raises it by roughly USD 33/month before data processing. The comparable Azure shape
is approximately USD 45-100/month before variable traffic and tax if it self-hosts
RabbitMQ, but this lower cash range excludes the operational equivalence supplied by
Amazon MQ and therefore is not an apples-to-apples managed-broker result.

Cost-control rules for both clouds:

- create the whole environment only for deployment/measurement windows and destroy it
  after PostgreSQL data that matters has been exported;
- do not promise that persistent data survives intentional environment destruction;
- retain protected state only while the environment exists, then follow the chosen
  teardown/runbook process;
- let a migration job/task exist only for its execution; and
- do not count API scale-to-zero as a full-system saving while the current co-hosted
  consumer must remain running.

Azure Flexible Server can be stopped for periods of inactivity, and Container Apps can
scale API replicas to zero, but neither creates a complete working pipeline when the
RabbitMQ consumer is absent. Amazon MQ and the AWS ALB have always-on floors, making
complete environment destruction the more meaningful AWS cost control.

## Recommendation

**Use AWS first.**

AWS wins despite its substantially higher Amazon MQ cost because it is the least
application-changing, most faithful first deployment of the accepted architecture:

- Amazon MQ is a managed RabbitMQ service compatible with the existing AMQP client and
  broker boundary;
- ECS Fargate, ALB, RDS, and ElastiCache map directly to the existing service roles;
- the same immutable GHCR image and migration bundle can be used for both migration
  task and API service;
- Terraform support is mature and covers the required resource families;
- the selected public-ALB/private-dependency staging network avoids premature NAT
  cost while keeping datastores and the broker private; and
- it delivers stronger cloud/networking/managed-service learning and portfolio value
  than first learning how to operate RabbitMQ on Azure Container Apps.

Azure remains a worthwhile second-cloud portability proof. Its API runtime, PostgreSQL,
Redis, secrets, migration-job, and Terraform support are adequate without application
changes. It loses the first position solely because no first-party managed RabbitMQ
fits the current architecture; the self-hosted alternative adds a substantial,
unrelated operational concern.

## Implementation sequencing after this investigation

The first AWS implementation PR should contain only the accepted AWS deployment
vertical slice: provider/version choices, protected-state approach, network/security
groups, ECS/ALB, RDS, ElastiCache Valkey, Amazon MQ, secret/identity wiring, the
explicit one-shot migration gate, `terraform fmt`/`validate`, a no-secrets runbook,
cost/teardown instructions, and a new verification checkpoint. It must preserve GHCR,
use an immutable image tag, and avoid a NAT Gateway unless later reviewed evidence
changes the selected staging boundary.

The later Azure portability PR should create a separate `infra/azure/` implementation
for the same artifact/configuration/lifecycle contracts, explicitly decide and record
the RabbitMQ hosting option, add its VNet/private DNS/private-endpoint model and
Container Apps Job migration gate, then compare a deployed scenario with AWS. It must
not claim that shared Terraform resources make the clouds interchangeable.

## Research sources

- [AWS ECS private registry authentication](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/private-auth.html) and [Fargate task networking](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/fargate-task-networking.html)
- [Amazon MQ private brokers](https://docs.aws.amazon.com/amazon-mq/latest/developer-guide/configuring-private-broker.html), [RabbitMQ listener ports](https://docs.aws.amazon.com/amazon-mq/latest/developer-guide/rabbitmq-defaults.html), and [Amazon MQ pricing](https://aws.amazon.com/amazon-mq/pricing/)
- [ECS command overrides for one-shot tasks](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_RunTask.html), [ElastiCache Serverless Terraform resource](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/elasticache_serverless_cache), and [Amazon MQ Terraform resource](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/mq_broker)
- [AWS Fargate pricing](https://aws.amazon.com/fargate/pricing/), [ALB pricing](https://aws.amazon.com/elasticloadbalancing/pricing/), and [NAT Gateway pricing](https://aws.amazon.com/vpc/pricing/)
- [Azure Container Apps containers and private registries](https://learn.microsoft.com/en-us/azure/container-apps/containers), [ingress](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview), [health probes](https://learn.microsoft.com/en-us/azure/container-apps/health-probes), and [jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs)
- [Azure PostgreSQL Flexible Server private networking](https://learn.microsoft.com/en-us/azure/postgresql/network/concepts-networking-private), [Azure Managed Redis](https://learn.microsoft.com/en-us/azure/redis/overview), and [legacy Azure Cache for Redis retirement](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/retirement-faq)
- [Azure Container Apps Azure Files mounts](https://learn.microsoft.com/en-us/azure/container-apps/storage-mounts-azure-files), [Azure Managed Redis Terraform resource](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/managed_redis), [Container App Terraform resource](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app), and [Container App Job Terraform resource](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_job)
- [Azure PostgreSQL Flexible Server pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/), [Azure Managed Redis pricing](https://azure.microsoft.com/en-us/pricing/details/managed-redis/), and [Azure Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/)
