# PostgreSQL runtime

Runs one PostgreSQL 18.4 container for the durable event store.

## Required variables

- `PULSEFLOW_POSTGRES_DB`
- `PULSEFLOW_POSTGRES_USER`
- `PULSEFLOW_POSTGRES_PASSWORD`

The Compose configuration fails if any required variable is missing.

## Host port and storage

- Host port `5432` is published for the application host.
- Named volume `postgres_data` persists PostgreSQL data at
  `/var/lib/postgresql`.

## Validate

Export placeholder values in the current shell, then run:

```powershell
docker compose -f infra/runtime/postgres/compose.yaml config
```
