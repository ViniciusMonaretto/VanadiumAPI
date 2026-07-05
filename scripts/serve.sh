#!/usr/bin/env bash
# Build Angular into wwwroot, then run VanadiumAPI (API + SPA + SignalR on one port).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

"${SCRIPT_DIR}/build-web.sh"

cd "${API_DIR}"
echo "==> Starting VanadiumAPI..."
exec dotnet run --project "${API_DIR}/VanadiumAPI.csproj"
