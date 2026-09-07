# AWS EC2 deployment manual

Use this manual to deploy the current PulseFlow EC2 environment by hand. It assumes you
are a backend developer, not an AWS specialist. Run each step in order, check the
result, and stop when told to stop. Do not put real passwords, tokens, instance IDs,
or public IP addresses into this file or another tracked file.

## The four machines

| Machine | What it does |
| --- | --- |
| App EC2 | Runs HAProxy and two PulseFlow API containers. |
| PostgreSQL EC2 | Runs the database that stores processed events. |
| RabbitMQ EC2 | Runs the queue between API acceptance and event processing. |
| Redis EC2 | Runs shared rate-limit state for the API replicas. |

The app connects to PostgreSQL, RabbitMQ, and Redis through their private IP addresses.
Those addresses work inside this AWS network and are not public internet addresses.

## Command locations

| Location | Meaning |
| --- | --- |
| **Local Windows PowerShell** | Your Windows PowerShell/Pwsh terminal, opened in the repository root. |
| **App EC2** | A Linux shell on the App EC2 machine. |
| **PostgreSQL EC2** | A Linux shell on the PostgreSQL EC2 machine. |
| **RabbitMQ EC2** | A Linux shell on the RabbitMQ EC2 machine. |
| **Redis EC2** | A Linux shell on the Redis EC2 machine. |

This repository opens Linux shells through AWS Systems Manager (SSM). SSM is the
approved way to reach EC2 without SSH keys, SSH ports, or public SSH access.

## 1. Check local tools and the AWS account

**Where:** Local Windows PowerShell, in the repository root.

**Why:** Terraform creates the AWS machines. AWS CLI and the Session Manager plugin
let the repository scripts find the machines and open SSM shells.

~~~powershell
terraform version
aws --version
session-manager-plugin --version
. ./scripts/aws/EnvFileLoader.ps1
aws sts get-caller-identity
~~~

The ignored repository-root .env holds local KEY=value settings. EnvFileLoader.ps1
loads them into this PowerShell session.

**Success:** Terraform reports version 1.6 or newer; AWS CLI reports aws-cli/2; the
Session Manager plugin reports a version; and get-caller-identity returns Account,
Arn, and UserId for the intended AWS account.

The EC2 operations scripts always use eu-central-1, the Frankfurt region. The AWS CLI
login above must be for the same AWS account Terraform will use. Terraform can use an
optional aws_profile setting, but the SSM scripts do not read it; setting it only in
terraform.tfvars does not change the AWS CLI login used by the scripts.

**Stop here:** Do not continue if a command fails, the account is wrong, or the region
is not eu-central-1.

## 2. Prepare Terraform inputs

**Where:** Local Windows PowerShell, in the repository root.

**Why:** Terraform needs the Linux image to install and the source CIDR allowed to
call the app over HTTP.

Open the ignored file infra/aws/ec2/terraform.tfvars. It must contain at least:

~~~hcl
ami_id                   = "ami-<current-amazon-linux-2023-x86_64-ami>"
allowed_http_source_cidr = "0.0.0.0/0"
~~~

Use a current Amazon Linux 2023 x86_64 AMI. For this disposable environment,
`0.0.0.0/0` is valid and exposes the app HTTP port publicly. You do not need to
discover or update an operator public IP when it changes. SSH remains closed, and
PostgreSQL, RabbitMQ, and Redis remain restricted to private VPC access. You may use a
narrower CIDR if needed, but it is optional.

Leave aws_region at its eu-central-1 default. If aws_profile is set, the AWS CLI login
from step 1 must still address the same account.

**Success:** The AMI and `0.0.0.0/0` source CIDR are present, or the operator has
intentionally supplied a narrower valid CIDR.

**Stop here:** Do not continue with a missing AMI, an invalid CIDR, or another region.

## 3. Initialize and review the AWS changes

**Where:** Local Windows PowerShell, in the repository root.

**Why:** init prepares Terraform locally. validate checks the configuration. plan shows
the AWS changes Terraform would make, but does not make them.

~~~powershell
terraform -chdir=infra/aws/ec2 init
terraform -chdir=infra/aws/ec2 validate
terraform -chdir=infra/aws/ec2 plan
~~~

