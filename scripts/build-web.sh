#!/usr/bin/env bash
# Builds VanadiumWebApp (Angular) and copies the output into VanadiumAPI/wwwroot
# so the API serves the dashboard on the same port as SignalR (/panelReadingsHub).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
WEBAPP_DIR="$(cd "${API_DIR}/../VanadiumWebApp" && pwd)"
WWWROOT="${API_DIR}/wwwroot"
DIST_BROWSER="${WEBAPP_DIR}/dist/web-app/browser"

echo "==> VanadiumWebApp: ${WEBAPP_DIR}"
echo "==> VanadiumAPI wwwroot: ${WWWROOT}"

if [[ ! -f "${WEBAPP_DIR}/package.json" ]]; then
  echo "Error: Angular project not found at ${WEBAPP_DIR}" >&2
  exit 1
fi

cd "${WEBAPP_DIR}"

if [[ ! -d node_modules ]]; then
  if [[ -f package-lock.json ]]; then
    echo "==> npm ci"
    npm ci
  else
    echo "==> npm install"
    npm install
  fi
else
  echo "==> node_modules present, skipping npm install (delete node_modules to reinstall)"
fi

echo "==> ng build (production)"
npm run build -- --configuration production

if [[ ! -d "${DIST_BROWSER}" ]]; then
  echo "Error: build output not found at ${DIST_BROWSER}" >&2
  exit 1
fi

echo "==> Copying to wwwroot"
mkdir -p "${WWWROOT}"
# Replace previous deploy (keep .gitkeep if present)
find "${WWWROOT}" -mindepth 1 ! -name '.gitkeep' -delete 2>/dev/null || true
cp -a "${DIST_BROWSER}/." "${WWWROOT}/"

# Same-origin SignalR hub (relative URL works on any host:port the API uses)
cat > "${WWWROOT}/assets/appSettings.json" <<'EOF'
{
  "apiUrl": "/panelReadingsHub"
}
EOF

echo "==> Done. Static files deployed to ${WWWROOT}"
echo "    Start the API (port from appsettings Server:Url, default http://localhost:5010):"
echo "      cd ${API_DIR} && dotnet run"
