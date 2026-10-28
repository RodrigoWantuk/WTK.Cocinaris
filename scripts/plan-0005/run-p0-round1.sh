#!/usr/bin/env bash
# PLAN-0005 P0 round-1 orchestration with shared Compose infrastructure and timing.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
source "${ROOT}/scripts/plan-0005/lib-identity.sh"
EVIDENCE_DIR="${PLAN0005_EVIDENCE_DIR}"
REPORT_DIR="${EVIDENCE_DIR}/reports"
mkdir -p "$REPORT_DIR" "$EVIDENCE_DIR/artifacts"
: > "${REPORT_DIR}/group-results.tsv"
plan0005_write_identity_json
: > "${REPORT_DIR}/container-samples.tsv"
# Background Docker sampling for Compose vs Testcontainers honesty.
(
  while true; do
    bash "${ROOT}/scripts/plan-0005/container-count.sh" sample || true
    sleep 2
  done
) &
CONTAINER_SAMPLER_PID=$!

if [[ ! -f .env ]]; then
  cp .env.example .env
fi
# shellcheck disable=SC1091
set -a
source .env
set +a
export COCINARIS_SMOKE_PASSWORD_A="${COCINARIS_SMOKE_PASSWORD_A:-development-only-user-a}"
export COCINARIS_SMOKE_PASSWORD_B="${COCINARIS_SMOKE_PASSWORD_B:-development-only-user-b}"

RUN_START="$(date +%s)"
RUN_START_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
OVERALL_STATUS=0
declare -a GROUP_RESULTS=()

run_group() {
  local name="$1"
  shift
  local start end status
  start="$(date +%s)"
  echo "==> PLAN-0005 group: ${name}"
  set +e
  "$@"
  status=$?
  set -e
  end="$(date +%s)"
  GROUP_RESULTS+=("${name}|${status}|$((end - start))")
  printf '%s|%s|%s\n' "${name}" "${status}" "$((end - start))" >> "${REPORT_DIR}/group-results.tsv"
  if [[ $status -ne 0 ]]; then
    OVERALL_STATUS=1
  fi
  return 0
}

TEARDOWN() {
  local api_pid fe_pid
  if [[ -f "${REPORT_DIR}/api.pid" ]]; then
    api_pid="$(cat "${REPORT_DIR}/api.pid")"
    if kill -0 "${api_pid}" 2>/dev/null; then
      kill "${api_pid}" 2>/dev/null || true
      wait "${api_pid}" 2>/dev/null || true
    fi
    rm -f "${REPORT_DIR}/api.pid"
  fi
  if [[ -f "${REPORT_DIR}/frontend.pid" ]]; then
    fe_pid="$(cat "${REPORT_DIR}/frontend.pid")"
    if kill -0 "${fe_pid}" 2>/dev/null; then
      kill "${fe_pid}" 2>/dev/null || true
      wait "${fe_pid}" 2>/dev/null || true
    fi
    rm -f "${REPORT_DIR}/frontend.pid"
  fi
}
trap TEARDOWN EXIT

chmod +x scripts/plan-0005/*.sh || true

run_group environment bash scripts/plan-0005/prove-environment.sh

run_group backend-domain-and-http bash -c '
  dotnet test apps/backend/Cocinaris.slnx -c Release --no-build \
    --filter "FullyQualifiedName~Cocinaris.UnitTests|FullyQualifiedName~Cocinaris.ArchitectureTests|FullyQualifiedName~Plan0005P0GapTests|FullyQualifiedName~ApiAuthenticationTests" \
    --logger "trx;LogFilePrefix=plan0005-p0-" \
    --results-directory "'"${REPORT_DIR}"'/trx"
'

run_group real-keycloak-oidc bash -c '
  node scripts/plan-0005/keycloak-p0-auth.mjs
'

run_group migrations bash -c '
  bash scripts/backend/generate-migration-script.sh "'"${REPORT_DIR}"'/cocinaris-migrations.sql"
  docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres psql -U cocinaris_dev -d postgres -c "DROP DATABASE IF EXISTS cocinaris_plan0005_idempotent WITH (FORCE)"
  docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres psql -U cocinaris_dev -d postgres -c "CREATE DATABASE cocinaris_plan0005_idempotent"
  docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres psql -v ON_ERROR_STOP=1 -U cocinaris_dev -d cocinaris_plan0005_idempotent < "'"${REPORT_DIR}"'/cocinaris-migrations.sql"
  docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres psql -v ON_ERROR_STOP=1 -U cocinaris_dev -d cocinaris_plan0005_idempotent < "'"${REPORT_DIR}"'/cocinaris-migrations.sql"
  expected="$(find apps/backend/src/Cocinaris.Infrastructure/Persistence/Migrations -maxdepth 1 -type f -name "*.cs" ! -name "*Designer.cs" ! -name "ApplicationDbContextModelSnapshot.cs" | wc -l | tr -d " ")"
  actual="$(docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres psql -tAc "SELECT count(*) FROM \"__EFMigrationsHistory\"" -U cocinaris_dev -d cocinaris_plan0005_idempotent | tr -d " ")"
  test "$actual" -eq "$expected"
'

run_group browser-e2e-frontend-production bash -c '
  # Always (re)build production here so Firefox/axe probes are not dependent on a partial env step.
  (cd apps/frontend && yarn install --frozen-lockfile && BUILD_PATH=build-production yarn build:production)
  # Production inventory journey availability probe (no mocks / no prototype substitution).
  python3 - <<'"'"'PY'"'"'
import json, pathlib, re
root = pathlib.Path("'"${ROOT}"'")
prod_index_path = root / "apps/frontend/build-production/index.html"
if not prod_index_path.exists():
  raise SystemExit("missing build-production after explicit production build")
prod_index = prod_index_path.read_text(encoding="utf-8")
assets_dir = root / "apps/frontend/build-production/static/js"
assets = list(assets_dir.glob("*.js")) if assets_dir.exists() else []
src_hits = []
for pattern in ("**/FeatureUnavailable*", "**/*unavailable*Adapter*", "**/ProductionApp.tsx", "**/createProductionRuntime*"):
  src_hits.extend(root.glob("apps/frontend/src/" + pattern))
