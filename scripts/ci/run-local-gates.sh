#!/usr/bin/env bash
# Runs the repository's merge gates on the owner host, replacing the GitHub Actions workflows
# (backend.yml, frontend.yml), which do not run for this repository (owner decision 2026-10-28,
# PLAN-0053). The gates run on a clean worktree of a committed ref, so uncommitted files in the
# development checkout can neither help nor break a result.
#
# Usage: scripts/ci/run-local-gates.sh [--ref <git-ref>] [--only backend,contracts,frontend,smoke,secrets]
#
# Results: artifacts/local-gates/<sha>/summary.md plus one log per step. Exit status is non-zero
# when any step fails; every selected step still runs so the summary lists all failures.
# The PLAN-0005 headed-browser validation workflow is not covered; run scripts/plan-0005/ directly.
set -uo pipefail

REF="HEAD"
ONLY="backend,contracts,frontend,smoke,secrets"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --ref) REF="$2"; shift 2 ;;
    --only) ONLY="$2"; shift 2 ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
SHA="$(git -C "$REPO_ROOT" rev-parse --verify "$REF^{commit}")" || exit 2
OUT="$REPO_ROOT/artifacts/local-gates/$SHA"
WORKTREE="$(mktemp -d /tmp/cocinaris-gates.XXXXXX)"
export PATH="${COCINARIS_DOTNET_DIR:-/opt/dotnet}:${COCINARIS_NODE_DIR:-/opt/node24}/bin:$PATH"
export DOTNET_ROOT="${COCINARIS_DOTNET_DIR:-/opt/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# A host PostgreSQL usually owns 5432; the development fixture then listens on 55432.
export COCINARIS_DEV_POSTGRES_PORT="${COCINARIS_DEV_POSTGRES_PORT:-55432}"
export COCINARIS_DB_CONNECTION="Host=127.0.0.1;Port=$COCINARIS_DEV_POSTGRES_PORT;Database=cocinaris;Username=cocinaris_dev;Password=development-only-change-me"
GITLEAKS_IMAGE="zricethezav/gitleaks:v8.18.4"

mkdir -p "$OUT"
: > "$OUT/summary.tsv"
FAILED=0
COMPOSE_UP=0

selected() { [[ ",$ONLY," == *",$1,"* ]]; }

# step <name> <command...>: runs in the worktree, logs to $OUT/<name>.log, records the result.
step() {
  local name="$1"; shift
  local started=$SECONDS
  if (cd "$WORKTREE" && "$@") > "$OUT/$name.log" 2>&1; then
    printf '%s\tPassed\t%ss\n' "$name" "$((SECONDS - started))" | tee -a "$OUT/summary.tsv"
  else
    printf '%s\tFailed\t%ss\n' "$name" "$((SECONDS - started))" | tee -a "$OUT/summary.tsv"
    FAILED=1
  fi
}

compose() { docker compose -f "$WORKTREE/infrastructure/compose/compose.dev.yml" -p cocinaris-gates "$@"; }

cleanup() {
  [[ $COMPOSE_UP -eq 1 ]] && compose down -v --remove-orphans >/dev/null 2>&1
  [[ -n "${API_PID:-}" ]] && kill -- "-$API_PID" 2>/dev/null
  git -C "$REPO_ROOT" worktree remove --force "$WORKTREE" >/dev/null 2>&1 || rm -rf "$WORKTREE"
}
trap cleanup EXIT

git -C "$REPO_ROOT" worktree add --quiet --detach "$WORKTREE" "$SHA" || exit 2
echo "Local gates for $SHA ($REF) — logs in $OUT"

backend_dependencies() {
  compose up -d postgres keycloak &&
    timeout 180 bash -c 'until curl --fail --silent http://127.0.0.1:8080/realms/cocinaris/.well-known/openid-configuration >/dev/null; do sleep 2; done'
}

migrations_twice() {
  bash scripts/backend/generate-migration-script.sh artifacts/cocinaris-migrations.sql &&
    compose exec -T postgres psql -q -U cocinaris_dev -d postgres -c 'DROP DATABASE IF EXISTS cocinaris_idempotent WITH (FORCE)' -c 'CREATE DATABASE cocinaris_idempotent' &&
    compose exec -T postgres psql -q -v ON_ERROR_STOP=1 -U cocinaris_dev -d cocinaris_idempotent < artifacts/cocinaris-migrations.sql &&
    compose exec -T postgres psql -q -v ON_ERROR_STOP=1 -U cocinaris_dev -d cocinaris_idempotent < artifacts/cocinaris-migrations.sql &&
    expected="$(find apps/backend/src/Cocinaris.Infrastructure/Persistence/Migrations -maxdepth 1 -name '*.cs' ! -name '*Designer.cs' ! -name 'ApplicationDbContextModelSnapshot.cs' | wc -l)" &&
    actual="$(compose exec -T postgres psql -tAc 'SELECT count(*) FROM "__EFMigrationsHistory"' -U cocinaris_dev -d cocinaris_idempotent | tr -d '[:space:]')" &&
    echo "migrations: expected $expected, applied $actual" && test "$actual" -eq "$expected"
}

