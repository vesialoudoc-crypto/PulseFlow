#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! $1 =~ ^[1-9][0-9]*$ ]]; then
  echo "Usage: run-baseline.sh <positive-virtual-user-count>" >&2
  exit 2
fi

cd /opt/pulseflow
docker compose pull k6
docker compose run --rm -e "VUS=$1" k6 run /scripts/ingestion-baseline.js