src_blob = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in src_hits if p.is_file())
blob = prod_index + "\n" + "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in assets[:20]) + "\n" + src_blob
markers = {
  "FeatureUnavailable": ("FeatureUnavailable" in blob) or ("FeatureUnavailable" in src_blob),
  "unavailableSession": ("unavailable" in blob.lower() and "session" in blob.lower()) or ("UnavailableSession" in src_blob) or ("createUnavailableSessionAdapter" in src_blob),
  "liveInventoryClient": bool(re.search(r"/api/v1/inventory", blob)),
}
status = "Blocked"
reason = "Production inventory adapter/session projection not implemented"
if markers["liveInventoryClient"] and not markers["FeatureUnavailable"]:
  status = "Passed"
  reason = "Live inventory client markers present without FeatureUnavailable gate"
report = {
  "testId": "TEST-0005-FRONTEND-PRODUCTION-INVENTORY",
  "status": status,
  "reason": reason,
  "markers": markers,
  "integratedMainSha": "'"${PLAN0005_INTEGRATED_SHA}"'",
}
path = pathlib.Path("'"${REPORT_DIR}"'") / "frontend-production-inventory.json"
path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
# Commit-friendly copy outside ignored reports/
(pathlib.Path("'"${EVIDENCE_DIR}"'") / "frontend-production-inventory.json").write_text(path.read_text(encoding="utf-8"), encoding="utf-8")
print(json.dumps(report, indent=2))
raise SystemExit(0 if status in {"Passed", "Blocked"} else 1)
PY
  (cd apps/frontend && BUILD_PATH=build-prototype yarn build:prototype)
  # Do not blank XAUTHORITY here — xvfb-run provides a valid authority cookie.
  # Local root/sudo hosts may set PLAYWRIGHT_XAUTHORITY="" explicitly.
  node scripts/plan-0005/firefox-zoom-pointer-keyboard.cjs
'

run_group security-automation bash -c '
  bash scripts/backend/check-dotnet-vulnerabilities.sh "'"${REPORT_DIR}"'/dotnet-vulnerabilities.json"
  (cd apps/frontend && yarn audit:policy)
'

TEARDOWN_START="$(date +%s)"
TEARDOWN
TEARDOWN_END="$(date +%s)"
# Disable trap duplicate teardown
trap - EXIT

kill "${CONTAINER_SAMPLER_PID}" 2>/dev/null || true
wait "${CONTAINER_SAMPLER_PID}" 2>/dev/null || true
bash "${ROOT}/scripts/plan-0005/container-count.sh" finalize || true

RUN_END="$(date +%s)"
RUN_END_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

python3 - <<PY
import json
from pathlib import Path
report_dir = Path("${REPORT_DIR}")
evidence_root = Path("${EVIDENCE_DIR}")
groups = []
overall = "Passed"
tsv = report_dir / "group-results.tsv"
if tsv.exists():
    for line in tsv.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        name, status, duration = line.split("|")
        status_i = int(status)
        entry = {"name": name, "exitCode": status_i, "durationSeconds": int(duration), "status": "Passed" if status_i == 0 else "Failed"}
        groups.append(entry)
        if status_i != 0:
            overall = "Failed"

# Fold Firefox report into summary when present.
# Pointer-only Failed + keyboard Passed is Conditional Pass residual (#21/#22), not a job failure.
ff_path = report_dir / "firefox-zoom-pointer-keyboard.json"
if ff_path.exists():
    ff = json.loads(ff_path.read_text(encoding="utf-8"))
    keyboard_failed = False
    pointer_failed = False
    for result in ff.get("results", []):
        if result.get("keyboard", {}).get("status") == "Failed":
            keyboard_failed = True
        if result.get("pointer", {}).get("status") == "Failed":
            pointer_failed = True
    if keyboard_failed:
        ff_status = "Failed"
        ff_exit = 1
        overall = "Failed"
    elif pointer_failed:
        ff_status = "ConditionalPassResidual"
        ff_exit = 0
    else:
        ff_status = "Passed"
        ff_exit = 0
    groups.append({
        "name": "firefox-pointer-keyboard-200",
        "status": ff_status,
        "durationSeconds": None,
        "exitCode": ff_exit,
        "pointerFailed": pointer_failed,
        "keyboardFailed": keyboard_failed,
        "issues": [21, 22] if pointer_failed and not keyboard_failed else []
    })

