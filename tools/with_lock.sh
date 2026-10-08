#!/usr/bin/env bash
# Serializes everything that touches a shared build artifact or runs the game (e.g. a native plugin DLL
# that a running engine keeps locked, or one engine instance at a time). Shared by all worktrees of the
# repo (the lock lives next to the common .git directory). Generalized from the DI2 project.
#   tools/with_lock.sh <session-name> <command...>      LOCK_NAME=<name> picks a different lock (default game)
# A lock older than 40 minutes is treated as stale (crashed holder) and removed.
common=$(git rev-parse --path-format=absolute --git-common-dir 2>/dev/null || echo "$PWD/.git")
LOCK="$(dirname "$common")/.locks/${LOCK_NAME:-game}"
mkdir -p "$(dirname "$LOCK")"
owner="$1"; shift
announced=0
until mkdir "$LOCK" 2>/dev/null; do
  if [ -n "$(find "$LOCK" -maxdepth 0 -mmin +40 2>/dev/null)" ]; then
    echo "with_lock: removing stale lock held by $(cat "$LOCK/owner" 2>/dev/null)"; rm -rf "$LOCK"; continue
  fi
  if [ $announced -eq 0 ]; then echo "with_lock: waiting for lock held by $(cat "$LOCK/owner" 2>/dev/null)"; announced=1; fi
  sleep 3
done
echo "$owner $(date +%T) $(pwd) $*" > "$LOCK/owner"
trap 'rm -rf "$LOCK"' EXIT
"$@"
