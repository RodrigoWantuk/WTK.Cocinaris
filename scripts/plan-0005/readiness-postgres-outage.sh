#!/usr/bin/env bash
# PLAN-0005: readiness outage (125/126) on shared Compose API.
# Application mutation integrity (132) is proven by Plan0005OutageMutationTests → outage-mutation-recovery.json.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
EVIDENCE_DIR="${PLAN0005_EVIDENCE_DIR:-docs/evidence/plan-0005}"
mkdir -p "$EVIDENCE_DIR"
COMPOSE=(docker compose -f infrastructure/compose/compose.dev.yml)

health_ready() {
  curl --fail --silent --max-time 5 http://127.0.0.1:7080/health/ready >/dev/null 2>&1 \
    || curl --fail --silent --max-time 5 --insecure https://localhost:7443/health/ready >/dev/null 2>&1
}
health_live() {
  curl --fail --silent --max-time 5 http://127.0.0.1:7080/health/live >/dev/null 2>&1 \
    || curl --fail --silent --max-time 5 --insecure https://localhost:7443/health/live >/dev/null 2>&1
}

if ! health_live; then
  echo "API must already be running (prove-environment) before readiness outage probe."
  exit 1
fi

BEFORE_READY=Passed
health_ready || BEFORE_READY=Failed

echo "Stopping PostgreSQL to force readiness failure"
"${COMPOSE[@]}" stop postgres
sleep 3

OUTAGE_UNAVAILABLE=Passed
if health_ready; then
  OUTAGE_UNAVAILABLE=Failed
fi

LIVE_DURING_OUTAGE=Passed
health_live || LIVE_DURING_OUTAGE=Failed

echo "Restoring PostgreSQL"
"${COMPOSE[@]}" start postgres
timeout 120 bash -c 'until docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres pg_isready -U cocinaris_dev -d cocinaris >/dev/null 2>&1; do sleep 2; done'
RECOVERY=Failed
for _ in $(seq 1 60); do
  if health_ready; then
    RECOVERY=Passed
    break
  fi
  sleep 2
done

python3 - <<PY
import json
from pathlib import Path

evidence = Path("${EVIDENCE_DIR}")
status_126 = "Passed" if "${OUTAGE_UNAVAILABLE}" == "Passed" and "${RECOVERY}" == "Passed" else "Failed"
note_126 = f"unavailable_during_outage=${OUTAGE_UNAVAILABLE}; recovered=${RECOVERY}; baseline_before=${BEFORE_READY}"

status_132 = "Blocked"
note_132 = "Awaiting Plan0005OutageMutationTests evidence (outage-mutation-recovery.json)"
outage_app = evidence / "outage-mutation-recovery.json"
if outage_app.exists():
    data = json.loads(outage_app.read_text(encoding="utf-8"))
    status_132 = data.get("status", "Blocked")
    note_132 = data.get("note", "See outage-mutation-recovery.json")

payload = {
  "plan": "PLAN-0005",
  "group": "readiness-postgres-outage",
  "integratedMainSha": "${PLAN0005_INTEGRATED_SHA:-b94abd9a83fe29d88b095e3e9a42f10d01c05414}",
  "results": [
    {"testId": "TEST-0005-125", "status": "${LIVE_DURING_OUTAGE}", "note": "Liveness during PostgreSQL stop"},
    {"testId": "TEST-0005-126", "status": status_126, "note": note_126},
    {"testId": "TEST-0005-132", "status": status_132, "note": note_132}
  ]
}
failed = sum(1 for r in payload["results"] if r["status"] not in {"Passed"})
payload["summary"] = {
  "passed": sum(1 for r in payload["results"] if r["status"] == "Passed"),
  "failed": failed,
  "blocked": sum(1 for r in payload["results"] if r["status"] == "Blocked"),
}
(evidence / "readiness-postgres-outage.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
print(json.dumps(payload, indent=2))
raise SystemExit(1 if failed else 0)
PY
