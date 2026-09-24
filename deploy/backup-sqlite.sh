#!/usr/bin/env sh
# Nightly encrypted backup of the search-log SQLite database from the dorksmith-state volume.
# Usage: BACKUP_DIR=/var/backups/dorksmith BACKUP_PASSPHRASE_FILE=/root/.dorksmith-backup-pass ./deploy/backup-sqlite.sh
# Cron:  15 3 * * * /opt/dorksmith/deploy/backup-sqlite.sh >> /var/log/dorksmith-backup.log 2>&1
set -eu

BACKUP_DIR="${BACKUP_DIR:-./backups}"
KEEP_DAYS="${KEEP_DAYS:-14}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP_DIR"

# Consistent snapshot via SQLite's online backup API, executed inside the api container.
docker compose exec -T api sh -c 'cd /tmp && rm -f snap.db && (command -v sqlite3 >/dev/null 2>&1 \
  && sqlite3 /app/state/dorksmith.db ".backup /tmp/snap.db" || cp /app/state/dorksmith.db /tmp/snap.db) && cat /tmp/snap.db' \
  > "$BACKUP_DIR/dorksmith-$STAMP.db"

if [ -n "${BACKUP_PASSPHRASE_FILE:-}" ]; then
  gpg --batch --yes --symmetric --cipher-algo AES256 --passphrase-file "$BACKUP_PASSPHRASE_FILE" \
      -o "$BACKUP_DIR/dorksmith-$STAMP.db.gpg" "$BACKUP_DIR/dorksmith-$STAMP.db"
  rm -f "$BACKUP_DIR/dorksmith-$STAMP.db"
fi

find "$BACKUP_DIR" -name 'dorksmith-*.db*' -mtime "+$KEEP_DAYS" -delete
echo "backup written: $BACKUP_DIR/dorksmith-$STAMP.db${BACKUP_PASSPHRASE_FILE:+.gpg}"
