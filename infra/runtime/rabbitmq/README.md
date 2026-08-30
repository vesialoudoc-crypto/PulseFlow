# RabbitMQ runtime

Runs one RabbitMQ 4.1 container for ingestion batches. The management UI and metrics
ports are not exposed by this portable runtime; the existing application uses AMQP on
port `5672`. Existing local performance tooling publishes the metrics port only from
`infra/local`; a portable metrics exposure needs a separate accepted operations
requirement.

## Required variables

- `PULSEFLOW_RABBITMQ_USER`
- `PULSEFLOW_RABBITMQ_PASSWORD`

The Compose configuration fails if either required variable is missing.

## Host port and storage

- Host port `5672` is published for the application host.
- Named volume `rabbitmq_data` persists broker data at `/var/lib/rabbitmq`.

## Validate

Export placeholder credentials in the current shell, then run:

```powershell
docker compose -f infra/runtime/rabbitmq/compose.yaml config
```
