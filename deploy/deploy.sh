#!/usr/bin/env bash
# OPS-DEPLOY-VPS — build, back up, migrate, start. Run from the checkout on the server, after `git pull`:
#   cd /opt/luxmap && git pull && deploy/deploy.sh
# The order is the point: the migration runs only after a backup succeeded, and the new API starts only after the
# migration succeeded (`set -e` stops at the first failure, leaving the running API untouched).
set -euo pipefail
cd "$(dirname "$0")"

[ -f .env ] || { echo "deploy/.env is missing: cp .env.example .env, then fill it in" >&2; exit 1; }
if grep -q 'CHANGE_ME' .env; then
  echo "deploy/.env still has CHANGE_ME values" >&2
  exit 1
fi
mkdir -p web
# backup.sh runs before the migration as this user; fail here, before the build, not halfway through (vps.md step 4).
backup_dir="${BACKUP_DIR:-/var/backups/luxmap}"
if ! { mkdir -p "$backup_dir" 2>/dev/null && [ -w "$backup_dir" ]; }; then
  echo "cannot write $backup_dir: sudo mkdir -p $backup_dir && sudo chown \$USER $backup_dir" >&2
  exit 1
fi

docker compose build api
docker compose up -d --wait postgres minio
docker compose run --rm minio-mc
./backup.sh
docker compose run --rm migrate
docker compose up -d --remove-orphans api caddy
docker compose ps
