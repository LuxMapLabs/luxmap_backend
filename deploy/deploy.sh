#!/usr/bin/env bash
# OPS-DEPLOY-VPS — build, back up, migrate, start. Every push to dev runs it through ci-deploy.sh (OPS-AUTO-DEPLOY);
# by hand only when GitHub Actions cannot: cd /opt/luxmap && git pull --ff-only && deploy/deploy.sh
# The order is the point: the migration runs only after a backup succeeded, and the new API starts only after the
# migration succeeded (`set -e` stops at the first failure, leaving the running API untouched).
set -euo pipefail
cd "$(dirname "$0")"

[ -f .env ] || { echo "deploy/.env is missing: cp .env.example .env, then fill it in" >&2; exit 1; }
# Comments are skipped: the template's own header mentions CHANGE_ME, so a fully filled .env would never pass.
unfilled="$(grep -v '^[[:space:]]*#' .env | grep 'CHANGE_ME' | cut -d= -f1 || true)"
if [ -n "$unfilled" ]; then
  echo "deploy/.env still has CHANGE_ME values:" $unfilled >&2
  exit 1
fi
mkdir -p web
# backup.sh runs before the migration as this user; fail here, before the build, not halfway through (vps.md step 4).
backup_dir="${BACKUP_DIR:-/var/backups/luxmap}"
if ! { mkdir -p "$backup_dir" 2>/dev/null && [ -w "$backup_dir" ]; }; then
  echo "cannot write $backup_dir: sudo mkdir -p $backup_dir && sudo chown \$USER $backup_dir" >&2
  exit 1
fi

docker compose build api iot-bridge
docker compose up -d --wait postgres minio
docker compose run --rm minio-mc
./backup.sh
docker compose run --rm migrate
docker compose up -d --remove-orphans api caddy iot-bridge
docker compose ps
# Each build leaves ~1.7 GB of build cache and nothing else removes it; on the 38 GB VPS that filled the disk (80% on
# 24/11/2026, 21.7 GB of cache). Prune only after the new API is up, so a failed deploy keeps its cache for the retry;
# keep the newest 3 GB so the next build still reuses the ffmpeg / restore layers. The deploy already succeeded here,
# so a prune error only warns.
docker builder prune -f --reserved-space 3gb || echo "warning: docker builder prune failed; free space by hand (docs/deploy/update.md)" >&2
