# Portable runtime configuration

`infra/runtime` defines what runs on Linux hosts. It is provider-independent: it
contains no cloud infrastructure, provider CLI, instance identity, or provider-specific
deployment logic.

Each subdirectory is an independently deployed Docker Compose project for one Linux
host. Configuration, cross-host addresses, and credentials are supplied through the
host environment; no `.env` file or secret is committed here.

## Expected host mapping

| Runtime | AWS | Future providers |
| --- | --- | --- |
| `app` | app EC2 | application VM |
| `postgres` | postgres EC2 | PostgreSQL VM |
| `rabbitmq` | rabbitmq EC2 | RabbitMQ VM |
| `redis` | redis EC2 | Redis VM |

The same Compose definitions can run on Azure VMs or Proxmox/Linux VMs. The host
operator supplies the dependency hostnames or IP addresses and secrets through its
environment, then starts each project independently.

Validate a runtime definition without starting containers by exporting its documented
variables and running:

```powershell
docker compose -f infra/runtime/<component>/compose.yaml config
```

The application runtime depends on PostgreSQL, RabbitMQ, and Redis being reachable on
other hosts. Docker Compose startup ordering cannot verify those external dependencies;
the API readiness endpoint performs that runtime check.
