# Roadmap

## Purpose

The roadmap defines the sequence of learning and implementation. It is not a promise of a specific architecture or timeline. Each stage must begin by clarifying requirements and end with a working, verifiable result.

## Statuses

- **Not started** — implementation of the stage has not begun.
- **In progress** — the current small outcome has been defined and work is underway.
- **Completed** — the expected result of the stage has been achieved and verified.
- **Deferred** — the stage has been deliberately postponed for a recorded reason.

A status applies to the stage as a whole. Incomplete details should be recorded in the most recent checkpoint in `docs/progress/` rather than hidden behind an overall completion percentage.

## Principles for Progressing Through the Stages

1. Build a minimal vertical slice first, then add infrastructure complexity.
2. Introduce a new component to solve an observed or explicitly stated problem.
3. Record options and criteria before a significant choice; create an ADR afterward when appropriate.
4. Every stage includes documentation, automated checks, and a demonstrable scenario.
5. The next stage does not require the previous one to be perfect, but it must not depend on unknown or non-working behavior.

## Stage 0: Documentation Foundation

**Status:** Completed (initial version)

### Goals

- Record the project's purpose, boundaries, and principles.
- Separate stable truth, the roadmap, and chronological progress records.
- Create a reliable entry point for future ChatGPT/Codex sessions.

### Learning Objectives

- Learn to separate product requirements from architectural decisions.
- Define verifiable outcomes without premature design.

### Expected Result

The repository contains the linked documents `00_PROJECT_CONTEXT.md`, `01_SOURCE_OF_TRUTH.md`, and `02_ROADMAP.md`, plus chronological checkpoint/handoff records in `docs/progress/`. The most recent checkpoint states what actually exists, what was verified, what remains unresolved, and what should happen next without presenting proposed technologies as accepted decisions.

## Stage 1: Basic Data Ingestion

**Status:** Completed

### Goals

- Create a minimal ASP.NET Core API for event ingestion using the accepted v1 event envelope.
- Define the remaining first-version HTTP contract, validation rules, and request result.
- Persist accepted data in PostgreSQL.
- Verify persistence with an integration test that may inspect PostgreSQL directly.
- Prepare reproducible local execution and basic automated tests.

### Learning Objectives

- The ASP.NET Core request lifecycle and HTTP API design.
- Validation at the system boundary and error handling.
- Working with PostgreSQL, transactions, and migrations.
- Integration testing of the API and database.

### Expected Result

From a clean environment, one can start the application and PostgreSQL, submit a valid ingestion request, have an integration test confirm the persisted data directly in PostgreSQL, and receive a predictable error for an invalid request. The first-version contract and constraints are documented. A public retrieval endpoint is not required to verify persistence for this stage.

### Current Scope Clarifications

- PulseFlow is a production-style event ingestion and processing system, not a CRUD API for events.
- The accepted Event Contract v1 envelope and its opaque arbitrary JSON object `payload` remain unchanged.
- A technical event identifier may exist in the persistence model, but it does not imply that `GET /api/events/{id}` is a primary user flow or a Stage 1 requirement.
- An integration test may verify successful ingestion by querying PostgreSQL directly; no public GET endpoint is required solely for persistence verification.
- A query or retrieval API for operators or data consumers has not been defined.
- The mechanism that transfers persisted events to later processing has not been defined.
- Batch ingestion uses NDJSON framing: each completely received line is an independent Event Contract v1 record. Valid records may be accepted independently; a malformed or truncated record does not invalidate other completely received valid records. See [ADR 0001](decisions/0001-use-ndjson-for-batch-ingestion.md).
- NDJSON records are read, parsed, and validated sequentially. Valid records are persisted to PostgreSQL in configurable chunks, and a record becomes accepted only when the transaction containing its chunk commits successfully. See [ADR 0002](decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md).
- The chunk size is operational configuration rather than part of the public contract. The implemented initial value is 100, is configurable and validated, and is not a tuned performance conclusion; later load measurements may change it.
- On normal completion, ingestion orchestration reports total and accepted counts with rejected derived as `Total - Accepted`. A chunk-store failure propagates without a handler result; earlier committed chunks remain durable. The resulting retry/idempotency problem remains unresolved. See [ADR 0006](decisions/0006-keep-ingestion-handler-failure-propagation-simple.md).
- Normal partial-invalid HTTP 200 semantics are accepted in [ADR 0007](decisions/0007-expose-controller-based-ndjson-ingestion-api.md). Batch limits and compression have not been accepted.

