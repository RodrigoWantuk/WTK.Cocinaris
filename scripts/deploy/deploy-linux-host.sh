#!/usr/bin/env bash
# Builds and deploys Cocinaris on a single Linux host (PLAN-0031).
#
# The build always runs in a temporary git worktree of the requested ref, never in the
# development checkout, so agents and developers can keep working while a deploy runs.
# Each deploy publishes into an immutable release directory; `api` and `web` under the
# deploy root are symlinks to the active release, which makes switching and rollback a
# symlink change. Database migrations are forward-only and applied through EF Core's
# idempotent script as the schema-owner role, after a fresh backup.
#
# Usage (as root):
#   scripts/deploy/deploy-linux-host.sh [--ref <git-ref>] [--dry-run] [--install-units]
#   scripts/deploy/deploy-linux-host.sh --rollback
#
# See docs/operations/linux-host-deployment-runbook.md for prerequisites and recovery.
set -euo pipefail

REF="origin/main"
DRY_RUN=0
INSTALL_UNITS=0
ROLLBACK=0

DEPLOY_ROOT="${COCINARIS_DEPLOY_ROOT:-/srv/wtk-cocinaris}"
SERVICE="${COCINARIS_SERVICE:-wtk-cocinaris}"
BACKUP_SERVICE="${COCINARIS_BACKUP_SERVICE:-wtk-cocinaris-backup}"
ENV_FILE="${COCINARIS_ENV_FILE:-/etc/wtk-cocinaris/api.env}"
DATABASE="${COCINARIS_DATABASE:-cocinaris}"
OWNER_ROLE="${COCINARIS_SCHEMA_OWNER_ROLE:-cocinaris_owner}"
HEALTH_URL="${COCINARIS_HEALTH_URL:-http://127.0.0.1:8004/health/ready}"
DOTNET_DIR="${COCINARIS_DOTNET_DIR:-/opt/dotnet}"
NODE_DIR="${COCINARIS_NODE_DIR:-/opt/node24}"
KEEP_RELEASES="${COCINARIS_KEEP_RELEASES:-5}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --ref) REF="$2"; shift 2 ;;
    --dry-run) DRY_RUN=1; shift ;;
    --install-units) INSTALL_UNITS=1; shift ;;
    --rollback) ROLLBACK=1; shift ;;
    -h|--help) sed -n '2,16p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

