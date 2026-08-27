#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! $1 =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$ ]]; then
  echo "Usage: cleanup-smoke-event.sh <lowercase-uuid-v1-to-v5>" >&2
  exit 2
fi

readonly event_id="$1"

cd /opt/pulseflow
source .env
deleted_count="$(docker compose exec -T postgres psql -U "$POSTGRES_USERNAME" -d pulseflow -v ON_ERROR_STOP=1 -tAc "WITH deleted AS (DELETE FROM events WHERE source = 'ec2-smoke' AND event_id = '$event_id' RETURNING 1) SELECT COUNT(*) FROM deleted;")"
deleted_count="${deleted_count//[[:space:]]/}"

if [[ "$deleted_count" != "1" ]]; then
  echo "Smoke cleanup expected to delete exactly one event, but deleted '$deleted_count'." >&2
  exit 1
fi

remaining_count="$(docker compose exec -T postgres psql -U "$POSTGRES_USERNAME" -d pulseflow -v ON_ERROR_STOP=1 -tAc "SELECT COUNT(*) FROM events WHERE source = 'ec2-smoke' AND event_id = '$event_id';")"
remaining_count="${remaining_count//[[:space:]]/}"

if [[ "$remaining_count" != "0" ]]; then
  echo "Smoke cleanup expected no matching PostgreSQL rows after deletion, but found '$remaining_count'." >&2
  exit 1
fi

echo "Smoke event was deleted and no matching PostgreSQL row remains."