### Do Not Decide in Advance

Do not treat the initial chunk capacity as tuned, or select RabbitMQ, polling, a queue or stream technology, delivery guarantees, idempotency semantics, an API query model, batch limits, compression, PostgreSQL retry behavior, transaction isolation level, multiple application instances, the final event schema, or the cloud topology before a separate decision establishes the need and criteria. Redis is retained for the accepted future distributed ingestion rate-limiting use case, but its algorithm, quota values, window strategy, implementation, and failure behavior remain undecided.

## Stage 2: Asynchronous Processing

**Status:** Completed

### Goals

- Decouple HTTP batch ingestion from NDJSON parsing and Event Contract v1 validation
  for the many-client ingestion scenario.
- Use RabbitMQ as the Stage 2 work-transfer broker between `PulseFlow.Api` and
  `EventParserConsumer` instances.
- Move NDJSON parsing, Event Contract v1 validation, and persistence behind the
  asynchronous boundary.
- Establish a minimal verifiable asynchronous path in which parser-consumer capacity
  can be increased independently from API capacity.

### Learning Objectives

- Boundaries between HTTP acceptance and asynchronous parsing, validation, and
  persistence.
- RabbitMQ work transfer and the limits of the selected initial semantics.
- Consumer ownership, independently scalable parser capacity, and graceful shutdown.
- Coordination between the API, broker, parser consumer, and PostgreSQL.

### Expected Result

`PulseFlow.Api` accepts an NDJSON batch without parsing individual records or applying
Event Contract v1 validation, publishes the complete raw batch body to RabbitMQ, and
returns HTTP `202 Accepted` only after RabbitMQ confirms successful publication.
`EventParserConsumer` receives the batch, performs NDJSON
parsing and Event Contract v1 validation, and persists valid events through an
appropriate boundary to PostgreSQL. Multiple parser consumers can be introduced to
increase parsing capacity independently of the API. The accepted RabbitMQ decision and
the limits of the initial semantics are documented. A public batch-status endpoint is
not required until its contract and storage model have been separately decided.

### Current implementation

PLAN 003 Steps 1 through 5 are implemented. `PulseFlow.Api` publishes the complete
raw NDJSON body and returns HTTP 202 only after RabbitMQ confirms publication.
`AddIngestionMessaging` hides RabbitMQ resource ownership from application composition.
It owns one application connection, one publisher-confirmation channel, and one
consumer channel for each worker. One hosted `EventParserConsumer` starts the validated
number of competing workers, reuses the Stage 1 parsing/validation/chunked-persistence
path, and acknowledges a delivery only after that handler succeeds. Real RabbitMQ and
PostgreSQL Testcontainers tests verify the complete path and, through RabbitMQ queue
metadata, that `ConsumerCount = 2` registers two consumers on the configured queue.
`RabbitMq:ConsumerCount` defaults to 1 and is validated as positive; it
controls parser-consumer capacity, not HTTP API instance count. Retry, dead-letter,
requeue, Outbox, idempotency, delivery guarantees, and batch status remain intentionally
unresolved. The adapter copies each delivery body and invokes the application handler
directly; it has no application-level queue or buffer. Broker-side flow control through
RabbitMQ prefetch is intentionally deferred.

### Do Not Decide in Advance

- Request and message-size limits, compression, exchange and queue topology,
  routing-key conventions, acknowledgement/requeue behavior, retry policy,
  dead-letter queues, delivery guarantees, broker-side flow control, RabbitMQ
  prefetch, and deployment topology. Prefetch must be designed with the outcome of a
  parsing or persistence failure, rather than selected before acknowledgement behavior
  for that failure is decided.
- Outbox, idempotency, deduplication, batch-status persistence, and public
  status/query endpoints; these are not prerequisites for the first Stage 2 slice.
  Redis implementation is deferred to Stage 4 for distributed ingestion rate limiting,
  not for generic caching or batch-status storage.

## Stage 3: Reliability and Correctness During Failures

**Status:** Completed

### Goals

- Define and implement behavior for repeated requests and repeated delivery.
- Decide what happens to a delivery when parsing or persistence fails before choosing
  RabbitMQ prefetch or other broker-side flow control.
- Define retry and recovery behavior, distinguishing recoverable and unrecoverable
  processing outcomes.
- Explicitly choose no automatic processing retry: a terminal processing failure is
  rejected without requeueing to the DLQ for later manual recovery.