log() { printf '[deploy %s] %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }
die() { printf '[deploy] ERROR: %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run as root (service control, database owner role, release directories)"
REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
RELEASES="$DEPLOY_ROOT/releases"
export PATH="$DOTNET_DIR:$NODE_DIR/bin:$PATH" DOTNET_ROOT="$DOTNET_DIR" DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  DOTNET_NOLOGO=1 COREPACK_ENABLE_DOWNLOAD_PROMPT=0

wait_healthy() {
  for _ in $(seq 1 60); do
    curl --fail --silent --output /dev/null "$HEALTH_URL" && return 0
    sleep 2
  done
  return 1
}

switch_to() {
  # ln -sfn on a temporary name followed by mv -T replaces each symlink atomically.
  local release="$1"
  for part in api web; do
    ln -sfn "$release/$part" "$DEPLOY_ROOT/.$part.next"
    mv -T "$DEPLOY_ROOT/.$part.next" "$DEPLOY_ROOT/$part"
  done
}

adopt_legacy_layout() {
  # Earlier deployments used plain directories; move them into a release so rollback works.
  if [[ -d "$DEPLOY_ROOT/api" && ! -L "$DEPLOY_ROOT/api" ]]; then
    # Named like a release so chronological (lexical) ordering keeps working for rollback.
    local legacy="$RELEASES/$(date -u +%Y%m%dT%H%M%SZ)-legacy"
    log "adopting existing plain directories as $legacy"
    mkdir -p "$legacy"
    mv "$DEPLOY_ROOT/api" "$legacy/api"
    mv "$DEPLOY_ROOT/web" "$legacy/web"
    switch_to "$legacy"
  fi
}

if [[ $ROLLBACK -eq 1 ]]; then
  current="$(readlink -f "$DEPLOY_ROOT/api" | xargs dirname)"
  # The release just before the active one; newer entries may be unactivated dry runs.
  previous="$(ls -1d "$RELEASES"/*/ 2>/dev/null | sed 's#/$##' | sort | awk -v cur="$current" '$0 == cur { print prev; exit } { prev = $0 }')"
  [[ -n "$previous" ]] || die "no previous release to roll back to"
  log "rolling back from $(basename "$current") to $(basename "$previous")"
  log "database migrations are forward-only; restore a backup if the newer schema is incompatible"
  systemctl stop "$SERVICE"
  switch_to "$previous"
  systemctl start "$SERVICE"
  wait_healthy || die "service unhealthy after rollback; inspect: journalctl -u $SERVICE"
  log "rollback complete"
  exit 0
fi

command -v dotnet >/dev/null || die "dotnet not found in $DOTNET_DIR"
command -v node >/dev/null || die "node not found in $NODE_DIR"
[[ -f "$ENV_FILE" ]] || die "missing environment file $ENV_FILE (see infrastructure/deploy/linux-host/api.env.example)"

log "fetching $REF"
git -C "$REPO_ROOT" fetch --quiet origin || log "fetch failed; continuing with the local ref state"
SHA="$(git -C "$REPO_ROOT" rev-parse --verify "$REF^{commit}")"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RELEASE="$RELEASES/$STAMP-${SHA:0:12}"
WORKTREE="$(mktemp -d /tmp/cocinaris-deploy.XXXXXX)"
RELEASE_READY=0
cleanup() {
  git -C "$REPO_ROOT" worktree remove --force "$WORKTREE" >/dev/null 2>&1 || rm -rf "$WORKTREE"
  # A release that failed to build must never become a rollback candidate.
  [[ $RELEASE_READY -eq 1 ]] || rm -rf "$RELEASE"
}
trap cleanup EXIT

log "building $SHA in $WORKTREE"
git -C "$REPO_ROOT" worktree add --quiet --detach "$WORKTREE" "$SHA"
mkdir -p "$RELEASE"
(
  cd "$WORKTREE/apps/backend"
  dotnet restore Cocinaris.slnx --locked-mode
  dotnet build Cocinaris.slnx -c Release --no-restore
  dotnet publish src/Cocinaris.Api/Cocinaris.Api.csproj -c Release --no-build -o "$RELEASE/api"
)
dotnet build-server shutdown >/dev/null 2>&1 || true
(cd "$WORKTREE" && bash scripts/backend/generate-migration-script.sh "$RELEASE/migrations.sql")
(
  cd "$WORKTREE/apps/frontend"
  corepack yarn install --frozen-lockfile --ignore-scripts
  corepack yarn build:production
  corepack yarn guard:build-mode
)
# Never publish an entry page that loads code or styles from another origin: analytics or
# session-recording snippets there would see every authenticated page.
if grep -qiE '<(script|link)[^>]+(src|href)="(https?:)?//' "$WORKTREE/apps/frontend/build/index.html"; then
  die "build/index.html references an external origin; refusing to publish"
fi
cp -a "$WORKTREE/apps/frontend/build" "$RELEASE/web"
printf '%s\n' "$SHA" > "$RELEASE/REVISION"
RELEASE_READY=1

problems=()
entry="$(sed -n 's#^ExecStart=.* \([^ ]*\.dll\)$#\1#p' "/etc/systemd/system/$SERVICE.service" 2>/dev/null || true)"
if [[ $INSTALL_UNITS -eq 0 && -n "$entry" && "$(basename "$entry")" != "Cocinaris.Api.dll" ]]; then
  problems+=("installed unit starts $(basename "$entry"); rerun with --install-units (runbook: rename migration)")
fi
if grep -q '^KITCHENFLOW_' "$ENV_FILE"; then
  problems+=("$ENV_FILE still uses KITCHENFLOW_* keys; rename them to COCINARIS_* (runbook: rename migration)")
fi

if [[ $DRY_RUN -eq 1 ]]; then
  for problem in "${problems[@]}"; do log "a real deploy would stop here: $problem"; done
  log "dry run: release built at $RELEASE (not activated, database untouched)"
  exit 0
fi
for problem in "${problems[@]}"; do die "$problem"; done

if [[ $INSTALL_UNITS -eq 1 ]]; then
  log "installing systemd units and backup script"
  install -m 0644 "$WORKTREE/infrastructure/deploy/linux-host/wtk-cocinaris.service" /etc/systemd/system/
  install -m 0644 "$WORKTREE/infrastructure/deploy/linux-host/wtk-cocinaris-backup.service" /etc/systemd/system/
  install -m 0644 "$WORKTREE/infrastructure/deploy/linux-host/wtk-cocinaris-backup.timer" /etc/systemd/system/
  install -D -m 0755 "$WORKTREE/infrastructure/deploy/linux-host/cocinaris-db-backup.sh" /usr/local/sbin/cocinaris-db-backup
  systemctl daemon-reload
  systemctl enable --quiet "$SERVICE" "$BACKUP_SERVICE.timer"
fi

log "stopping $SERVICE and taking a backup"
systemctl stop "$SERVICE"
systemctl start "$BACKUP_SERVICE"
log "applying migrations as $OWNER_ROLE"
# EF writes a UTF-8 BOM that psql would treat as part of the first statement.
{ echo "SET ROLE $OWNER_ROLE;"; sed '1s/^\xEF\xBB\xBF//' "$RELEASE/migrations.sql"; } \
  | runuser -u postgres -- psql -d "$DATABASE" -v ON_ERROR_STOP=1 -q
adopt_legacy_layout
switch_to "$RELEASE"
systemctl start "$SERVICE"
if ! wait_healthy; then
  log "new release unhealthy; inspect: journalctl -u $SERVICE -n 200"
  die "deploy failed after migrations; run --rollback if the previous release is schema-compatible"
fi
log "deployed $SHA as $(basename "$RELEASE")"

ls -1d "$RELEASES"/*/ | sed 's#/$##' | sort | head -n -"$KEEP_RELEASES" | while read -r old; do
  [[ "$old" == "$RELEASE" ]] || rm -rf "$old"
done
