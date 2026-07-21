#!/usr/bin/env bash
# Build Linux and Windows self-contained executables.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

export SKIP_WEB_BUILD=0
"${SCRIPT_DIR}/build-web.sh"

export SKIP_WEB_BUILD=1
"${SCRIPT_DIR}/publish-linux.sh"
"${SCRIPT_DIR}/publish-windows.sh"

echo ""
echo "==> All platforms published under dist/"