Read the plan. It should show one network, minimal SSM permissions, and the four Linux
machines from the table above. It must not show unexpected resources, account, or
region.

**Success:** init and validate succeed and the plan matches the intended four-machine
environment.

**Stop here:** Do not run apply if any command fails or the plan is unexpected.

For source-only validation that must not initialize the configured backend, terraform
init -backend=false is an optional separate check. Do not use it for this deployment.

## 4. Create the AWS environment

**Where:** Local Windows PowerShell, in the repository root.

**Why:** apply performs the reviewed plan and creates the AWS network, permissions, and
four clean Linux machines.

~~~powershell
terraform -chdir=infra/aws/ec2 apply
terraform -chdir=infra/aws/ec2 output instance_ids
terraform -chdir=infra/aws/ec2 output instance_private_ips
terraform -chdir=infra/aws/ec2 output instance_public_ips
~~~

Read the apply confirmation and type yes only after confirming it is the plan from
step 3. Keep the private IP output for the app configuration. Use only the app public
IP for external HTTP checks. Every new deployment gets new addresses.

**Success:** Terraform reports Apply complete and each output has app, postgres,
rabbitmq, and redis values.

**Stop here:** Do not use the machines if apply fails or an expected output is missing.

## 5. Wait for SSM

**Where:** Local Windows PowerShell, in the repository root.

**Why:** A machine must be Online in SSM before scripts can install Docker, copy files,
or open its shell.

~~~powershell
pwsh ./infra/aws/ec2/ops/status.ps1
~~~

Run it again after a short wait when a new machine is not yet Online. The script finds
the machines by their exact names in eu-central-1.

**Success:** app, postgres, rabbitmq, and redis all show Ssm as Online. Before Docker
installation, Docker can show not-installed and RuntimeDirectory can show missing.

**Stop here:** Do not bootstrap or connect to a machine that is not Online.

## 6. Install Docker on all machines

**Where:** Local Windows PowerShell, in the repository root.

**Why:** Each service runs as a Docker Compose project, so every machine needs Docker
Engine and the Docker Compose plugin.

~~~powershell
pwsh ./infra/aws/ec2/ops/bootstrap.ps1 all
pwsh ./infra/aws/ec2/ops/status.ps1
~~~

**Success:** bootstrap succeeds for all roles and confirms Docker Engine and docker
compose. status.ps1 shows Docker as running for all four machines.

**Stop here:** Do not copy runtime files or open deployment shells if bootstrap fails
for any role.

## 7. Copy the runtime files

**Where:** Local Windows PowerShell, in the repository root.

**Why:** The reviewed files in infra/runtime define which containers run on each
machine. This step copies the matching files through SSM.

~~~powershell
pwsh ./infra/aws/ec2/ops/copy-runtime.ps1 all
pwsh ./infra/aws/ec2/ops/status.ps1
~~~

The directories are /opt/pulseflow/runtime/app, /opt/pulseflow/runtime/postgres,
/opt/pulseflow/runtime/rabbitmq, and /opt/pulseflow/runtime/redis. The script creates
them as root with mode 0755. It copies compose.yaml everywhere and haproxy.cfg to App
EC2.

**Success:** copy-runtime.ps1 succeeds for every role and status.ps1 reports
RuntimeDirectory as present for every role.

**Stop here:** Do not create .env files or start containers if a directory is missing.

## 8. Start PostgreSQL

**Where:** Run the PowerShell command on Local Windows PowerShell. Run the Bash block
on PostgreSQL EC2.

**Why:** The database must be healthy before migrations and APIs can use it.

~~~powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 postgres
~~~

SSM shells normally use ssm-user, but the runtime directory belongs to root. Create
the .env file as root, mode 600, and use sudo docker compose so Compose can read it.
Generate passwords using only ASCII letters, digits, underscores, and hyphens. The
current runtime does not encode arbitrary characters for Compose, PostgreSQL connection
strings, or RabbitMQ URIs.

~~~bash
cd /opt/pulseflow/runtime/postgres
sudo install -o root -g root -m 0600 /dev/null .env
sudo tee .env >/dev/null <<'EOF'
# PostgreSQL runtime configuration. Replace the password placeholder.
PULSEFLOW_POSTGRES_DB=pulseflow
PULSEFLOW_POSTGRES_USER=pulseflow
PULSEFLOW_POSTGRES_PASSWORD=<generated-safe-postgresql-password>
EOF
sudo chmod 600 .env
sudo docker compose config
sudo docker compose up -d postgres
sudo docker compose ps
sudo docker inspect --format '{{.State.Health.Status}}' "$(sudo docker compose ps -q postgres)"
~~~

