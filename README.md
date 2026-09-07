# PulseFlow

PulseFlow is a production-like ASP.NET Core / .NET 10 backend portfolio and pet
project for asynchronous event ingestion. Its deliberately small domain keeps the
focus on backend engineering: persistence, messaging, failure handling, multiple
API instances, testing, and reproducible infrastructure experiments.

Clients submit NDJSON batches to `POST /api/events`. The API returns `202 Accepted`
after RabbitMQ confirms publication; background consumers then parse, validate,
and persist valid events. HTTP acceptance confirms broker handoff, not completed
database persistence. Results are verified directly in PostgreSQL.

## What it demonstrates

- **PostgreSQL / EF Core:** migrations, chunked transactional persistence, and JSON
  payload storage.
- **RabbitMQ asynchronous processing:** publisher confirmations, competing consumers,
  acknowledgements after successful processing, and dead-letter handling for
  unexpected processing failures.
- **Event-level idempotency:** a unique `(source, eventId)` identity makes duplicate
  delivery and replay safe for already-persisted events, including concurrent inserts.
- **Redis distributed rate limiting:** API replicas share a global quota; excess
  requests receive `429`, and Redis failures fail closed with `503`.
- **Health and readiness:** separate liveness and readiness endpoints, startup
  initialization, dependency checks, and explicit timeout budgets.
- **Docker and HAProxy:** containerized dependencies and two API replicas behind
  health-aware ingress, with portable runtime definitions for separate hosts.
- **Verification:** unit tests and Testcontainers integration tests against real
  PostgreSQL, RabbitMQ, and Redis; k6 tooling and recorded local performance reports.
- **Engineering decisions:** ADRs explain accepted choices and trade-offs;
  chronological checkpoints record implementation and verification evidence.

## Architecture at a glance

```text
Client
  |
  v
HAProxy
  +--> PulseFlow.Api #1 --+
  +--> PulseFlow.Api #2 --+--> RabbitMQ
                         +--> Redis
                         +--> PostgreSQL
```

Both API processes also host RabbitMQ consumers. The diagram shows shared
dependencies; event persistence happens in the asynchronous consumer path.

## AWS deployment proof

PulseFlow was actually deployed and verified on AWS. The
[EC2 lifecycle record](docs/progress/2026-08-31-088-record-successful-ec2-lifecycle-proof.md)
documents Terraform provisioning of four hosts, successful EF migrations,
internal and public readiness checks, an external ingestion request returning
`202 Accepted`, and verification of that exact event in PostgreSQL. Terraform
destroy removed all 16 managed resources, and the state was empty afterward.

An earlier [managed-AWS deployment](docs/progress/2026-08-26-073-finalize-first-aws-deployment-lifecycle-proof.md)
also completed verification and teardown; EC2 is the active infrastructure path.
AWS performance measurements and deployed observability remain pending. Existing
performance reports describe local experiments, not deployed capacity guarantees.

## Development workflow

Development has used feature branches and pull requests. GitHub Actions provides
[build, tests, and formatting checks](.github/workflows/ci.yml); the
[image publishing workflow](.github/workflows/publish-api-image.yml) also validates
project documentation and publishes Docker images to GitHub Container Registry
with full commit-SHA tags.

CI runs automatically for pushes and pull requests. Image publishing remains
manually triggered to control GitHub Actions runner usage and cost. EC2 runtime
deployment remains an explicit operator procedure; automated deployment is still
pending.

## Explore

- [Ingestion architecture](docs/architecture/ingestion.md) and
  [architecture documents](docs/architecture/).
- [AWS infrastructure and deployment entry point](infra/aws/README.md),
  [runtime operations](infra/aws/ec2/ops/README.md), and
  [deployment runbook](docs/runbooks/aws-ec2-deployment.md).
- [Architectural decision records](docs/decisions/).
- [Unit tests](tests/PulseFlow.UnitTests/) and
  [integration tests](tests/PulseFlow.IntegrationTests/).
- [Performance tooling and run instructions](tests/performance/README.md) and
  [local measurement reports](docs/performance/).
- [Roadmap and remaining work](docs/02_ROADMAP.md).
