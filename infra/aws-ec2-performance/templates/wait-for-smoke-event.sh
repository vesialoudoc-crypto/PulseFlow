#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! $1 =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$ ]]; then
  echo "Usage: wait-for-smoke-event.sh <lowercase-uuid-v1-to-v5>" >&2
  exit 2
fi

readonly event_id="$1"
readonly deadline_epoch="$((SECONDS + 180))"

cd /opt/pulseflow
source .env

while true; do
  count="$(docker compose exec -T postgres psql -U "$POSTGRES_USERNAME" -d pulseflow -tAc "SELECT COUNT(*) FROM events WHERE source = 'ec2-smoke' AND event_id = '$event_id';")"
  count="${count//[[:space:]]/}"

  if [[ "$count" == "1" ]]; then
    echo "$count"
    exit 0
  fi

  if (( SECONDS >= deadline_epoch )); then
    echo "Smoke event $event_id was not persisted within three minutes." >&2
    exit 1
  fi

  sleep 2
done