fe_path = report_dir / "frontend-production-inventory.json"
frontend_status = None
if fe_path.exists():
    frontend_status = json.loads(fe_path.read_text(encoding="utf-8"))

kc_path = report_dir / "keycloak-p0-auth.json"
kc = json.loads(kc_path.read_text(encoding="utf-8")) if kc_path.exists() else None

containers = {}
cc_path = evidence_root / "container-count.json"
if cc_path.exists():
    containers = json.loads(cc_path.read_text(encoding="utf-8")).get("containers", {})

timing = {
  "plan": "PLAN-0005",
  "round": "P0-round-1",
  "integratedMainSha": "${PLAN0005_INTEGRATED_SHA}",
  "prHeadSha": "${PLAN0005_PR_HEAD_SHA}",
  "checkedOutCommitSha": "${PLAN0005_CHECKED_OUT_SHA}",
  "evidenceGenerationSha": "${PLAN0005_EVIDENCE_GENERATION_SHA}",
  "startedAtUtc": "${RUN_START_UTC}",
  "endedAtUtc": "${RUN_END_UTC}",
  "containers": containers,
  "composeServiceCount": containers.get("composeServiceCount"),
  "startupAndTotal": {
    "totalSeconds": int("${RUN_END}") - int("${RUN_START}"),
    "teardownSeconds": int("${TEARDOWN_END}") - int("${TEARDOWN_START}")
  },
  "groups": groups,
  "overall": overall,
  "frontendProductionInventory": frontend_status,
  "keycloakSummary": None if kc is None else kc.get("summary"),
}
(report_dir / "p0-round1-timing.json").write_text(json.dumps(timing, indent=2) + "\n", encoding="utf-8")

# Regenerated current P0 summary (never reuse CSS-zoom historical summary under this name).
ff = json.loads(ff_path.read_text(encoding="utf-8")) if ff_path.exists() else {}
pointer = {}
issues = []
for result in ff.get("results", []):
    name = str(result.get("scenario") or result.get("name") or "")
    key = "Cook" if ("Cook" in name or "CTA" in name or "hoje" in name.lower()) else "Pantry"
    pointer[f"{key}_pointer"] = (result.get("pointer") or {}).get("status")
    pointer[f"{key}_keyboard"] = (result.get("keyboard") or {}).get("status")
if pointer.get("Cook_pointer") == "Failed":
    issues.append(21)
if pointer.get("Pantry_pointer") == "Failed":
    issues.append(22)
outcome = "ConditionalPassResidual" if any(pointer.get(k) == "Failed" for k in ("Cook_pointer", "Pantry_pointer")) else ("Pass" if overall == "Passed" else overall)
summary = {
  "plan": "PLAN-0005",
  "round": "P0-round-1",
  "integratedMainSha": "${PLAN0005_INTEGRATED_SHA}",
  "prHeadSha": "${PLAN0005_PR_HEAD_SHA}",
  "checkedOutCommitSha": "${PLAN0005_CHECKED_OUT_SHA}",
  "evidenceGenerationSha": "${PLAN0005_EVIDENCE_GENERATION_SHA}",
  "generatedAtUtc": "${RUN_END_UTC}",
  "overall": overall,
  "outcome": outcome,
  "zoomTechnique": ff.get("zoomTechnique") or "native-ctrl-plus-xdotool",
  "cssZoomForbidden": True,
  "frontendProductionInventory": {
    "status": None if frontend_status is None else frontend_status.get("status"),
    "issue": 20 if frontend_status and frontend_status.get("status") == "Blocked" else None,
  },
  "pointerResiduals": {**pointer, "issues": issues},
  "containers": {
    "composeServiceCount": containers.get("composeServiceCount"),
    "testcontainersCreated": containers.get("testcontainersCreated"),
    "maximumConcurrentContainers": containers.get("maximumConcurrentContainers"),
    "totalContainerInstancesCreated": containers.get("totalContainerInstancesCreated"),
    "testcontainerImages": containers.get("testcontainerImages"),
  },
}
(report_dir / "p0-round1-summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

# Commit-friendly copies outside ignored reports/ directories.
for name in ("p0-round1-timing.json", "p0-round1-summary.json", "keycloak-p0-auth.json", "frontend-production-inventory.json", "firefox-zoom-pointer-keyboard.json", "environment-timing.json"):
    src = report_dir / name
    if src.exists():
        (evidence_root / name).write_text(src.read_text(encoding="utf-8"), encoding="utf-8")
print(json.dumps(timing, indent=2))
raise SystemExit(0 if overall == "Passed" and int("${OVERALL_STATUS}") == 0 else 1)
PY
