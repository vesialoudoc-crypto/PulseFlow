#!/usr/bin/env bash
set -euo pipefail

cd /opt/pulseflow
docker compose pull
docker compose up -d --wait
