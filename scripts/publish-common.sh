#!/usr/bin/env bash
# Shared helpers for publish-linux.sh / publish-windows.sh
set -euo pipefail

publish_vanadium_api() {
  local runtime="$1"
  local output_dir="$2"
  local api_dir="$3"
  local csproj="${api_dir}/VanadiumAPI.csproj"

  echo "==> dotnet publish (${runtime})"
  echo "    Output: ${output_dir}"

  dotnet publish "${csproj}" \
    -c Release \
    -r "${runtime}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "${output_dir}"

  cat > "${output_dir}/README-DEPLOY.txt" <<EOF
VanadiumAPI deployment (${runtime})
===================================

Run:
  Linux:   ./VanadiumAPI
  Windows: VanadiumAPI.exe

Dashboard + API + SignalR hub on the port in appsettings.json (default http://localhost:5010).

Requirements on target machine:
  - MongoDB reachable (Mongo:ConnectionString in appsettings.json)
  - MQTT broker if using live gateways (MqttOptions in appsettings.json)
  - SQLite app.db is created on first run next to the executable

Files in this folder:
  - VanadiumAPI executable (+ native deps if not single-file)
  - wwwroot/          Angular dashboard
  - appsettings.json  Server URL, Mongo, JWT, CORS
  - config.json       Optional seed data for empty SQLite DB

Build date: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
EOF
}
