#!/bin/sh
# Custom-format dump of the Cocinaris database, kept for 14 days. Restore procedure and
# rehearsal: docs/operations/linux-host-deployment-runbook.md.
set -eu
DIR="${COCINARIS_BACKUP_DIR:-/var/backups/wtk-cocinaris}"
DATABASE="${COCINARIS_DATABASE:-cocinaris}"
install -d -m 700 "$DIR"
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
runuser -u postgres -- pg_dump --format=custom "$DATABASE" > "$DIR/$DATABASE-$STAMP.dump"
chmod 600 "$DIR/$DATABASE-$STAMP.dump"
find "$DIR" -name "$DATABASE-*.dump" -mtime +14 -delete
