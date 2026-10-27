#!/usr/bin/env bash
# OPS-OFFSITE-BACKUP — copy $BACKUP_DIR (what backup.sh wrote) to an S3 bucket, so a lost disk no longer loses the backups too.
#   ./backup-offsite.sh         # from this directory, AFTER backup.sh
#   cron: 45 20 * * * cd /opt/luxmap/deploy && ./backup-offsite.sh >> /var/backups/luxmap/backup-offsite.log 2>&1   (03:45 in Vietnam)
#
# One-way and additive: `mc mirror` without --remove only copies what is new or changed and never deletes remotely, so the
# bucket keeps the dumps backup.sh has already pruned here (set the retention as a bucket lifecycle rule, not in this script).
# The key it signs with should be able to put, get and list but NOT delete (vps.md "Sao lưu ra ngoài"): a stolen server must
# not be able to erase its own backups. The OFFSITE_S3_* values come from .env through compose, never through this shell.
set -euo pipefail
cd "$(dirname "$0")"

BACKUP_DIR="${BACKUP_DIR:-/var/backups/luxmap}"
[ -d "$BACKUP_DIR/db" ] || { echo "no backup in $BACKUP_DIR: run ./backup.sh first" >&2; exit 1; }

docker compose run --rm --no-deps -T -v "$BACKUP_DIR:/backup:ro" --entrypoint /bin/sh minio-mc -c '
  set -e
  [ -n "$OFFSITE_S3_BUCKET" ] && [ -n "$OFFSITE_S3_ACCESS_KEY" ] && [ -n "$OFFSITE_S3_SECRET_KEY" ] || {
    echo "OFFSITE_S3_BUCKET, OFFSITE_S3_ACCESS_KEY and OFFSITE_S3_SECRET_KEY must be set in .env" >&2; exit 1; }
  mc alias set offsite "$OFFSITE_S3_ENDPOINT" "$OFFSITE_S3_ACCESS_KEY" "$OFFSITE_S3_SECRET_KEY" >/dev/null
  mc mirror --quiet --overwrite /backup "offsite/$OFFSITE_S3_BUCKET/luxmap"'

echo "offsite $(date -u +%Y%m%dT%H%M%SZ): $BACKUP_DIR mirrored"
