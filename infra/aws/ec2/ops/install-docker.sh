#!/usr/bin/env bash
set -euo pipefail

if [[ "$(id -u)" -eq 0 ]]; then
  sudo_command=()
else
  sudo_command=(sudo)
fi

if ! command -v docker >/dev/null 2>&1; then
  "${sudo_command[@]}" dnf install -y docker
fi

if ! command -v curl >/dev/null 2>&1; then
  "${sudo_command[@]}" dnf install -y curl
fi

"${sudo_command[@]}" systemctl enable --now docker

compose_version="v2.32.4"
compose_plugin_directory="/usr/local/lib/docker/cli-plugins"
compose_plugin_path="$compose_plugin_directory/docker-compose"
compose_download_path="$(mktemp /tmp/pulseflow-docker-compose.XXXXXX)"

trap 'rm -f "$compose_download_path"' EXIT

"${sudo_command[@]}" install -d -m 0755 "$compose_plugin_directory"
curl --fail --location --silent --show-error \
  "https://github.com/docker/compose/releases/download/$compose_version/docker-compose-linux-x86_64" \
  --output "$compose_download_path"
"${sudo_command[@]}" install -m 0755 "$compose_download_path" "$compose_plugin_path"

session_user="${SUDO_USER:-$(id -un)}"
if [[ "$session_user" == "root" ]] && id ssm-user >/dev/null 2>&1; then
  session_user="ssm-user"
fi

if [[ "$session_user" != "root" ]]; then
  "${sudo_command[@]}" usermod -aG docker "$session_user"
fi

"${sudo_command[@]}" docker version --format 'Docker server version: {{.Server.Version}}'
"${sudo_command[@]}" docker compose version

if [[ "$session_user" == "root" ]]; then
  echo "Docker is installed and running."
else
  echo "Docker is installed and running. Reconnect before using Docker as $session_user."
fi