Keep the database name, user, and password for the app .env file.

**Success:** Compose validation succeeds, postgres is running, and the final command
prints healthy.

**Stop here:** Do not start other services if PostgreSQL is not healthy.

## 9. Start RabbitMQ

**Where:** Run the PowerShell command on Local Windows PowerShell. Run the Bash block
on RabbitMQ EC2.

**Why:** RabbitMQ receives event batches from the API and allows processing to happen
after the HTTP request.

~~~powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 rabbitmq
~~~

Use a password with the safe character set from step 8. Use the same user and password
later on App EC2.

~~~bash
cd /opt/pulseflow/runtime/rabbitmq
sudo install -o root -g root -m 0600 /dev/null .env
sudo tee .env >/dev/null <<'EOF'
# RabbitMQ runtime configuration. Replace the password placeholder.
PULSEFLOW_RABBITMQ_USER=pulseflow
PULSEFLOW_RABBITMQ_PASSWORD=<generated-safe-rabbitmq-password>
EOF
sudo chmod 600 .env
sudo docker compose config
sudo docker compose up -d rabbitmq
sudo docker compose ps
sudo docker inspect --format '{{.State.Health.Status}}' "$(sudo docker compose ps -q rabbitmq)"
~~~

**Success:** Compose validation succeeds, rabbitmq is running, and the final command
prints healthy.

**Stop here:** Do not start the app if RabbitMQ is not healthy.

## 10. Start Redis

**Where:** Run the PowerShell command on Local Windows PowerShell. Run the Bash block
on Redis EC2.

**Why:** Both API replicas use Redis for shared rate-limit state.

~~~powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 redis
~~~

Redis has no .env file or password in the current runtime. Its security group permits
port 6379 only from App EC2.

~~~bash
cd /opt/pulseflow/runtime/redis
sudo docker compose config
sudo docker compose up -d redis
sudo docker compose ps
sudo docker inspect --format '{{.State.Health.Status}}' "$(sudo docker compose ps -q redis)"
~~~

**Success:** Compose validation succeeds, redis is running, and the final command
prints healthy.

**Stop here:** Do not start the app if Redis is not healthy.

## 11. Configure App EC2 and download the API image

**Where:** Get addresses and open the shell on Local Windows PowerShell. Run the Bash
blocks on App EC2.

**Why:** The app needs private addresses and credentials for its three dependencies.
It also needs a GHCR login because the API image may not be publicly downloadable.

~~~powershell
terraform -chdir=infra/aws/ec2 output instance_private_ips
pwsh ./infra/aws/ec2/ops/connect.ps1 app
~~~

Use current private IPs and the PostgreSQL/RabbitMQ values from steps 8 and 9. Use an
immutable SHA tag for the API image.

~~~bash
cd /opt/pulseflow/runtime/app
sudo install -o root -g root -m 0600 /dev/null .env
sudo tee .env >/dev/null <<'EOF'
# App EC2 runtime configuration. Replace every placeholder.
PULSEFLOW_IMAGE=ghcr.io/<repository-owner>/pulseflow-api:sha-<40-character-commit-sha>

# PostgreSQL dependency
PULSEFLOW_POSTGRES_HOST=<current-postgres-private-ip>
PULSEFLOW_POSTGRES_DB=pulseflow
PULSEFLOW_POSTGRES_USER=pulseflow
PULSEFLOW_POSTGRES_PASSWORD=<generated-safe-postgresql-password>

# RabbitMQ dependency
PULSEFLOW_RABBITMQ_HOST=<current-rabbitmq-private-ip>
PULSEFLOW_RABBITMQ_USER=pulseflow
PULSEFLOW_RABBITMQ_PASSWORD=<generated-safe-rabbitmq-password>

# Redis dependency
PULSEFLOW_REDIS_HOST=<current-redis-private-ip>
EOF
sudo chmod 600 .env
sudo docker compose config
~~~

