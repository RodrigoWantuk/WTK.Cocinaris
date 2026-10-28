#!/usr/bin/env bash
# PLAN-0005 P0 environment bring-up proof against shared Compose infrastructure.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

EVIDENCE_DIR="${PLAN0005_EVIDENCE_DIR:-docs/evidence/plan-0005}"
# Resolve to absolute paths so nested `cd` subshells cannot break redirects.
EVIDENCE_DIR="$(cd "$ROOT" && mkdir -p "$EVIDENCE_DIR" && cd "$EVIDENCE_DIR" && pwd)"
REPORT_DIR="${EVIDENCE_DIR}/reports"
mkdir -p "$REPORT_DIR"

INTEGRATED_SHA="${PLAN0005_INTEGRATED_SHA:-b94abd9a83fe29d88b095e3e9a42f10d01c05414}"
ACTUAL_HEAD="$(git rev-parse HEAD)"
TIMING_FILE="${REPORT_DIR}/environment-timing.json"
START_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
START_EPOCH="$(date +%s)"

log() { printf '[plan-0005-env] %s\n' "$*"; }

if ! git merge-base --is-ancestor "${INTEGRATED_SHA}" HEAD; then
  log "ERROR: integrated main SHA ${INTEGRATED_SHA} is not an ancestor of HEAD ${ACTUAL_HEAD}"
  exit 1
fi

if [[ ! -f .env ]]; then
  cp .env.example .env
  log "Created .env from .env.example (ignored local fixture)."
fi

# shellcheck disable=SC1091
set -a
source .env
set +a

COMPOSE=(docker compose -f infrastructure/compose/compose.dev.yml)

log "Validating Compose config"
"${COMPOSE[@]}" config --quiet

UP_START="$(date +%s)"
log "Starting shared postgres + keycloak"
"${COMPOSE[@]}" up -d postgres keycloak
UP_END="$(date +%s)"

log "Waiting for PostgreSQL readiness"
timeout 120 bash -c 'until docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres pg_isready -U cocinaris_dev -d cocinaris >/dev/null 2>&1; do sleep 2; done'

log "Waiting for Keycloak readiness"
timeout 180 bash -c 'until curl --fail --silent http://127.0.0.1:8080/realms/cocinaris/.well-known/openid-configuration >/dev/null; do sleep 2; done'

CONTAINER_COUNT="$("${COMPOSE[@]}" ps --status running --services | wc -l | tr -d ' ')"

MIG_START="$(date +%s)"
log "Restoring tools and applying migrations"
dotnet tool restore >/dev/null
dotnet restore apps/backend/Cocinaris.slnx --locked-mode
dotnet build apps/backend/Cocinaris.slnx -c Release --no-restore
dotnet ef database update \
  --project apps/backend/src/Cocinaris.Infrastructure/Cocinaris.Infrastructure.csproj \
  --startup-project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj \
  --configuration Release \
  --no-build
MIG_END="$(date +%s)"

mkdir -p .local-data/cocinaris/session-keys
export COCINARIS_DB_CONNECTION
export COCINARIS_OIDC_AUTHORITY
export COCINARIS_OIDC_CLIENT_ID
export COCINARIS_OIDC_CLIENT_SECRET
export COCINARIS_SESSION_KEYRING_PATH="${COCINARIS_SESSION_KEYRING_PATH:-.local-data/cocinaris/session-keys}"

API_LOG="${REPORT_DIR}/api-release.log"
API_START="$(date +%s)"
# Prefer trusted HTTPS; in CI Linux runners the ASP.NET development cert is often not in the
# system trust store, so health probes may use curl --insecure without disabling TLS itself.
curl_https() {
  if curl --fail --silent "$@" >/dev/null 2>&1; then
    return 0
  fi
  curl --fail --silent --insecure "$@" >/dev/null 2>&1
}

log "Starting backend Release on HTTPS :7443 (also HTTP :7080 for CI probes)"
if curl_https https://localhost:7443/health/live; then
  log "Backend already responding on :7443; reusing existing process"
  API_REUSED=true
