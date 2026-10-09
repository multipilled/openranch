#!/usr/bin/env bash
# Machine-wide CPU budget shared by every session of every project: heavy jobs (builds, engine runs,
# Ghidra headless, exporters, big scans) take one of $CPU_SLOTS slots and run at below-normal priority
# (re-applied to late children), so the desktop stays responsive. Generalized from the DI2 project.
#   tools/with_cpu.sh <session-name> <command...>
# Combine with with_lock.sh for anything that touches a shared build artifact or runs the game:
#   tools/with_cpu.sh me tools/with_lock.sh me <command...>
# Never pipe a heredoc into it (`with_cpu.sh me python - <<EOF`): the job runs in the background, the
# heredoc never arrives and the interpreter hangs holding a slot. Write the script to a file instead.
# Stale slots (holder gone for > 40 min) are reclaimed. If a job of yours hangs, release only its own
# slot dir (check its owner file first).
SLOTS=${CPU_SLOTS:-4}
DIR=${CPU_SLOT_DIR:-$HOME/.claude/cpu-slots}
mkdir -p "$DIR"
owner="$1"; shift
slot=""
announced=0
while [ -z "$slot" ]; do
  for i in $(seq 1 "$SLOTS"); do
    if mkdir "$DIR/$i" 2>/dev/null; then slot=$i; break; fi
    if [ -n "$(find "$DIR/$i" -maxdepth 0 -mmin +40 2>/dev/null)" ]; then rm -rf "$DIR/$i"; fi
  done
  if [ -z "$slot" ]; then
    [ $announced -eq 0 ] && echo "with_cpu: waiting for a CPU slot ($(cat "$DIR"/*/owner 2>/dev/null | tr '\n' ';'))" && announced=1
    sleep 5
  fi
done
echo "$owner $(date +%T) $(pwd) $*" > "$DIR/$slot/owner"
trap 'rm -rf "$DIR/$slot"' EXIT
"$@" &
pid=$!
winpid=$(cat /proc/$pid/winpid 2>/dev/null)
lower() {
  [ -n "$winpid" ] && powershell.exe -NoProfile -Command "
    function Low(\$id) { try { (Get-Process -Id \$id -ErrorAction Stop).PriorityClass = 'BelowNormal' } catch {}
      Get-CimInstance Win32_Process -Filter \"ParentProcessId=\$id\" | ForEach-Object { Low \$_.ProcessId } }
    Low $winpid" >/dev/null 2>&1
}
( for t in 1 5 20 60; do sleep $t; kill -0 $pid 2>/dev/null || exit 0; lower; done ) &
watcher=$!
lower
wait $pid
code=$?
kill $watcher 2>/dev/null
exit $code
