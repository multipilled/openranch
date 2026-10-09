#!/usr/bin/env bash
# Machine-wide CPU slots: one shared copy for every project. Usage, rules and reclaim logic live there:
# ~/.claude/bin/with_cpu.sh (see github.com/multipilled/claude-slot-lanes;  with_cpu.sh <session-name> <command...>; never pass a script on stdin)
exec bash "$HOME/.claude/bin/with_cpu.sh" "$@"
