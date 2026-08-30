# Redis runtime

Runs one Redis 8.4 container for shared ingestion-rate-limit state. This portable
runtime keeps Redis unauthenticated because the existing application configuration
uses network access control rather than Redis credentials.

## Required variables

None.

## Host port and storage

- Host port `6379` is published for the application host.
- Named volume `redis_data` persists Redis data at `/data`.

## Validate

```powershell
docker compose -f infra/runtime/redis/compose.yaml config
```
