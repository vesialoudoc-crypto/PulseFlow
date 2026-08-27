#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! $1 =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$ ]]; then
  echo "Usage: cleanup-smoke-event.sh <lowercase-uuid-v1-to-v5>" >&2
  exit 2
fi

readonly event_id="$1"

cd /opt/pulseflow
source .env
docker compose exec -T postgres psql -U "$POSTGRES_USERNAME" -d pulseflow -v ON_ERROR_STOP=1 -c "DELETE FROM events WHERE source = 'ec2-smoke' AND event_id = '$event_id';"