- Test partial failures between the main components.
- Prevent silent data loss in the selected scenarios.

### Learning Objectives

- Idempotency and deduplication.
- Transaction boundaries and consistency.
- At-most-once, at-least-once, and the consequences of the chosen model.
- Backoff, retries, poison messages, and failure recovery.
- Races, locks, and concurrent updates.

### Expected Result

Automated or reproducible checks exist for a documented set of failures and repetitions. The system reaches a defined state, does not produce inexplicable results, and provides enough information for diagnosis. Accepted guarantees are stated without claiming "exactly once" unless that claim has been demonstrated within defined boundaries.

### Current implementation

The first reliability slice is implemented. For an unexpected parsing or PostgreSQL
persistence failure, `EventParserConsumer` logs the failure, rejects only that RabbitMQ
delivery with `requeue = false`, and routes it to a durable dedicated dead-letter queue.
Each delivery receives exactly one complete processing attempt. Successful processing
still acknowledges only after the handler completes; shutdown cancellation does not
reject a delivery. Malformed NDJSON and contract-invalid records remain record-level
outcomes inside the established pipeline and do not dead-letter the whole batch.

The main queue uses application-owned dead-letter queue arguments. An existing local
queue that was declared before this change must be recreated manually because RabbitMQ
does not permit those queue arguments to change. Application startup never deletes,
purges, or silently recreates a queue. The implementation and its limitations are
recorded in [ADR 0012](decisions/0012-process-each-rabbitmq-delivery-once.md).

The hosted parser consumer starts exactly the configured number of workers and passes
the host stopping token to each one. It waits for all worker tasks with
`Task.WhenAll`; it does not coordinate worker failures, cancel siblings, restart
workers, or treat a normally completed worker as a special failure. A worker exception
naturally faults the `BackgroundService` after `Task.WhenAll` completes. This lifecycle
is recorded in [ADR 0013](decisions/0013-use-backgroundservice-worker-lifecycle.md).

The next reliability slice is implemented through Event Contract v2. Every valid event
has a source-owned UUID `eventId`; the logical identity is `(source, eventId)`. The
database stores non-null `event_id` and enforces a unique `(source, event_id)` index.
Each persistence chunk remains one explicit transaction and one PostgreSQL
`INSERT ... ON CONFLICT (source, event_id) DO NOTHING` command. A valid duplicate is a
successful no-op, including when a complete batch is replayed after a later chunk
failed. Missing or malformed event IDs remain record-level invalid outcomes and do not
dead-letter a whole batch. The real-PostgreSQL tests cover duplicate replay, different
sources with one UUID, partial chunk replay, and concurrent inserts. See
[ADR 0011](decisions/0011-use-event-level-idempotency.md).

This is not an end-to-end no-loss, at-least-once, or exactly-once guarantee. RabbitMQ
dead-letter republishing can fail, and earlier PostgreSQL chunks can already be durable
when a later chunk fails. No automatic retry or DLQ redrive exists; later recovery is
manual and separately defined. Retry queues, Outbox, and prefetch remain unresolved.

## Stage 4: Horizontal Scaling and Load

**Status:** Completed (local horizontal-scaling/load milestone)

### Goals

- Run multiple instances of applicable components.
- Verify distribution and concurrent-processing correctness.
- Introduce Redis-backed distributed ingestion rate limiting so multiple
  `PulseFlow.Api` instances share one global quota state.
- Reject an over-limit request with HTTP 429, with `Retry-After` where appropriate,
  before it is published to RabbitMQ.
- Create a reproducible load scenario.
- Establish a controlled local Single-vs-Multi architecture and load baseline.

### Learning Objectives

- Stateless design and coordination through external state.
- Distributed quota counters and rate limiting across independently serving API
  instances.
- Parallelism limits, backpressure, and resource saturation.
- Throughput, latency, errors, and percentile interpretation.
- Connection pools, indexes, locks, and the database's impact on scaling.

### Expected Result

The local load test runs reproducibly and produces a clear baseline report. Multiple
`PulseFlow.Api` instances share Redis-backed rate-limit/quota state: process-local
in-memory counters are not used because they would be incorrect when requests are
distributed across instances. Redis stores this fast-changing operational state only;
it is not the primary event store, generic cache, or batch-status store. Requests
exceeding the accepted quota receive HTTP 429 and are not published to RabbitMQ.
Applicable components work correctly in the tested multi-instance scenarios, and the
limits of the conclusions are stated explicitly. This milestone does not establish
production capacity, linear scaling, or a causal bottleneck.

