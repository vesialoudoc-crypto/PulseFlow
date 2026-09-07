# EC2 operations and runtime deployment

Start here only after Terraform reports `Apply complete!` in [the EC2 guide](../README.md). For implementation details, see [INFO.md](INFO.md).

Run PowerShell commands from the repository root. Run Bash commands in the named EC2 host's SSM shell.

## 1. Confirm all hosts are reachable

```powershell
pwsh ./infra/aws/ec2/ops/status.ps1
```

Expected: `app`, `postgres`, `rabbitmq`, and `redis` show `Ssm` as `Online`. Retry after a short wait if a new host is not online.

## 2. Install Docker and Docker Compose

```powershell
pwsh ./infra/aws/ec2/ops/bootstrap.ps1 all
pwsh ./infra/aws/ec2/ops/status.ps1
```

Expected: bootstrap succeeds for every role; status shows Docker as `running` on all four hosts.

## 3. Copy the runtime definitions

```powershell
pwsh ./infra/aws/ec2/ops/copy-runtime.ps1 all
pwsh ./infra/aws/ec2/ops/status.ps1
```

Expected: copy succeeds for every role; status shows `RuntimeDirectory` as `present` on all four hosts.

## 4. Start PostgreSQL

Open its SSM shell:

```powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 postgres
```

On PostgreSQL EC2, replace the password placeholder with a new password containing only ASCII letters, digits, underscores, and hyphens. Keep these database values for step 7.

```bash
cd /opt/pulseflow/runtime/postgres
sudo install -o root -g root -m 0600 /dev/null .env
sudo tee .env >/dev/null <<'EOF'
PULSEFLOW_POSTGRES_DB=pulseflow
PULSEFLOW_POSTGRES_USER=pulseflow
PULSEFLOW_POSTGRES_PASSWORD=<generated-safe-postgresql-password>
EOF
sudo docker compose config
sudo docker compose up -d postgres
sudo docker inspect --format '{{.State.Health.Status}}' "$(sudo docker compose ps -q postgres)"
```

Expected: the final command prints `healthy`.

## 5. Start RabbitMQ

Open its SSM shell:

```powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 rabbitmq
```

On RabbitMQ EC2, replace the password placeholder with a new safe password. Keep these broker values for step 7.

```bash
cd /opt/pulseflow/runtime/rabbitmq
sudo install -o root -g root -m 0600 /dev/null .env
sudo tee .env >/dev/null <<'EOF'
PULSEFLOW_RABBITMQ_USER=pulseflow
PULSEFLOW_RABBITMQ_PASSWORD=<generated-safe-rabbitmq-password>
EOF
sudo docker compose config
sudo docker compose up -d rabbitmq
sudo docker inspect --format '{{.State.Health.Status}}' "$(sudo docker compose ps -q rabbitmq)"
```

Expected: the final command prints `healthy`.

## 6. Start Redis

Open its SSM shell:

```powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 redis
```

On Redis EC2:

```bash
cd /opt/pulseflow/runtime/redis
sudo docker compose config
sudo docker compose up -d redis
sudo docker inspect --format '{{.State.Health.Status}}' "$(sudo docker compose ps -q redis)"
```

Expected: the final command prints `healthy`.

## 7. Configure and start the application host

Get the dependency addresses, then open the app shell:

```powershell
terraform -chdir=infra/aws/ec2 output instance_private_ips
pwsh ./infra/aws/ec2/ops/connect.ps1 app
```

On App EC2, replace every placeholder. Use the current private IPs and the credentials created in steps 4 and 5. Use an immutable `sha-<40-character-commit-sha>` GHCR image tag.

