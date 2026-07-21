#!/usr/bin/env bash
# Build VanadiumWebApp + publish a self-contained Linux x64 executable.
# Output: dist/linux-x64/
#
# Usage:
#   ./scripts/publish-linux.sh
#   SKIP_WEB_BUILD=1 ./scripts/publish-linux.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
OUTPUT_DIR="${API_DIR}/dist/linux-x64"
RUNTIME="${RUNTIME:-linux-x64}"

source "${SCRIPT_DIR}/publish-common.sh"

echo "==> VanadiumAPI Linux publish (${RUNTIME})"

if [[ "${SKIP_WEB_BUILD:-0}" != "1" ]]; then
  "${SCRIPT_DIR}/build-web.sh"
else
  echo "==> SKIP_WEB_BUILD=1, using existing wwwroot"
fi

rm -rf "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"

publish_vanadium_api "${RUNTIME}" "${OUTPUT_DIR}" "${API_DIR}"

cat > "${OUTPUT_DIR}/run.sh" <<'EOF'
#!/usr/bin/env bash
cd "$(dirname "$0")"
exec ./VanadiumAPI "$@"
EOF
chmod +x "${OUTPUT_DIR}/run.sh"
chmod +x "${OUTPUT_DIR}/VanadiumAPI" 2>/dev/null || true

echo ""
echo "==> Linux build complete"
echo "    ${OUTPUT_DIR}/VanadiumAPI"
echo "    ${OUTPUT_DIR}/run.sh"
echo ""
echo "Deploy: copy the entire dist/linux-x64 folder to the target server, then:"
echo "  cd dist/linux-x64 && ./run.sh"