### Current implementation

The Stage 4 Redis limiter foundation is implemented. `StackExchange.Redis` is
registered as one shared process-level `IConnectionMultiplexer`, configured through
`ConnectionStrings:Redis`. `IngestionRateLimit:RequestLimit` and
`IngestionRateLimit:WindowDuration` are startup-validated. The registered
`RedisIngestionRateLimiter` implements the Redis-independent
`IIngestionRateLimiter` contract with one atomic Lua script and the global key
`pulseflow:rate-limit:ingestion:global`. It uses the accepted fixed-window algorithm,
returns an exceeded result with the Redis TTL as `RetryAfter`, and fails closed with an
unavailable result for a Redis operation failure. The accepted global scope and
fail-closed behavior are recorded in
[ADR 0014](decisions/0014-use-redis-for-global-ingestion-rate-limiting.md).

`EventsController` checks the limiter before it reads the request body or calls the
RabbitMQ publisher. An exceeded result returns HTTP 429 with `Retry-After` rounded up
to whole delta seconds; an unavailable result returns HTTP 503. Focused HTTP tests
verify these mappings, confirm that rejected requests do not call the publisher, and
confirm that an allowed request is published and returns HTTP 202.

Two explicit local measurement topologies are accepted. Single remains the direct
`client -> api` control. Multi uses HAProxy as its only public HTTP entry point and
routes round-robin across healthy internal `api-1` and `api-2` replicas; both API
processes also host one RabbitMQ consumer when `RabbitMq:ConsumerCount = 1`. Both
topologies share PostgreSQL, RabbitMQ, and Redis. This local choice does not select
the future cloud/AWS ingress. See
[ADR 0016](decisions/0016-use-haproxy-for-local-multi-instance-api-ingress.md).

The reproducible load-test harness is available at
[`tests/performance/ingestion-baseline.js`](../tests/performance/ingestion-baseline.js). It uses a closed model
with configurable VUs and duration, posts one valid Event Contract v2 NDJSON record
per request, and checks for HTTP 202. It intentionally has no performance thresholds
or measured target. [Ingestion Performance Baseline 001](performance/ingestion-baseline-001.md)
is retained unchanged as the historical pre-fix three-level local sweep at commit
`ad1da229afeb36dbdf17e7a18d780196a137abd6`. It predates the RabbitMQ
publisher-channel pool and performance-runner correctness changes; its 30-VU result
therefore does not represent the current configuration.

[Ingestion Performance Baseline 002](performance/ingestion-baseline-002.md) records
the final fresh post-fix `10 VU / 10s`, `20 VU / 10s`, and `30 VU / 10s` sweep for
one API instance, `RabbitMq:ConsumerCount = 1`, `RabbitMq:PublisherChannelCount = 4`,
and `Ingestion:ChunkCapacity = 100`. The corrected runner verifies completion by
requiring PostgreSQL's persisted event count to equal the k6 HTTP-202 acceptance
count after the queue is empty. Every Baseline 002 row meets that invariant. Its
local-run instructions require a high enough local rate-limit quota to prevent HTTP
429 from becoming the limiting factor. No bottleneck conclusion, target, or
optimization decision is accepted from this local baseline.

[Ingestion Single vs Multi Comparison 001](performance/ingestion-single-vs-multi-001.md)
records the completed controlled `10 VU / 10s`, `20 VU / 10s`, and `30 VU / 10s`
comparison. It found Multi accepted approximately 16-22% more HTTP requests per
second at 20 and 30 VUs, but no improvement at 10 VUs. Every valid measured run had
0% HTTP failures and equal accepted HTTP 202 and final persisted-row counts. This is
not an isolated HTTP/API scaling measurement: Multi changes both the API-process
count and RabbitMQ-consumer count from one to two. The substantial RabbitMQ backlog
after each ten-second acceptance burst means HTTP acceptance throughput is not a
measure of sustainable end-to-end persistence throughput. No linear-scaling,
production-capacity, or proven-bottleneck conclusion is accepted from this experiment.

The startup-readiness slice is implemented. The process exposes a dependency-free
`GET /health/live` endpoint and a tagged ASP.NET Core `GET /health/ready` endpoint.
Readiness requires successful one-time PostgreSQL, RabbitMQ, and Redis initialization,
completed parser-consumer subscription, and current side-effect-free dependency health
checks. Runtime dependency failure makes readiness return HTTP 503 without rerunning
global initialization. The API checks that migrations are current but never executes
migrations; the Compose migrations service remains responsible for applying them.

