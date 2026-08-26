# First Staging Environment Architecture

## Historical purpose and environment model

This is the historical Render staging design. It was accepted and represented in
Terraform, but was later rejected as the actual deployment target because the full
topology could not meet the intended free/cheap boundary. No Render resources were
created. [ADR 0020](../decisions/0020-use-aws-for-first-cloud-deployment.md) selects
AWS for the first real cloud deployment; the comparison is in
[Cloud Portability Audit](cloud-portability-audit.md).

The former environment model was:

```text
Local: Docker Compose
Staging: Render
Final cloud target: AWS, later
```

Render staging was designed to practise deployment and infrastructure automation, then
to support deployed load measurements and bottleneck investigation. It did not select
the future AWS production topology. [ADR 0018](../decisions/0018-use-render-for-first-disposable-staging-environment.md)
records that historical decision.

## Target topology

```text
                    Internet
                       |
             Render HTTPS ingress
                       |
                PulseFlow.Api
          GHCR sha-<commit-sha>
                       |
                private network
          +------------+------------+
          |            |            |
 Render PostgreSQL  Render Key Value  RabbitMQ service
     managed       Redis-compatible  + persistent disk
```

`PulseFlow.Api` initially runs as one instance. The architecture permits an
intentional later increase in API replica count, but that is not a capacity target or
a scaling conclusion.

The normal application path uses Render private connectivity. Operator and debugging
access is a separate, controlled path:

```text
trusted developer IP
    +-- temporary PostgreSQL administration
    +-- temporary Redis debugging
    +-- RabbitMQ management UI
```

This diagram does not mean that dependencies are public application endpoints.

## API artifact and ingress

Staging consumes the existing immutable GitHub Container Registry image:

```text
ghcr.io/<repository-owner>/pulseflow-api:sha-<commit-sha>
```

The SHA tag identifies the selected deployment revision. Render must not rebuild
`PulseFlow.Api` source as the normal staging deployment path. The OCI image remains
environment-independent; runtime configuration and credentials are supplied outside
the image and are never committed to Git.

HAProxy remains only in the local Docker Compose Multi topology. It is not deployed
to Render. In staging, Render owns the public HTTPS ingress and load-balancing
implementation:

```text
Local:   client -> HAProxy -> PulseFlow.Api
Staging: Internet -> Render HTTPS ingress / load balancing -> PulseFlow.Api
```

The API continues to listen on its normal HTTP port. It must not depend on
Render-specific load-balancer behavior.

