#!/usr/bin/env bash
# OPS-AUTO-DEPLOY — forced command of the GitHub Actions deploy key (.github/workflows/deploy.yml, docs/deploy/update.md A).
# Installed on the server as ONE line of ~ubuntu/.ssh/authorized_keys:
#   restrict,command="/opt/luxmap/deploy/ci-deploy.sh" ssh-ed25519 AAAA… luxmap-backend-ci-deploy
# The key holder can choose only WHICH commit of origin/dev to deploy: the SSH command must be one full commit SHA.
# No shell, no forwarding, no other path. Rolling back is a revert on dev, never a checkout of an older commit.
set -euo pipefail

if [ "${1:-}" = --run ]; then
  sha="$2"
  cd "$(dirname "$0")/.."
  trap 'echo $? > "$3.status"' EXIT
  # One deploy at a time: a second push waits here instead of building over the first.
  exec 9> "$HOME/.luxmap-deploy.lock"
  flock -w 3600 9
  [ "$(git symbolic-ref --short HEAD)" = dev ] || { echo "the server checkout is not on dev" >&2; exit 1; }
  # Same rule as the manual procedure: local edits on the server stop the deploy; they are never discarded.
  if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
    echo "the server checkout has local changes — ask the team, do not discard them:" >&2
    git status --short --untracked-files=no >&2
    exit 1
  fi
  git fetch --quiet origin dev
  git merge-base --is-ancestor "$sha" origin/dev || { echo "$sha is not on origin/dev" >&2; exit 1; }
  # Only forward: a late or re-run job must never put an older build back over a newer one.
  git merge-base --is-ancestor HEAD "$sha" || {
    echo "server is at $(git rev-parse --short HEAD), which $sha does not contain — refusing to go back" >&2
    exit 1
  }
  git merge --ff-only --quiet "$sha"
  echo "deploying $(git log -1 --format='%h %s')"
  deploy/deploy.sh
  echo "deployed $(git rev-parse HEAD)"
  exit 0
fi

sha="${SSH_ORIGINAL_COMMAND:-}"
[[ "$sha" =~ ^[0-9a-f]{40}$ ]] || { echo "usage: ssh <server> <full commit sha on origin/dev>" >&2; exit 64; }

log="$HOME/deploy-$(date -u +%Y%m%dT%H%M%SZ)-${sha:0:7}.log"
: > "$log"
# Detached from the SSH session: a dropped connection or a cancelled job must not kill deploy.sh between the backup and
# the migration. The run goes on; this side only follows its log. `setsid -w` lives exactly as long as the run.
setsid -w "$0" --run "$sha" "$log" >> "$log" 2>&1 < /dev/null &
tail -n +1 --pid=$! -f "$log"
exit "$(cat "$log.status" 2>/dev/null || echo 1)"