else
  API_REUSED=false
  if ! dotnet dev-certs https --check >/dev/null 2>&1; then
    log "Creating local HTTPS development certificate"
  fi
  if ! dotnet dev-certs https --trust >/dev/null 2>&1; then
    log "Certificate trust incomplete on this host; HTTP :7080 and insecure HTTPS probes will be used"
  fi
  ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}" \
  ASPNETCORE_URLS="https://localhost:7443;http://127.0.0.1:7080" \
  nohup dotnet run \
    --project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj \
    --configuration Release \
    --no-build \
    --no-launch-profile \
    >"${API_LOG}" 2>&1 &
  echo $! > "${REPORT_DIR}/api.pid"
  timeout 180 bash -c 'until curl --fail --silent http://127.0.0.1:7080/health/live >/dev/null || curl --fail --silent --insecure https://localhost:7443/health/live >/dev/null; do sleep 2; done'
  timeout 180 bash -c 'until curl --fail --silent http://127.0.0.1:7080/health/ready >/dev/null || curl --fail --silent --insecure https://localhost:7443/health/ready >/dev/null; do sleep 2; done'
fi
API_END="$(date +%s)"

if ! curl --fail --silent http://127.0.0.1:7080/health/live >/dev/null 2>&1; then
  curl_https https://localhost:7443/health/live || { log "ERROR: health/live failed"; exit 1; }
fi
if ! curl --fail --silent http://127.0.0.1:7080/health/ready >/dev/null 2>&1; then
  curl_https https://localhost:7443/health/ready || { log "ERROR: health/ready failed"; exit 1; }
fi
log "Health and readiness OK"

FE_START="$(date +%s)"
log "Building frontend production"
(
  cd apps/frontend
  yarn install --frozen-lockfile
  BUILD_PATH=build-production yarn build:production
)
FE_END="$(date +%s)"

# Serve production build briefly to prove it is servable.
FE_SERVE_LOG="${REPORT_DIR}/frontend-production-serve.log"
FE_PORT="${PLAN0005_FE_PORT:-4173}"
if curl --fail --silent "http://127.0.0.1:${FE_PORT}/" >/dev/null 2>&1; then
  FE_REUSED=true
else
  FE_REUSED=false
  (
    cd apps/frontend
    nohup npx --yes serve -s build-production -l "tcp://127.0.0.1:${FE_PORT}" >"${FE_SERVE_LOG}" 2>&1 &
    echo $! > "${REPORT_DIR}/frontend.pid"
  )
  timeout 60 bash -c "until curl --fail --silent http://127.0.0.1:${FE_PORT}/ >/dev/null; do sleep 1; done"
fi
curl --fail --silent "http://127.0.0.1:${FE_PORT}/" >/dev/null
log "Frontend production served on :${FE_PORT}"

END_EPOCH="$(date +%s)"
END_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

python3 - <<PY
import json
from pathlib import Path
payload = {
  "integratedMainSha": "${INTEGRATED_SHA}",
  "actualHead": "${ACTUAL_HEAD}",
  "startedAtUtc": "${START_UTC}",
  "endedAtUtc": "${END_UTC}",
  "containerCount": int("${CONTAINER_COUNT}"),
  "services": ["postgres", "keycloak"],
  "apiReused": "${API_REUSED}" == "true",
  "frontendReused": "${FE_REUSED}" == "true",
  "frontendPort": int("${FE_PORT}"),
  "durationsSeconds": {
    "composeUp": int("${UP_END}") - int("${UP_START}"),
    "migrations": int("${MIG_END}") - int("${MIG_START}"),
    "apiReady": int("${API_END}") - int("${API_START}"),
    "frontendProductionBuild": int("${FE_END}") - int("${FE_START}"),
    "totalEnvironment": int("${END_EPOCH}") - int("${START_EPOCH}")
  },
  "checks": {
    "composeConfig": "Passed",
    "postgresReady": "Passed",
    "keycloakReady": "Passed",
    "migrationsApplied": "Passed",
    "backendReleaseHealth": "Passed",
    "backendReleaseReady": "Passed",
    "frontendProductionBuild": "Passed",
    "frontendProductionServed": "Passed"
  }
}
Path("${TIMING_FILE}").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
print(json.dumps(payload, indent=2))
PY

log "Environment proof complete → ${TIMING_FILE}"