Render's traffic-readiness check must use `GET /health/ready`. A new instance must
not receive normal traffic until its mandatory initialization and current dependency
checks succeed. `GET /health/live` remains a separate process/application-liveness
endpoint; it is not the dependency-readiness decision. The current endpoint semantics
are documented in [the ingestion architecture](ingestion.md#startup-initialization-liveness-and-readiness).

## Managed dependencies and private boundaries

### PostgreSQL

Render managed PostgreSQL is the staging durable event store. `PulseFlow.Api` depends
only on the PostgreSQL connection configuration, currently
`ConnectionStrings:PulseFlow`; it must not depend on Render's physical hosting,
proxying, replication, or storage implementation. Normal API traffic uses the
private/internal PostgreSQL endpoint.

PostgreSQL data survives ordinary API image redeployments. Before intentionally
destroying the whole staging environment, any PostgreSQL data worth retaining must be
exported using normal PostgreSQL logical backup/dump and restore mechanisms. Physical
disk access is not a portability boundary.

Temporary external administration may be enabled for a trusted developer IP with a
restrictive allow list, for example for `psql`, pgAdmin, or DBeaver. Unrestricted
public database access is not part of normal operation.

### Redis-compatible Key Value storage

Render Key Value / Redis-compatible managed storage holds the operational distributed
ingestion rate-limit and quota state. `PulseFlow.Api` depends only on
`ConnectionStrings:Redis`; normal traffic uses its private/internal endpoint. Redis
is not the primary durable event store.

Redis state is disposable when the complete staging environment is intentionally
rebuilt. External Redis debugging may be temporarily enabled only for a trusted
developer IP and must otherwise remain disabled or restricted.

### RabbitMQ

RabbitMQ runs as a separate Render service with a persistent disk. It is
infrastructure, not part of the API container. `PulseFlow.Api` uses its private AMQP
endpoint through `ConnectionStrings:RabbitMq`; AMQP port `5672` remains private.
RabbitMQ persistence protects staging broker state during ordinary service restarts
and API redeployments, but its data need not be migrated when the entire disposable
staging environment is destroyed.

The RabbitMQ HTTP management interface may be made externally available for staging
operator/debug use only when protected by authentication. That access is distinct
from AMQP: exposing the management UI must not expose AMQP `5672` publicly.

## Runtime configuration and secrets

Render supplies runtime configuration and secrets. The exact application keys must
match the existing code:

- `ConnectionStrings:PulseFlow` for PostgreSQL;
- `ConnectionStrings:Redis` for Redis-compatible Key Value storage;
- `ConnectionStrings:RabbitMq` for RabbitMQ connection information and credentials;
- `RabbitMq:QueueName`, `RabbitMq:ConsumerCount`, and
  `RabbitMq:PublisherChannelCount` for RabbitMQ runtime settings;
- `Ingestion:ChunkCapacity` and `Ingestion:MaxBatchBytes` for ingestion runtime
  configuration;
- `IngestionRateLimit:RequestLimit` and `IngestionRateLimit:WindowDuration` for the
  distributed ingestion quota.

Connection information and RabbitMQ credentials must not be baked into the image or
committed to the repository. Deployment-specific ingestion settings must be supplied
as runtime configuration; the repository retains only non-secret default values.

## Current API and consumer coupling

Today one `PulseFlow.Api` process hosts both the HTTP API and a RabbitMQ consumer:

```text
PulseFlow.Api
├─ HTTP API
└─ RabbitMQ consumer
```

Consequently, with `RabbitMq:ConsumerCount = 1`:

```text
2 API replicas = 2 HTTP API processes + 2 RabbitMQ consumers
```

Increasing API replicas is therefore not an isolated HTTP scaling change. Any later
deployed scaling experiment must account for the corresponding consumer-count change.
`PulseFlow.Worker` extraction and independent consumer scaling are deliberately
deferred.

## Migration and persistence lifecycle

EF Core migrations already exist. The API validates that migrations are current but
does not run them. The staging rollout invariant is:

```text
new immutable API version selected
        -> apply pending EF Core migrations exactly once
        -> migration succeeds
        -> new API version becomes active
```

Render's pre-deploy lifecycle phase is the accepted boundary for this operation. No
API replica may independently run migrations at startup. The published final runtime
image contains the framework-dependent EF Core migration bundle at
`/app/migrations/pulseflow-migrations`, alongside the normal
`dotnet PulseFlow.Api.dll` entry point. Render runs:

```text
/app/migrations/pulseflow-migrations --connection "$ConnectionStrings__PulseFlow"
```

in the selected immutable image before starting the API. The PostgreSQL connection
string is supplied as runtime configuration and is not embedded in the image or
Terraform source. The runtime image does not contain `dotnet-ef`. This selection is
recorded in [ADR 0019](../decisions/0019-run-render-migrations-from-an-immutable-api-image.md).

Replacing `sha-old` with `sha-new` must not delete PostgreSQL data, RabbitMQ persistent
data, or managed Redis merely because the API is redeployed. Destroying the complete
disposable staging environment may intentionally destroy PostgreSQL, Redis, and
RabbitMQ state/disk subject to the PostgreSQL backup rule above.

## Infrastructure definition and deferred deployment work

The first reviewable Terraform definition is in
[`infra/render/`](../../infra/render/). It represents the Render API service, managed
PostgreSQL, Key Value storage, private RabbitMQ service, RabbitMQ persistent disk,
private connection values, public API ingress, `/health/ready`, immutable GHCR image
selection, and the pre-deploy migration command. It does not create resources until
a later controlled `plan` and `apply`.

PostgreSQL and Key Value optional operator allow lists are represented as an external
input. RabbitMQ is deliberately a private service, so AMQP `5672` is not public. The
selected Render Terraform provider has no clean resource model for exposing only the
RabbitMQ management UI from that private service. Operator management-UI access is
therefore deferred rather than weakening the AMQP boundary.

Still deferred: a Render account bootstrap, private-GHCR credential bootstrap,
automatic deployment/CI-CD integration, first `plan`/`apply`, `PulseFlow.Worker`
extraction, RabbitMQ prefetch or consumer-count changes, RabbitMQ HA/backup, load
tests, and final AWS architecture.
