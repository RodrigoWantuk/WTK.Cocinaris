#!/usr/bin/env bash
# PLAN-0005 P1/P2 completion round orchestration.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
# shellcheck disable=SC1091
source "${ROOT}/scripts/plan-0005/lib-identity.sh"
EVIDENCE_DIR="${PLAN0005_EVIDENCE_DIR}"
REPORT_DIR="${EVIDENCE_DIR}/reports"
mkdir -p "$REPORT_DIR"
: > "${REPORT_DIR}/p1-group-results.tsv"
plan0005_write_identity_json
: > "${REPORT_DIR}/container-samples.tsv"
export HOME="${PLAYWRIGHT_HOME:-/root}" PLAYWRIGHT_HOME="${PLAYWRIGHT_HOME:-/root}"
unset XAUTHORITY || true
export DISPLAY="${PLAYWRIGHT_DISPLAY:-}"
(
  while true; do
    bash "${ROOT}/scripts/plan-0005/container-count.sh" sample || true
    sleep 2
  done
) &
CONTAINER_SAMPLER_PID=$!

if [[ ! -f .env ]]; then cp .env.example .env; fi
set -a
# shellcheck disable=SC1091
source .env
set +a
export COCINARIS_SMOKE_PASSWORD_A="${COCINARIS_SMOKE_PASSWORD_A:-development-only-user-a}"
export COCINARIS_SMOKE_PASSWORD_B="${COCINARIS_SMOKE_PASSWORD_B:-development-only-user-b}"

OVERALL=0
RUN_START="$(date +%s)"
run_group() {
  local name="$1"; shift
  local start end status
  start="$(date +%s)"
  echo "==> PLAN-0005 P1 group: ${name}"
  set +e
  "$@"
  status=$?
  set -e
  end="$(date +%s)"
  printf '%s|%s|%s\n' "$name" "$status" "$((end-start))" >> "${REPORT_DIR}/p1-group-results.tsv"
  [[ $status -eq 0 ]] || OVERALL=1
}

chmod +x scripts/plan-0005/*.sh || true

# Ensure shared infra + API for OpenAPI/health groups.
run_group environment bash scripts/plan-0005/prove-environment.sh

run_group pagination-http bash -c '
  dotnet test apps/backend/Cocinaris.slnx -c Release --no-build \
    --filter "FullyQualifiedName~Plan0005P1PaginationTests" \
    --logger "trx;LogFilePrefix=plan0005-p1-page-" \
    --results-directory "'"${REPORT_DIR}"'/trx-p1"
'

run_group openapi bash scripts/plan-0005/openapi-p1.sh
run_group i18n node scripts/plan-0005/i18n-production-catalog.mjs
run_group axe node scripts/plan-0005/axe-production-surfaces.cjs
run_group observability-performance bash scripts/plan-0005/observability-performance-p1.sh

# Frontend unit/i18n tests already in package
run_group frontend-unit bash -c '
  cd apps/frontend
  yarn test --watchAll=false --testPathPattern="i18n|productionIsolation|productionCatalog" --passWithNoTests
'

kill "${CONTAINER_SAMPLER_PID}" 2>/dev/null || true
wait "${CONTAINER_SAMPLER_PID}" 2>/dev/null || true
bash "${ROOT}/scripts/plan-0005/container-count.sh" finalize || true

RUN_END="$(date +%s)"
python3 - <<PY
import json
from pathlib import Path
from datetime import datetime, timezone
report_dir = Path("${REPORT_DIR}")
evidence = Path("${EVIDENCE_DIR}")
groups=[]
overall="Passed"
for line in (report_dir/"p1-group-results.tsv").read_text().splitlines():
    name, status, dur = line.split("|")
    st = "Passed" if int(status)==0 else "Failed"
    if st=="Failed":
        overall="Failed"
    groups.append({"name": name, "exitCode": int(status), "durationSeconds": int(dur), "status": st})

def load(name):
    p = evidence/name
    return json.loads(p.read_text()) if p.exists() else None

containers = {}
cc = load("container-count.json")
if cc:
    containers = cc.get("containers") or {}

timing = {
  "plan": "PLAN-0005",
  "round": "P1-completion-round",
  "integratedMainSha": "${PLAN0005_INTEGRATED_SHA}",
  "prHeadSha": "${PLAN0005_PR_HEAD_SHA}",
  "checkedOutCommitSha": "${PLAN0005_CHECKED_OUT_SHA}",
  "evidenceGenerationSha": "${PLAN0005_EVIDENCE_GENERATION_SHA}",
  "startedAtUtc": datetime.fromtimestamp(int("${RUN_START}"), timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
  "endedAtUtc": datetime.fromtimestamp(int("${RUN_END}"), timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
  "totalSeconds": int("${RUN_END}")-int("${RUN_START}"),
  "containers": containers,
  "composeServiceCount": containers.get("composeServiceCount"),
  "groups": groups,
  "overall": overall,
  "openapi": None if not load("openapi-p1.json") else load("openapi-p1.json").get("summary"),
  "i18n": None if not load("i18n-production-catalog.json") else load("i18n-production-catalog.json").get("summary"),
  "axe": None if not load("axe-production-surfaces.json") else load("axe-production-surfaces.json").get("summary"),
  "observability": None if not load("observability-performance-p1.json") else load("observability-performance-p1.json").get("summary"),
}
(evidence/"p1-round-timing.json").write_text(json.dumps(timing, indent=2)+"\n", encoding="utf-8")
print(json.dumps(timing, indent=2))
raise SystemExit(0 if overall=="Passed" and int("${OVERALL}")==0 else 1)
PY