```bash
cd /opt/pulseflow/runtime/app
sudo install -o root -g root -m 0600 /dev/null .env
sudo tee .env >/dev/null <<'EOF'
PULSEFLOW_IMAGE=ghcr.io/<repository-owner>/pulseflow-api:sha-<40-character-commit-sha>
PULSEFLOW_POSTGRES_HOST=<current-postgres-private-ip>
PULSEFLOW_POSTGRES_DB=pulseflow
PULSEFLOW_POSTGRES_USER=pulseflow
PULSEFLOW_POSTGRES_PASSWORD=<generated-safe-postgresql-password>
PULSEFLOW_RABBITMQ_HOST=<current-rabbitmq-private-ip>
PULSEFLOW_RABBITMQ_USER=pulseflow
PULSEFLOW_RABBITMQ_PASSWORD=<generated-safe-rabbitmq-password>
PULSEFLOW_REDIS_HOST=<current-redis-private-ip>
EOF
sudo docker compose config
```

Log in to GHCR. Enter a GitHub Packages read token at the hidden prompt; do not put it in `.env` or shell history.

```bash
read -r -s -p 'GHCR token: ' GHCR_TOKEN; echo
printf '%s' "$GHCR_TOKEN" | sudo docker login ghcr.io --username '<github-username>' --password-stdin
unset GHCR_TOKEN
sudo docker compose pull
```

Expected: `Login Succeeded` and the image pull completes.

Run the migrations before starting either API container:

```bash
sudo docker compose up migrations
sudo docker inspect --format '{{.State.ExitCode}}' "$(sudo docker compose ps -aq migrations)"
```

Expected: the final command prints `0`.

Start the two APIs and HAProxy:

```bash
sudo docker compose up -d api-1 api-2
sudo docker compose up -d haproxy
sudo docker compose ps
```

Expected: `api-1`, `api-2`, and `haproxy` are running.

## 8. Verify readiness

On App EC2:

```bash
curl --fail --show-error --silent --write-out '\nHTTP %{http_code}\n' http://127.0.0.1/health/ready
```

From local PowerShell:

```powershell
$appPublicIp = (terraform -chdir=infra/aws/ec2 output -json instance_public_ips | ConvertFrom-Json).app
$readiness = Invoke-WebRequest -Uri "http://$appPublicIp/health/ready"
$readiness.StatusCode
$readiness.Content
```

Expected: both checks return HTTP 200 and `Healthy`.

## 9. Prove ingestion reaches PostgreSQL

From local PowerShell, submit one unique smoke event and keep the printed ID:

```powershell
$appPublicIp = (terraform -chdir=infra/aws/ec2 output -json instance_public_ips | ConvertFrom-Json).app
$smokeEventId = [guid]::NewGuid().ToString()
$smokeSource = 'aws-ec2-readme-smoke'
$smokeEvent = @{
    eventId = $smokeEventId
    type = 'deployment.smoke'
    source = $smokeSource
    occurredAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    payload = @{ proof = 'aws-ec2-readme' }
} | ConvertTo-Json -Compress
(Invoke-WebRequest -Method Post -Uri "http://$appPublicIp/api/events" -ContentType 'application/x-ndjson' -Body ($smokeEvent + [Environment]::NewLine)).StatusCode
$smokeEventId
```

Expected: HTTP 202. This confirms RabbitMQ accepted the batch; wait for persistence next.

Open PostgreSQL EC2:

```powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 postgres
```

On PostgreSQL EC2, replace `<event-id-from-step-9>` with the printed value:

```bash
cd /opt/pulseflow/runtime/postgres
SMOKE_EVENT_ID='<event-id-from-step-9>'
SMOKE_SOURCE='aws-ec2-readme-smoke'
for attempt in $(seq 1 30); do
  found=$(sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" -tAc "SELECT EXISTS (SELECT 1 FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid);")
  [ "$found" = "t" ] && break
  sleep 1
done
test "$found" = "t"
sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" -c "SELECT id, event_id, source, type, occurred_at, received_at FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid;"
```

Expected: the event appears within 30 seconds. Remove only this smoke-test row:

```bash
sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow -v ON_ERROR_STOP=1 -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" -c "DELETE FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid RETURNING id, event_id, source;"
sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" -c "SELECT count(*) AS remaining_smoke_rows FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid;"
```

Expected: the final count is `0`.

## Manual steps in this stage

The repository has no automation for runtime secret delivery, GHCR authentication, creating `.env` files, running migrations, starting the Compose projects, or the smoke test. Perform the commands above manually through SSM.
