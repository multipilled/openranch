# Parallel sessions: who owns what

Several sessions work on openranch at once, each in its own git worktree. Edit only what you own.
If you need a change in someone else's area, describe it in your final message (or message that
session) instead of editing it. The coordinator session "SR workstreams" merges and pushes.

| Area | Owner session | Worktree / branch |
|---|---|---|
| game/scripts/SaveLoad (except SaveWriter*); src/OpenRanch.Ranch/PlotLayout.cs and new plot/actor files there; tests/OpenRanch.Ranch.Tests (new files); small hooks in game/scripts/Ranch.cs | SR M3: plot upgrades, contents and loose actors (running) | openranch-m3plots / m3-plots |
| new files src/OpenRanch.Ranch/RanchWriter*.cs, game/scripts/SaveLoad/SaveWriter*.cs, tests for them; a save key/flag hook in game/scripts/Ranch.cs | SR M3: save the live ranch back (running) | openranch-m3save / m3-save |
| game/scripts/World/WorldLighting.cs and new files in game/scripts/World (world clock); sky and fog shaders in game/shaders (not the landscape or paint shaders); docs/behavior/day-and-night.md and day-cycle.md; a small clock hook in game/scripts/Ranch.cs | SR day cycle in game | openranch-daycycle / day-cycle |
| (paused, not merged: clean-room redo pending) landscape and paint shaders | SR Ranch look: shaders and lighting polish | openranch-render / m1-render-polish |
| README.md, OpenRanch.sln (except adding your own new projects), COORDINATION.md, UNVERIFIED.md structure, tools/, merges | coordinator (SR workstreams) | main checkout |

## Board (coordinator keeps this current; a new coordinator starts here)

Main: 06b66c5 (2026-10-08), M0-M2 done, M3 loading merged (`--m3-check` PASS on Game2_4, collision 40/40,
tests 105/105). M3 is done when a save loads fully and the live ranch saves back.

| P | Task | Owner | Status | Done-check |
|---|---|---|---|---|
| P2 | Day cycle in game: running world clock drives sun, sky, fog and night-only objects; starts at the save's world time | SR day cycle in game (openranch-daycycle) | running | headless `--day-check`: clock advances at `secsPerGameDay`, lighting at 4 hours matches the zone settings, night-only objects toggle at the data's hours; collision 40/40 |
| P2 | M3 plots: saved plot upgrades switched on (prefab upgrade children), plot contents (crops, chickens, silo), loose actors (food, plorts, chickens) from the save | SR M3 plots (openranch-m3plots) | running | `--m3-check` extended: upgrades and actor counts by id match the save reader on Game2_4; PASS twice |
| P2 | M3 save back: write the live ranch (money, world time, plots, slimes, actors) to `.ranch.json`; reload gives the same state | SR M3 save (openranch-m3save) | running | `--save X --save-out Y` then `--save Y --m3-check` PASS; writer unit tests round-trip |
| P3 | Render redo, clean room: landscape/paint look from behaviour notes, not translated shaders. m1-render-polish is NEVER merged | unassigned | paused | 5 reference spots closer than now; UNVERIFIED rows closed with sources |
| P3 | Expansion purchase flow (barriers stay solid without `--save`; nothing lifts them yet) | unassigned | later (M4) | buy an expansion in game, barrier lifts, saved |
| P3 | Slime ids with no M2 catalog prefab are skipped; `SavedRanch.Load` opens a second GameScripts | unassigned | later | none skipped on all 15 saves; one load |

Next milestone after M3 (proposed, confirm with Control): **M4, leave The Ranch**: build the next zone (Dry
Reef) from the install, the 15 save plot sites outside zoneRANCH appear there, the player can walk between zones.

Stale folders: D:\Projects\openranch-m3load and openranch-faces are empty but held open (their old
sessions); delete once those sessions are archived.

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
