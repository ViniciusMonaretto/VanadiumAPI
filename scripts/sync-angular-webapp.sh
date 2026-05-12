#!/usr/bin/env bash
# Builds VanadiumWebApp (Angular) and copies the browser bundle into this API's wwwroot.
# Requires: Node.js/npm on PATH, dotnet SDK.
# Usage: ./scripts/sync-angular-webapp.sh
#        ./scripts/sync-angular-webapp.sh -- -v:m
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CSPROJ="$API_ROOT/VanadiumAPI.csproj"

if [[ ! -f "$CSPROJ" ]]; then
  echo "VanadiumAPI.csproj not found at $CSPROJ" >&2
  exit 1
fi

if [[ "${1:-}" == "--" ]]; then
  shift
fi

exec dotnet build "$CSPROJ" -p:BuildAngular=true "$@"
