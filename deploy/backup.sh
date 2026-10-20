#!/usr/bin/env bash
# OPS-DEPLOY-VPS — one backup of the database AND the object store, into $BACKUP_DIR (default /var/backups/luxmap).
#   ./backup.sh                 # from this directory; deploy.sh calls it before every migration
#   cron: 15 20 * * * cd /opt/luxmap/deploy && ./backup.sh >> /var/backups/luxmap/backup.log 2>&1   (server is UTC: 03:15 in Vietnam)
#
# The database dump is the custom format (`pg_restore` restores it). The object store is mirrored, not archived: photos
# and videos never change once written (keys carry their id or hash), so a mirror only copies what is new.
# Both stay ON THIS SERVER — copy $BACKUP_DIR off the machine as well (vps.md "Sao lưu"), or a lost disk loses both.
set -euo pipefail
cd "$(dirname "$0")"

BACKUP_DIR="${BACKUP_DIR:-/var/backups/luxmap}"
KEEP_DAYS="${KEEP_DAYS:-14}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP_DIR/db" "$BACKUP_DIR/objects"

# Written under a temporary name and renamed when complete, so a failed dump never looks like a good one.
# The credentials are read INSIDE the containers, from their own environment: .env is never sourced by a shell, where a
# password with a `$` or a space would be mangled.
docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$BACKUP_DIR/db/.luxmap-$stamp.dump.part"
mv "$BACKUP_DIR/db/.luxmap-$stamp.dump.part" "$BACKUP_DIR/db/luxmap-$stamp.dump"
find "$BACKUP_DIR/db" -name 'luxmap-*.dump' -mtime +"$KEEP_DAYS" -delete

docker compose run --rm --no-deps -T -v "$BACKUP_DIR/objects:/backup" --entrypoint /bin/sh minio-mc -c '
  set -e
  mc alias set luxmap http://minio:9000 "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null
  for bucket in luxmap-survey luxmap-evidence luxmap-video luxmap-citizen; do
    mc mirror --quiet --overwrite "luxmap/$bucket" "/backup/$bucket"
  done'

echo "backup $stamp: $(du -sh "$BACKUP_DIR/db/luxmap-$stamp.dump" | cut -f1) database, $(du -sh "$BACKUP_DIR/objects" | cut -f1) objects"
