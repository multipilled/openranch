# Parallel sessions: who owns what

Several sessions work on openranch at once, each in its own git worktree. Edit only what you own.
If you need a change in someone else's area, describe it in your final message (or message that
session) instead of editing it. The coordinator session "SR workstreams" merges and pushes.

| Area | Owner session | Worktree / branch |
|---|---|---|
| game/scripts/SaveLoad; new files in src/OpenRanch.Ranch and tests/OpenRanch.Ranch.Tests; the `Hidden` set in src/OpenRanch.Formats/Scene/ZoneExtractor.cs; small hooks in game/scripts/Ranch.cs | SR M3: load a ranch save into The Ranch | openranch-m3load / m3-load-save |
| game/scripts/World/WorldLighting.cs and new files in game/scripts/World (world clock); sky and fog shaders in game/shaders (not the landscape or paint shaders); docs/behavior/day-and-night.md and day-cycle.md; a small clock hook in game/scripts/Ranch.cs | SR day cycle in game | openranch-daycycle / day-cycle |
| (paused, not merged: clean-room redo pending) landscape and paint shaders | SR Ranch look: shaders and lighting polish | openranch-render / m1-render-polish |
| README.md, OpenRanch.sln (except adding your own new projects), COORDINATION.md, UNVERIFIED.md structure, tools/, merges | coordinator (SR workstreams) | main checkout |

## Rules

1. Heavy jobs (builds, tests, Godot runs, big scans) go through `tools/with_cpu.sh <you> ...`: machine-wide
   slots (3 by default) shared with every session of every project on this PC, at below-normal priority.
   Anything that runs Godot also goes through `tools/with_lock.sh <you> ...` (one Godot run at a time
   across all openranch worktrees). In bash, use dash-style MSBuild switches so they aren't mangled:
   `tools/with_cpu.sh <you> dotnet build OpenRanch.sln -m:2 -nodeReuse:false -p:UseSharedCompilation=false`.
   Never pipe a heredoc into them; write scripts to files. If a slot or lock is held, wait; never kill
   another session's process.
2. Re-read a file right before editing it. Use exact-match edits; never rewrite a whole shared file.
3. Keep screenshots and logs in your own folder outside the repo (they show the game's art).
4. Changing a shared signature means a message to the other sessions first; add new parameters at the
   end with defaults.
5. Godot runs only via tools/godot-capture.ps1 or tools/godot-headless.ps1: off-screen, unfocusable, silent.
6. Never assume: every value comes from the game's data or static analysis and is cited in a comment;
   anything unsettled goes into UNVERIFIED.md (or your final message, if you don't own that file's row).
7. Decompiled code and RE notes are in the git-ignored gamedata/ of the main checkout
   (D:\Projects\openranch\gamedata, index gamedata/re/SPEC.md). Read only; never copy into tracked files.
8. Never commit game data; the commit guard must pass.

## Knowledge library and who starts sessions (user, 2026-10-08)
- **Knowledge library first:** `D:\Local Projects\Knowledge` (`knowledge` skill). Before reverse-engineering, decoding a
  format, picking an engine approach or debugging a known kind of problem, grep its `index/` and `MAP.md`; every brief
  links the relevant notes. When a task ends, add anything another project could reuse (extend the existing note,
  otherwise the template) and run `python tools/lint.py` there.
- **Workers nest under this project's "TAG workstreams" coordinator:** only the coordinator starts workers
  (`start_session`), so they show under it in the sidebar. Control (`D:\Local Projects\Master`) sets priorities and
  messages the coordinator; it never starts this project's workers itself.
- Every session runs on Opus 5.5 with the 1M window; no hand-overs because of context size.
