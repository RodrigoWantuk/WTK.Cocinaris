#!/usr/bin/env bash
# Starts or stops a production-shaped replica on https://localhost:7443: a separate Caddy process
# using infrastructure/deploy/linux-host/Caddyfile.cocinaris (TLS internal, admin off, its own
# storage) in front of the API in Development mode with the local PostgreSQL and OIDC fixtures,
# serving the production frontend build. Defects that only appear behind the deployment proxy,
# such as altered ETags (PLAN-0053), reproduce here and not in unit or integration tests.
#
# Usage: scripts/e2e/proxy-replica.sh start [caddy-template]   (default: repository template)
#        scripts/e2e/proxy-replica.sh stop
# Prerequisites: `docker compose -f infrastructure/compose/compose.dev.yml up -d postgres keycloak`
# with COCINARIS_DEV_POSTGRES_PORT=55432, migrations applied, a Debug backend build, and
# `yarn build` in apps/frontend. The OIDC fixture only accepts https://localhost:7443.
# Set API_DLL to run a published API build (for example a previous release) instead of the source.
set -euo pipefail
REPO="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
STATE="$REPO/.local-data/e2e"
export PATH=/opt/dotnet:/opt/node24/bin:$PATH DOTNET_ROOT=/opt/dotnet

stop() {
  for name in api caddy; do
    [[ -f "$STATE/$name.pid" ]] && kill -- "-$(cat "$STATE/$name.pid")" 2>/dev/null || true
    rm -f "$STATE/$name.pid"
  done
}

if [[ "${1:-}" == "stop" ]]; then stop; exit 0; fi
[[ "${1:-}" == "start" ]] || { sed -n '2,14p' "$0"; exit 2; }
TEMPLATE="${2:-$REPO/infrastructure/deploy/linux-host/Caddyfile.cocinaris}"
stop
mkdir -p "$STATE/keys" "$STATE/caddy-data"
export COCINARIS_DB_CONNECTION="Host=127.0.0.1;Port=${COCINARIS_DEV_POSTGRES_PORT:-55432};Database=cocinaris;Username=cocinaris_dev;Password=development-only-change-me"
# Synthetic development-only secret committed with the realm fixture; never printed.
COCINARIS_OIDC_CLIENT_SECRET="$(python3 -c "import json,sys;r=json.load(open(sys.argv[1]));print([c['secret'] for c in r['clients'] if c['clientId']=='cocinaris-backend'][0])" "$REPO/infrastructure/keycloak/cocinaris-realm.json")"
export COCINARIS_OIDC_CLIENT_SECRET COCINARIS_SESSION_KEYRING_PATH="$STATE/keys" ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

python3 - "$TEMPLATE" "$REPO" "$STATE" > "$STATE/Caddyfile" <<'PY'
import sys
template, repo, state = sys.argv[1:]
text = open(template).read()
text = text.replace("cocinaris.example.com {", "localhost:7443 {\n\ttls internal", 1)
text = text.replace("/srv/wtk-cocinaris/web", f"{repo}/apps/frontend/build").replace("127.0.0.1:8004", "127.0.0.1:7080")
print("{\n\tadmin off\n\tstorage file_system " + state + "/caddy-data\n\tskip_install_trust\n\thttp_port 7081\n\tauto_https disable_redirects\n}\n" + text)
PY

if [[ -n "${API_DLL:-}" ]]; then
  (cd "$(dirname "$API_DLL")" && exec setsid dotnet "$API_DLL" --urls http://127.0.0.1:7080) > "$STATE/api.log" 2>&1 &
else
  (cd "$REPO" && exec setsid dotnet run --project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:7080) > "$STATE/api.log" 2>&1 &
fi
echo $! > "$STATE/api.pid"
setsid caddy run --config "$STATE/Caddyfile" --adapter caddyfile > "$STATE/caddy.log" 2>&1 &
echo $! > "$STATE/caddy.pid"
timeout 90 bash -c 'until curl -sk --fail https://localhost:7443/health/ready >/dev/null; do sleep 2; done'
echo "replica ready on https://localhost:7443 (logs in $STATE)"