openapi_drift() {
  export COCINARIS_OPENAPI_URL=http://127.0.0.1:7080/openapi/v1.json
  dotnet ef database update --project apps/backend/src/Cocinaris.Infrastructure/Cocinaris.Infrastructure.csproj --startup-project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj --configuration Release --no-build || return 1
  setsid dotnet run --project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj --configuration Release --no-build --urls http://127.0.0.1:7080 > "$OUT/api.log" 2>&1 &
  API_PID=$!
  timeout 120 bash -c 'until curl --fail --silent http://127.0.0.1:7080/health/ready >/dev/null; do sleep 2; done' &&
    bash scripts/backend/export-openapi.sh &&
    git diff --exit-code -- packages/contracts/openapi/cocinaris-v1.json &&
    bash scripts/backend/check-openapi.sh &&
    bash scripts/backend/lint-openapi.sh
  local result=$?
  kill -- "-$API_PID" 2>/dev/null; API_PID=""
  return $result
}

if selected backend; then
  if ss -ltn | grep -q -E ':(7080|8080) '; then
    echo "ports 7080/8080 are in use; stop the development stack or API first" | tee "$OUT/backend-ports.log"
    printf 'backend-ports\tFailed\t0s\n' | tee -a "$OUT/summary.tsv"; FAILED=1
  else
    step backend-restore bash -c 'cd apps/backend && dotnet restore Cocinaris.slnx --locked-mode'
    step backend-vulnerabilities bash scripts/backend/check-dotnet-vulnerabilities.sh artifacts/dotnet-vulnerabilities.json
    step backend-format bash -c 'cd apps/backend && dotnet format Cocinaris.slnx --verify-no-changes --no-restore'
    step backend-build bash -c 'cd apps/backend && dotnet build Cocinaris.slnx -c Release --no-restore -warnaserror'
    step backend-dependencies backend_dependencies && COMPOSE_UP=1
    step backend-tests bash -c 'cd apps/backend && dotnet test Cocinaris.slnx -c Release --no-build'
    step backend-tool-restore dotnet tool restore
    step backend-migrations-idempotent migrations_twice
    step backend-openapi-drift openapi_drift
  fi
fi

if selected contracts; then
  step contracts-recipe-ai bash scripts/contracts/validate-recipe-ai.sh
fi

if selected frontend || selected contracts || selected smoke; then
  step frontend-install bash -c 'cd apps/frontend && yarn install --frozen-lockfile --silent'
  step api-client-install bash -c 'cd packages/api-client && yarn install --frozen-lockfile --silent'
fi

if selected contracts; then
  step api-client-drift bash -c 'cd apps/frontend && yarn -s check:api-client-drift'
  step api-client-typecheck bash -c 'cd apps/frontend && yarn -s typecheck:api-client'
  step api-client-format bash -c 'cd apps/frontend && yarn -s format:check:api-client'
fi

if selected frontend; then
  for script in typecheck lint format:check test guard:ts-only guard:interactive-nesting guard:build-mode guard:production-isolation build inspect:production-bundle build:prototype build:production inspect:production-bundle guard:third-party-html audit:policy; do
    step "frontend-${script//:/-}" bash -c "cd apps/frontend && yarn -s $script"
  done
  step frontend-zoom-evidence bash -c 'cd apps/frontend && node --check scripts/browser-zoom-200-validation.js && node ../../scripts/frontend/validate-zoom-evidence.mjs'
fi

if selected smoke; then
  if ss -ltn | grep -q -E ':(3000|3001) '; then
    echo "ports 3000/3001 are in use; stop the frontend dev server first" | tee "$OUT/frontend-smoke.log"
    printf 'frontend-smoke\tFailed\t0s\n' | tee -a "$OUT/summary.tsv"; FAILED=1
  else
    step frontend-smoke bash -c "cd apps/frontend && SMOKE_TESTED_CODE_SHA=$SHA yarn -s smoke:browser:ci"
  fi
fi

if selected secrets; then
  # Full history, like the CI job with fetch-depth 0; findings are redacted in the log.
  step secrets-gitleaks docker run --rm -v "$WORKTREE:/repo:ro" -v "$REPO_ROOT/.git:$REPO_ROOT/.git:ro" "$GITLEAKS_IMAGE" detect --source /repo --config /repo/.gitleaks.toml --no-banner --redact
fi

{
  echo "# Local gates"
  echo
  echo "- Commit: \`$SHA\` ($REF)"
  echo "- Host: $(hostname), $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "- Result: $([[ $FAILED -eq 0 ]] && echo Passed || echo Failed)"
  echo
  echo "| Step | Result | Duration |"
  echo "|---|---|---|"
  awk -F'\t' '{ printf "| %s | %s | %s |\n", $1, $2, $3 }' "$OUT/summary.tsv"
} > "$OUT/summary.md"
echo "Summary: $OUT/summary.md"
exit $FAILED
