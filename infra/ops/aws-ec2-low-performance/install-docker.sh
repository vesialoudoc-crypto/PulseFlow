#!/usr/bin/env bash
set -euo pipefail

session_user="${SUDO_USER:-$(id -un)}"
if [[ "$session_user" == "root" ]]; then
  echo "Run this script from a non-root Session Manager shell." >&2
  exit 1
fi

sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker "$session_user"

sudo docker version --format 'Docker server version: {{.Server.Version}}'

echo "Docker is installed and running. Reconnect before using Docker as $session_user."
