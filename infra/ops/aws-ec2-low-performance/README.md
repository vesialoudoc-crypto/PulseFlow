# AWS EC2 Low-Performance Operations

Terraform creates clean hosts. This directory configures and operates
already-created hosts. Terraform never invokes this directory.

Use Session Manager to open an interactive shell on exactly one running host:

```powershell
./infra/ops/aws-ec2-low-performance/connect.ps1 app
./infra/ops/aws-ec2-low-performance/connect.ps1 rabbitmq
./infra/ops/aws-ec2-low-performance/connect.ps1 redis
./infra/ops/aws-ec2-low-performance/connect.ps1 postgres
```

The script needs AWS CLI v2, the Session Manager plugin, a configured AWS credential
context, and the correct selected AWS region. It finds a single `running` instance by
its exact `Name` tag (for example, `pulseflow-app`) and opens an SSM shell. It fails
instead of choosing when no matching running instance or more than one exists.

After connecting, transfer the reviewed `install-docker.sh` to the target through an
explicit operator-controlled SSM method, then run it as the Docker foundation:

```bash
bash install-docker.sh
```

The script installs Docker from Amazon Linux 2023 repositories, starts it, adds the
actual invoking non-root Session Manager user to the `docker` group, and prints the
Docker server version. Reconnect after it finishes so the new group membership applies.

The intended future runtime topology is:

```text
app EC2:       HAProxy, PulseFlow.Api #1, PulseFlow.Api #2
rabbitmq EC2:  RabbitMQ
redis EC2:     Redis
postgres EC2:  PostgreSQL
```

No Compose topology, service installation, image pull, credentials, migration, or
runtime deployment is part of this task.
