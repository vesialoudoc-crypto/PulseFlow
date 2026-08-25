# First Staging Environment Architecture

## Purpose and environment model

The first disposable staging environment uses Render. It is an intermediate
deployment and operations-learning environment, not the final cloud architecture.
The environment model is:

```text
Local: Docker Compose
Staging: Render
Final cloud target: AWS, later
```

Render staging exists to practise deployment and infrastructure automation, then to
support deployed load measurements and bottleneck investigation. It does not select
the future AWS production topology. [ADR 0018](../decisions/0018-use-render-for-first-disposable-staging-environment.md)
records the platform and topology decision.

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
- `Ingestion:ChunkCapacity` for ingestion runtime configuration;
- `IngestionRateLimit:RequestLimit` and `IngestionRateLimit:WindowDuration` for the
  distributed ingestion quota.

Connection information, RabbitMQ credentials, and ingestion settings must not be
baked into the image or committed to the repository.

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

Render's pre-deploy lifecycle phase is the preferred boundary for this operation. No
API replica may independently run migrations at startup. The published final runtime
image contains the normal API publish output and the EF Core migration bundle from the
same source revision. The bundle is at `/app/migrations/pulseflow-migrations`; it is
not a second OCI artifact or GHCR package. The Dockerfile retains its `migrations`
stage because local Compose uses that stage's `dotnet ef database update` command.

The future Render pre-deploy command is:

```text
/app/migrations/pulseflow-migrations --connection "$ConnectionStrings__PulseFlow"
```

Render supplies `ConnectionStrings__PulseFlow` as a runtime secret. The shell expands
that environment variable and passes its value to the bundle's `--connection` option;
the image contains neither a connection string nor a Render endpoint. The API keeps
its normal `dotnet PulseFlow.Api.dll` entry point, so migration execution is an
explicit pre-deploy action rather than normal API startup behavior.

"Exactly once" here means one controlled pre-deploy migration execution before a
rollout, not a claim of mathematically exactly-once database semantics. EF Core's
migration history determines which migrations are already applied, so rerunning the
bundle on a current database succeeds without applying schema changes. See
[ADR 0019](../decisions/0019-use-ef-core-migration-bundle-in-api-image.md).

Replacing `sha-old` with `sha-new` must not delete PostgreSQL data, RabbitMQ persistent
data, or managed Redis merely because the API is redeployed. Destroying the complete
disposable staging environment may intentionally destroy PostgreSQL, Redis, and
RabbitMQ state/disk subject to the PostgreSQL backup rule above.

## Deferred implementation work

The next infrastructure-as-code task must create and configure the Render API
service, managed PostgreSQL, Key Value storage, RabbitMQ service, and RabbitMQ
persistent disk. It must also establish private connectivity, public API ingress,
runtime configuration and secrets, `/health/ready`, immutable GHCR SHA-image
selection, restricted operator access, and the documented pre-deploy migration command.

This decision does not deploy resources, add Terraform/OpenTofu, configure a Render
account, implement automatic staging deployment, extract a worker, change RabbitMQ
prefetch or consumer count, run load tests, or change the local Compose topology.