HAProxy active health checks use `/health/ready`, so its Multi ingress sends client
traffic only to replicas that have completed mandatory initialization and whose
runtime dependencies are healthy. Before it starts samplers or fixed 10-second k6,
`tests/performance/run.ps1` waits for `Single`'s API readiness endpoint or directly
probes both `Multi` replicas' readiness endpoints through the internal Compose network.
Multi starts measurement only when both replicas return HTTP 200 in the same polling
cycle; each probe has a one-second timeout within the two-minute overall startup
deadline. It does not use a synthetic ingestion POST, row deletion, Redis-key deletion,
or an arbitrary sleep as its readiness mechanism. Multi also preserves HAProxy-log
evidence that both replicas received measured client POST traffic. See
[ADR 0015](decisions/0015-separate-startup-initialization-from-runtime-readiness.md),
[ADR 0016](decisions/0016-use-haproxy-for-local-multi-instance-api-ingress.md), and
[ADR 0017](decisions/0017-warm-full-ingestion-path-before-performance-baseline.md)
for the superseded diagnostic warm-up history.

The local horizontal-scaling/load milestone is complete. It provides Redis-backed
shared ingestion quota, multiple API instances behind HAProxy local ingress,
readiness/liveness checks, a reproducible Single and Multi performance runner, and a
controlled `10 / 20 / 30 VU` Single-vs-Multi comparison. Each valid measured run had
0% HTTP failures and equal accepted HTTP 202 and persisted PostgreSQL-row counts. The
comparison also documents that Multi doubles the co-hosted RabbitMQ-consumer count;
therefore it does not isolate API/ingress scaling.

Further local bottleneck attribution and tuning are deliberately deferred. API,
RabbitMQ, PostgreSQL, Redis, and HAProxy share one Docker Desktop host and its CPU,
RAM, disk, and virtualization resources, so another local optimization cycle would
not reliably attribute a constraint to one component. The preserved objective is:

```text
local topology baseline -> deployment -> deployed load measurement
-> bottleneck investigation -> justified improvement -> before/after
```

The bottleneck objective will be executed in the deployed-environment performance
phase after a deployment architecture exists; it has not been deleted or satisfied by
the local baseline. RabbitMQ backlog and CPU observations remain investigation leads,
not proof that RabbitMQ is a bottleneck.

### Do Not Decide in Advance

The exact number of instances and target performance metrics remain undecided. The
local baseline is not sufficient evidence to select either. Do not resume local
bottleneck optimization on the shared-host Docker Desktop topology; first design and
implement deployment, then investigate and optimize a measured deployed constraint.

## Stage 5: Deployment to AWS

**Status:** In progress (first disposable AWS lifecycle proven; next: deployed observability and performance measurement)

### Goals

- Establish the first cheap, replaceable staging environment for deployment practice,
  infrastructure automation, and later deployed measurements.
- Select a minimal AWS architecture based on the already working system.
- Automate the creation or configuration of the required infrastructure.
- Configure secure storage for configuration and secrets.
- Create a CI/CD path with automated checks and controlled deployment.
- Limit costs and document how to remove or stop resources.
- After deployment exists, measure the deployed topology before investigating a real
  bottleneck and making one justified before/after improvement.

### Learning Objectives

- Networking, compute, managed services, and the AWS responsibility model.
- Infrastructure as Code.
- Building, testing, packaging, and delivering the application.
- Configuration, secret, and environment management.
- Basic cloud security and cost-control practices.

### Expected Result

The system can be deployed reproducibly to AWS, a demonstration scenario can be
performed, and a verified change can be delivered through an automated process. The
architecture, operational commands, approximate cost, and safe resource-removal
procedure are documented. This stage starts by designing and implementing the minimal
deployment architecture from the already working application; it does not assume a
specific AWS service, network topology, or release strategy. Its deployed-environment
performance phase preserves the deferred sequence: deployed load measurement,
bottleneck investigation, one justified improvement, and before/after comparison.

### Current implementation

`PulseFlow.Api` is published to GitHub Container Registry only when a developer
manually starts the **Publish PulseFlow.Api image** workflow from GitHub Actions. This
explicit action represents a staging-image release. The workflow runs its build, test,
formatting, and documentation checks before publishing. The image name is
`ghcr.io/<repository-owner>/pulseflow-api`. Every published image receives an
immutable full-commit-SHA tag, `sha-<40-character-commit-sha>`, which is the
deployable and auditable tag. The same image also receives the movable `develop` tag
as a convenience reference; `latest` is not published.

