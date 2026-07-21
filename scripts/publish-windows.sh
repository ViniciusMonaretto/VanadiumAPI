#!/usr/bin/env bash
# Cross-compile a self-contained Windows x64 executable (run from Linux/WSL/macOS).
# Output: dist/win-x64/
#
# Usage:
#   ./scripts/publish-windows.sh
#   SKIP_WEB_BUILD=1 ./scripts/publish-windows.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
OUTPUT_DIR="${API_DIR}/dist/win-x64"
RUNTIME="${RUNTIME:-win-x64}"

source "${SCRIPT_DIR}/publish-common.sh"

echo "==> VanadiumAPI Windows publish (${RUNTIME})"

if [[ "${SKIP_WEB_BUILD:-0}" != "1" ]]; then
  "${SCRIPT_DIR}/build-web.sh"
else
  echo "==> SKIP_WEB_BUILD=1, using existing wwwroot"
fi

rm -rf "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"

publish_vanadium_api "${RUNTIME}" "${OUTPUT_DIR}" "${API_DIR}"

cat > "${OUTPUT_DIR}/run.bat" <<'EOF'
@echo off
cd /d "%~dp0"
VanadiumAPI.exe %*
EOF

echo ""
echo "==> Windows build complete"
echo "    ${OUTPUT_DIR}/VanadiumAPI.exe"
echo "    ${OUTPUT_DIR}/run.bat"
echo ""
echo "Deploy: copy dist/win-x64 to a Windows machine and run VanadiumAPI.exe or run.bat"
