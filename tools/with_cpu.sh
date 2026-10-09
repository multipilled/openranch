#!/usr/bin/env bash
# Machine-wide CPU slots: one shared copy for every project. Usage, rules and reclaim logic live there:
# C:\Users\eynon\.claude\bin\with_cpu.sh   (with_cpu.sh <session-name> <command...>; never pass a script on stdin)
exec bash "$HOME/.claude/bin/with_cpu.sh" "$@"