Pull a specific immutable image with:

```text
docker pull ghcr.io/<repository-owner>/pulseflow-api:sha-<40-character-commit-sha>
```

The image contains the published API output plus the framework-dependent migration
bundle at `/app/migrations/pulseflow-migrations`. It receives connection strings and
other environment-specific configuration at runtime. It is intended to be reused
unchanged by local and future staging/cloud deployment environments; no staging or
cloud infrastructure is defined by this publication step.

Render was accepted historically as a disposable staging experiment and has a complete
but unapplied Terraform definition in [`infra/render/`](../infra/render/). It was
later rejected as the actual deployment target because its complete required topology
could not meet the intended free/cheap boundary. No Render account bootstrap,
credentials, state, plan, apply, or resources were created. The Terraform remains
intact as a historical portability reference and must not be extended or deleted in
the AWS work. ADR 0018 is superseded; its historical record and the Render migration
bundle decision remain useful context.

The AWS/Azure portability audit selected AWS for the first real cloud deployment. The
accepted target is an Application Load Balancer, ECS Fargate API tasks, RDS PostgreSQL,
ElastiCache Serverless for Valkey, and Amazon MQ for RabbitMQ. The API continues to
consume the existing immutable GHCR SHA-tagged image and `/health/ready` remains the
traffic-readiness endpoint. The same image runs its migration bundle exactly once in a
controlled ECS Fargate task before the API service update. AWS does not require ECR
for this path: ECS can authenticate to GHCR through a Secret Manager-backed registry
credential. The intentionally cheap first staging proposal avoids a NAT Gateway by
allowing public-IP Fargate tasks whose security group accepts HTTP only from the ALB;
PostgreSQL, Valkey, and RabbitMQ remain private. This is a staging boundary, not a
production-networking claim.

The first AWS Terraform definition exists in [`infra/aws/`](../infra/aws/). It is
locally formatted and validated with Terraform 1.15.9, AWS provider 6.61.0, and Random
provider 3.9.0. Its accepted first-plan configuration is Frankfurt (`eu-central-1`),
a no-NAT VPC, ALB, one
0.25-vCPU/512-MiB Fargate API task after successful deployment, private Single-AZ
`db.t4g.micro` RDS PostgreSQL 17, ElastiCache Serverless Valkey 8, and a private
RabbitMQ 4.2 `mq.m7g.medium` Amazon MQ single instance. It keeps the GHCR SHA image
and adds an explicit deployment script: the service begins at zero tasks, a same-image
migration task must exit zero, then the script rolls out exactly one API task and
proves a unique HTTP-202 event reaches PostgreSQL. The configuration uses local
sensitive state, external GHCR credential bootstrap, explicit execution/task roles,
and a documented paid-resource checkpoint. See
[AWS Staging Environment Architecture](architecture/aws-staging-environment.md) and
[ADR 0021](decisions/0021-define-first-aws-staging-resource-configuration.md).

The repository-root ignored `.env` is now the documented local bootstrap source for
AWS credentials, region, the GHCR package-read credential, and the immutable image.
The staging lifecycle is explicit: the default
`pwsh ./scripts/deploy-aws-staging.ps1` bootstraps the external GHCR secret without
putting the token in Terraform, verifies the image, and saves a plan; after plan/cost
review and approval, `-Apply` applies exactly that saved plan and stops with the ECS
service at desired count zero; `-Deploy` separately runs migration, rollout, and the
end-to-end smoke proof; `-Destroy` removes Terraform-managed resources and verifies
empty Terraform state. Normal destroy keeps the external, non-Terraform-managed GHCR
bootstrap secret reusable; `-Destroy -DeleteBootstrapSecret` is the separate explicit
complete-bootstrap-cleanup mode. AWS profiles remain optional rather than a required
second local credential store. The script resolves installed Terraform and AWS CLI v2
executables itself: it uses `PATH` first, then WinGet Terraform and standard Windows
AWS CLI locations. The current reviewed saved plan requires Terraform 1.15.8, which
`-Apply` verifies before it can invoke Terraform apply.