Enter the GHCR token only at the hidden prompt. It goes through standard input, not
shell history or .env. sudo is required because sudo docker compose pull uses the root
Docker client.

~~~bash
read -r -s -p 'GHCR token: ' GHCR_TOKEN; echo
printf '%s' "$GHCR_TOKEN" | sudo docker login ghcr.io --username '<github-username>' --password-stdin
unset GHCR_TOKEN
sudo docker compose pull
~~~

**Success:** Compose validation succeeds, docker login says Login Succeeded, and the
image pull completes.

**Stop here:** Do not run migrations if configuration, login, or image pull fails.

## 12. Apply database migrations

**Where:** App EC2.

**Why:** Migrations create or update the database schema before the APIs start. The
APIs only check for pending migrations; they never apply them themselves.

~~~bash
cd /opt/pulseflow/runtime/app
sudo docker compose up migrations
sudo docker compose ps -a migrations
sudo docker inspect --format '{{.State.ExitCode}}' "$(sudo docker compose ps -aq migrations)"
~~~

The migrations service runs the image bundle with an explicit connection built from
the PostgreSQL values in .env.

**Success:** The migration command exits successfully, ps -a shows the completed
migrations service, and the final command prints 0.

**Stop here:** Do not start either API when migrations fail or the exit code is not 0.

## 13. Start the APIs and HAProxy

**Where:** App EC2.

**Why:** The two API containers handle requests. HAProxy listens on port 80 and sends
requests only to ready API containers.

~~~bash
cd /opt/pulseflow/runtime/app
sudo docker compose up -d api-1 api-2
sudo docker compose up -d haproxy
sudo docker compose ps
~~~

**Success:** api-1, api-2, and haproxy are running.

**Stop here:** Do not test readiness when an expected container is not running.

## 14. Check readiness

**Where:** First command on App EC2. Second block on Local Windows PowerShell.

**Why:** The internal check proves HAProxy can reach ready APIs. The external check
proves the app public endpoint can be reached over HTTP.

On App EC2:

~~~bash
curl --fail --show-error --silent --write-out '\nHTTP %{http_code}\n' http://127.0.0.1/health/ready
~~~

On Local Windows PowerShell:

~~~powershell
$appPublicIp = (terraform -chdir=infra/aws/ec2 output -json instance_public_ips | ConvertFrom-Json).app
$readiness = Invoke-WebRequest -Uri "http://$appPublicIp/health/ready"
$readiness.StatusCode
$readiness.Content
~~~

**Success:** Both checks return HTTP 200 and Healthy. With the default CIDR, the
external check requires no operator public-IP configuration.

**Stop here:** Do not submit a smoke event if either check fails.

## 15. Submit one smoke event

**Where:** Local Windows PowerShell.

**Why:** This sends one new Event Contract v2 event through the public endpoint and
proves the client can reach HAProxy and the API can publish the batch to RabbitMQ.

~~~powershell
$appPublicIp = (terraform -chdir=infra/aws/ec2 output -json instance_public_ips | ConvertFrom-Json).app
$smokeEventId = [guid]::NewGuid().ToString()
$smokeSource = 'aws-ec2-runbook-smoke'
$smokeEvent = @{
    eventId = $smokeEventId
    type = 'deployment.smoke'
    source = $smokeSource
    occurredAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    payload = @{ proof = 'aws-ec2-manual-runbook' }
} | ConvertTo-Json -Compress
$response = Invoke-WebRequest -Method Post -Uri "http://$appPublicIp/api/events" -ContentType 'application/x-ndjson' -Body ($smokeEvent + [Environment]::NewLine)
$response.StatusCode
$smokeEventId
$smokeSource
~~~

Keep the printed event ID and source for the next steps.

**Success:** The status code is 202 Accepted.

HTTP 202 is not proof that PostgreSQL already contains the event. It means RabbitMQ
accepted the raw batch; a worker must still read, validate, and store it.

**Stop here:** Do not claim complete deployment proof when the response is not 202.

## 16. Wait for the exact event in PostgreSQL

**Where:** Run the PowerShell command on Local Windows PowerShell. Run the Bash block
on PostgreSQL EC2.

**Why:** This proves the full path: external client, HAProxy, API, RabbitMQ processing,
and PostgreSQL persistence.

