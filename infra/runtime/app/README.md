# Application runtime

Runs HAProxy, two equivalent PulseFlow API replicas, and a one-shot database migration
service on one Linux application host. It uses the immutable GHCR image supplied by
`PULSEFLOW_IMAGE`; it never builds the API on that host.

The migration service overrides the image entrypoint with
`/app/migrations/pulseflow-migrations`. Both API replicas wait for that service to
exit successfully. HAProxy is the only application ingress and routes only to ready
API replicas.

## Required variables

- `PULSEFLOW_IMAGE`
- `PULSEFLOW_POSTGRES_HOST`
- `PULSEFLOW_POSTGRES_DB`
- `PULSEFLOW_POSTGRES_USER`
- `PULSEFLOW_POSTGRES_PASSWORD`
- `PULSEFLOW_RABBITMQ_HOST`
- `PULSEFLOW_RABBITMQ_USER`
- `PULSEFLOW_RABBITMQ_PASSWORD`
- `PULSEFLOW_REDIS_HOST`

Dependency hosts may be DNS names or IP addresses. PostgreSQL, RabbitMQ, and Redis
use their standard ports `5432`, `5672`, and `6379` respectively. The Compose file
constructs the existing application connection-string settings from these variables
and preserves the established queue, publisher, ingestion, and rate-limit values.

## Host port and storage

- Host port `80` is published to HAProxy container port `8080`.
- API containers publish no host ports.
- This runtime has no persistent named volume; durable data belongs to the PostgreSQL,
  RabbitMQ, and Redis runtimes.

## Validate

Export placeholder values in the current shell, then run:

```powershell
docker compose -f infra/runtime/app/compose.yaml config
```
