# EC2 operations implementation reference

The operations scripts act only on already-created, running EC2 hosts. Terraform never invokes them.

## Host discovery

The scripts require AWS CLI v2 and use the fixed `eu-central-1` region. They discover one running host for each exact `Name` tag:

| Role | Name tag |
| --- | --- |
| `app` | `pulseflow-app` |
| `postgres` | `pulseflow-postgres` |
| `rabbitmq` | `pulseflow-rabbitmq` |
| `redis` | `pulseflow-redis` |

Discovery fails if no host, more than one host, or no private IP is found. `connect.ps1` also requires the AWS Session Manager plugin.

## Script behavior

- `bootstrap.ps1` sends `install-docker.sh` through SSM Run Command, installs and starts Docker Engine, and installs Docker Compose v2.32.4 for x86_64 hosts.
- `copy-runtime.ps1` sends the role-matched Compose definition through SSM Run Command, then validates it with temporary placeholder values.
- `status.ps1` reports SSM state, Docker state, and whether the expected runtime directory exists.
- `connect.ps1` starts an interactive SSM shell.

The bootstrap and copy transport encode local file contents as Base64 in an SSM command, stage them in a temporary remote path, install them, and remove the temporary path. This transport does not deliver real secrets.

## Runtime destinations

- `/opt/pulseflow/runtime/app/` contains `compose.yaml` and `haproxy.cfg`.
- `/opt/pulseflow/runtime/postgres/`, `/opt/pulseflow/runtime/rabbitmq/`, and `/opt/pulseflow/runtime/redis/` each contain `compose.yaml`.

Directories are created as root and runtime files are readable. Runtime `.env` files are intentionally created manually as root with mode `0600`; deployment uses `sudo docker compose` so it can read them.

## Deliberate limits

This layer does not create AWS resources, use Terraform state or Terraform outputs, use SSH, configure AWS secret storage, transmit real credentials, pull images, run migrations, or start containers. It provides a repeatable host bootstrap and transport foundation, not an automated deployment system.