The first disposable AWS lifecycle has been proven end to end. Terraform apply
reported 49 added, 0 changed, and 0 destroyed. The deployment script then completed
the migration ECS task, public-ALB `/health/live` and `/health/ready` HTTP-200 checks,
an ingestion smoke POST returning HTTP 202, in-VPC PostgreSQL verification of the
exact event, and removal of that exact smoke row. Terraform destroy subsequently
reported 49 destroyed. Its post-destroy verification then exposed a PowerShell
empty-pipeline `.Count` defect; a manual non-mutating `terraform state list` returned
empty, confirming the Terraform-managed staging environment had been removed. The
external GHCR bootstrap secret remains intentionally reusable because normal
`-Destroy` was used without `-DeleteBootstrapSecret`.

The first deployment also established that the RDS-managed master secret contains
credentials, not endpoint metadata. Deployment tooling now obtains endpoint, port,
and database name from `rds describe-db-instances` and retains the credential secret
only for username and password. This is a correction within the accepted architecture,
not a change to its resource mapping or lifecycle sequence.

A second, separate AWS Terraform root defines a temporary EC2 performance environment
in [`infra/aws-ec2-performance/`](../infra/aws-ec2-performance/). It does not replace
or rewrite the managed proof. The accepted low-cost topology is one `c7i-flex.large`
app node with HAProxy and two API containers, plus `t3.small` RabbitMQ, Redis,
PostgreSQL, and k6 nodes. It has 80 GiB of encrypted root-only gp3 storage in total.
All runtime components remain Dockerized and use fixed private IPv4 service addresses.
It uses SSM rather than a public operator path, no NAT, no ALB, no Elastic IP, and no
managed runtime service. EventBridge Scheduler remains defined for weekday 08:00–17:00
`Europe/Warsaw` operation, but Scheduler authorization is not a topology blocker.

The previous `m7i.large` sizing over-optimized benchmark isolation and did not respect
the primary AWS cost constraint. AWS is for short real-cloud proofs; sustained
performance experiments belong to the local/home environment. The lifecycle script now
uses exact environment-tag discovery as an emergency stop fallback when an apply fails
before its full node-output map exists. The original failed attempt remains recorded
unchanged in checkpoint 075; its four rejected `m7i.large` launches and its missing
runtime proof are historical facts. The x86_64/requested `c7i-flex.large` shape was
inspected and EC2 `RunInstances` dry-run returned `DryRunOperation`; no actual
`c7i-flex.large` launch is proven until a future reviewed apply. See [AWS EC2
Performance Environment](architecture/aws-ec2-performance-environment.md),
[ADR 0022](decisions/0022-use-ec2-for-temporary-aws-performance-environment.md), and
[ADR 0023](decisions/0023-prioritize-low-cost-ec2-performance-proof.md).

Cleanup of the failed environment is currently blocked. Terraform's reviewed destroy
plan began removing supporting resources, but an EC2 termination dry-run proved the
active principal lacks `ec2:TerminateInstances` for the sole running Redis node. The
remaining state still contains that node, its root volume, VPC/network resources,
runtime secrets, and non-Scheduler IAM resources. No new Terraform plan has been
created against this partial state. The next active technical objective is for an
administrator to grant or perform the exact termination, then rerun Terraform destroy,
independently verify that the environment and state are empty, and only then create and
review a fresh low-cost saved plan. See
[checkpoint 076](progress/2026-08-27-076-correct-low-cost-ec2-topology-and-blocked-cleanup.md)
for the exact retained-state inventory. Do not resume local bottleneck tuning on the
shared Docker Desktop topology.

Azure remains the later portability proof. Azure Container Apps, PostgreSQL Flexible
Server, Azure Managed Redis, and a Container Apps migration job fit the application,
but Azure has no first-party managed RabbitMQ equivalent. Self-hosting RabbitMQ is a
separate later operating-model decision and is not included in the first AWS slice.
See [Cloud Portability Audit](architecture/cloud-portability-audit.md) and
[ADR 0020](decisions/0020-use-aws-for-first-cloud-deployment.md).

### Do Not Decide in Advance

The first AWS managed-service mapping, staging networking boundary, selected first-plan
region/sizes, local-state boundary, GHCR bootstrap boundary, and manual migration-first
release mechanism are accepted in ADRs 0020 and 0021. The separate EC2 temporary
performance topology, its root-disk-only storage, SSM operator path, and timezone-aware
start/stop schedule are accepted in ADRs 0022 and 0023. Automatic deployment/CI/CD, a real EC2
plan/apply proof, deployed observability and performance measurement, a measured
bottleneck and justified before/after optimization, production networking/HA,
restricted RabbitMQ user management, and the final production topology remain
unresolved. API and RabbitMQ-consumer decoupling also remains deferred.