~~~powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 postgres
~~~

Replace the event-ID placeholder with the value printed in step 15. This checks only
the exact source and event ID for at most 30 seconds.

~~~bash
cd /opt/pulseflow/runtime/postgres
SMOKE_EVENT_ID='<event-id-printed-by-local-powershell>'
SMOKE_SOURCE='aws-ec2-runbook-smoke'
found=false
for attempt in $(seq 1 30); do
  found=$(sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow \
    -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" \
    -tAc "SELECT EXISTS (SELECT 1 FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid);")
  if [ "$found" = "t" ]; then
    break
  fi
  sleep 1
done
if [ "$found" != "t" ]; then
  echo "Smoke event was not persisted within 30 seconds; stopping proof." >&2
  exit 1
fi
sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow \
  -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" \
  -c "SELECT id, event_id, source, type, occurred_at, received_at, payload FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid;"
~~~

**Success:** The event appears within 30 seconds and the final query returns exactly
one row with the submitted source and event ID.

**Stop here:** The script exits when the event never appears. Investigate instead of
continuing to cleanup or claiming a persistence proof.

## 17. Remove only the smoke event

**Where:** PostgreSQL EC2.

**Why:** The smoke event is test data. Delete only the exact row from step 16; never
delete all events or truncate the table.

~~~bash
sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow \
  -v ON_ERROR_STOP=1 -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" \
  -c "DELETE FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid RETURNING id, event_id, source;"
sudo docker compose exec -T postgres psql -U pulseflow -d pulseflow \
  -v event_id="$SMOKE_EVENT_ID" -v source="$SMOKE_SOURCE" \
  -c "SELECT count(*) AS remaining_smoke_rows FROM events WHERE source = :'source' AND event_id = :'event_id'::uuid;"
~~~

**Success:** The delete returns the exact smoke row and the final count is 0.

**Stop here:** Do not destroy the environment when the count is not 0. Recheck the
source and event ID before any other database change.

## 18. Optionally stop containers

**Where:** Run the matching block on each named EC2 machine.

**Why:** This is optional operational cleanup before AWS removal. Terraform does not
manage these containers and does not require this step.

On App EC2:

~~~bash
cd /opt/pulseflow/runtime/app
sudo docker compose ps
sudo docker compose down
~~~

On PostgreSQL EC2:

~~~bash
cd /opt/pulseflow/runtime/postgres
sudo docker compose ps
sudo docker compose down
~~~

On RabbitMQ EC2:

~~~bash
cd /opt/pulseflow/runtime/rabbitmq
sudo docker compose ps
sudo docker compose down
~~~

On Redis EC2:

~~~bash
cd /opt/pulseflow/runtime/redis
sudo docker compose ps
sudo docker compose down
~~~

**Success:** Each selected project reports its containers and stops them. Named data
volumes stay until the machine itself is removed.

**Stop here:** Investigate a container that cannot stop before destroying its machine.

## 19. Destroy the AWS environment

**Where:** Local Windows PowerShell, in the repository root.

**Why:** This disposable environment costs money while its AWS machines exist.
terraform destroy removes the AWS resources created by Terraform.

~~~powershell
. ./scripts/aws/EnvFileLoader.ps1
terraform -chdir=infra/aws/ec2 plan -destroy
terraform -chdir=infra/aws/ec2 destroy
terraform -chdir=infra/aws/ec2 state list
~~~

plan -destroy shows the deletion without deleting. destroy performs that reviewed
deletion. state list shows which AWS resources this Terraform directory still tracks.

**Success:** Terraform reports Destroy complete and terraform state list produces no
output.

An empty state list confirms this Terraform directory no longer tracks resources from
this deployment. It does not audit every AWS resource in the account.

**Stop here:** If destroy fails or state list is not empty, cleanup is not complete.
Read the error, resolve the remaining tracked resource, and review the destroy plan
again.

## What this manual does not automate

- Terraform creates and removes AWS networking, permissions, and clean EC2 machines.
- The ops scripts use SSM to install Docker, copy runtime files, and open shells.
- The operator supplies credentials and creates runtime .env files.
- Compose files define containers; they do not create AWS resources.
- Terraform does not install Docker, deliver secrets, pull images, run migrations,
  start containers, or run smoke tests.
