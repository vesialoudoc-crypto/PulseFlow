#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! $1 =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$ ]]; then
  echo "Usage: run-smoke.sh <lowercase-uuid-v1-to-v5>" >&2
  exit 2
fi

cd /opt/pulseflow
docker compose pull k6
docker compose run --rm -e "SMOKE_EVENT_ID=$1" k6 run /scripts/smoke.js