## Stage 6: Production Hardening

**Status:** In progress (ingestion request-body size limit)

### Goals

- Collect logs, metrics, and traces around the critical path.
- Define health/readiness checks and minimal operational signals.
- Harden the public surface and supply chain.
- Verify backup and recovery where applicable.
- Prepare a runbook, final architecture, and portfolio demonstration.

### Learning Objectives

- Diagnosing distributed requests and building useful signals.
- Alerting, operational thresholds, and noise reduction.
- Authentication, authorization, rate limiting, and dependency management.
- Graceful shutdown, updates, rollbacks, and recovery.
- Defining SLI/SLO targets based on measurements.

### Expected Result

The demonstration includes the normal flow and several controlled failures that are visible in system signals and can be investigated using the runbook. Automated checks, documentation, and the final README allow another developer to evaluate the system, reproduce key scenarios, and understand its limitations.

### Current implementation

The first public-input hardening slice is implemented. `POST /api/events` bounds the
raw NDJSON request body using startup-validated `Ingestion:MaxBatchBytes`: the default
is 10 MiB and configuration cannot exceed 100 MiB. A known oversized Content-Length
is rejected before the body is read; an unknown or chunked body is rejected after at
most the configured bytes and one probe byte. The buffer starts small, grows only as
needed, and is returned to `ArrayPool<byte>` after publishing. `EventsController`
delegates this technical work to the stateless `IIngestionBatchBodyReader` boundary;
its success result owns the pooled buffer until publishing completes. The endpoint
disables Kestrel's request-size limit so this dynamic application limit owns the HTTP
413 contract. Both rejection paths return safe HTTP 413 Problem Details with a trace
ID and do not publish to RabbitMQ. This does not add compression, record-count limits,
message-size limits, parsing in the HTTP API, or infrastructure limits.

The explicit dependency-timeout slice is also implemented through component-owned
options. PostgreSQL uses Npgsql connection and command timeouts; RabbitMQ uses
RabbitMQ.Client connection, handshake, and continuation timeouts plus validated local
token-based deadlines for topology, publisher-slot wait/creation, and publish calls.
Uncertain publisher channels and failed-startup connections are detached immediately;
their background cleanup has its own one-second `RabbitMq:CleanupTimeout` best-effort
budget and does not delay the triggering publish timeout, caller cancellation, or
startup failure. Redis creates its shared multiplexer asynchronously inside the startup
initializer, so the overall startup token bounds its logical wait; a connection that
finishes after cancellation is disposed. Redis also uses StackExchange.Redis connect
and async timeouts, with `WaitAsync` only for caller cancellation. RabbitMQ's fixed
publisher slots remain recoverable for later batches without retrying the earlier batch,
and make readiness unhealthy if none are usable. `Startup` owns one linked budget for
the complete startup sequence, and `HealthChecks` sets built-in readiness-registration
timeouts. Redis rate-limit timeout fails closed as HTTP 503 without publishing;
RabbitMQ publish timeout cannot return HTTP 202; startup timeout makes readiness failed
and stops startup. See
[ADR 0020](decisions/0020-use-explicit-dependency-timeout-budgets.md).

## Deferred Portfolio and Production Coverage

The following are intentional future learning and portfolio concerns, not rejected
requirements and not claims about the currently implemented architecture:

- liveness and readiness health checks;
- structured logging and correlation IDs;
- metrics, distributed tracing, and broader observability;
- authentication and authorization;
- rate limiting and request/input limits;
- resilience and retry policies where concrete failure behavior justifies them;
- Redis-backed distributed ingestion rate limiting when Stage 4 introduces multiple
  `PulseFlow.Api` instances; Redis is not planned as generic caching or batch-status
  storage;
- asynchronous/background processing and messaging when Stage 2 requires them;
- load testing and multi-instance behavior;
- graceful shutdown and operational behavior;
- CI/CD, AWS deployment, and production configuration and secrets.

Specific technologies and guarantees remain just-in-time decisions for the stages
that establish their requirements and verification criteria.

## Updating the Roadmap

After completing a meaningful step:

1. Update the stage status or expected result only when it actually changed.
2. Create a new checkpoint in `docs/progress/` with the starting point, changes, resulting state, verification results, decisions, unresolved items, and next recommended step.
3. Record architectural decisions in ADRs if genuine alternatives and consequences existed.
4. Do not rewrite earlier checkpoints except to correct factual errors; use a new checkpoint to explain useful changes in direction.
