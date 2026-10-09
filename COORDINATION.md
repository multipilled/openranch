# Parallel sessions: who owns what

Several sessions work on openranch at once, each in its own git worktree. Edit only what you own.
If you need a change in someone else's area, describe it in your final message (or message that
session) instead of editing it. The coordinator session "SR workstreams" merges and pushes.

| Area | Owner session | Worktree / branch |
|---|---|---|
| game/scripts/SaveLoad; src/OpenRanch.Ranch (RanchWriter, ranch state); game/scripts/Slimes/M2World.cs; game/scripts/Vacpack; game/scripts/Market; new tests; small hooks in game/scripts/Ranch.cs and a position setter in Player/PlayerController.cs | SR M3 rest (proposed) | openranch-m3rest / m3-rest |
| src/OpenRanch.Formats/Scene (zone extraction, except the `Hidden` set); game/scripts/World (except WorldLighting's clock); game/scripts/CollisionCheck.cs; new docs/behavior/zones.md; a `--zone` hook in game/scripts/Ranch.cs | SR M4: every zone builds and is walkable (running) | openranch-m4zones / m4-zones |
| (paused, not merged: clean-room redo pending) landscape and paint shaders | SR Ranch look: shaders and lighting polish | openranch-render / m1-render-polish |
| README.md, OpenRanch.sln (except adding your own new projects), COORDINATION.md, UNVERIFIED.md structure, tools/, merges | coordinator (SR workstreams) | main checkout |

## Board (coordinator keeps this current; a new coordinator starts here)

**Full goal:** all of Slime Rancher playable 1:1 from start to finish, running from the user's own install.

Milestones, weighted by size (out of 100). Progress = the sum of finished weight. Report to Control at every 10%
(row in D:\Local Projects\Master\PROGRESS.md).

| M | Milestone | Weight | Done | State |
|---|---|---|---|---|
| M0 | Foundation: Unity readers, importer, v12 save reader | 5 | 5 | done |
| M1 | Walk The Ranch: world, collision, zone lighting | 8 | 7 | works; the clean-room look redo is still open |
| M2 | Core loop: slimes eat and make plorts, vacpack, corrals, plort market | 8 | 7 | 3 slime types; tabby pounce and plort lifetime open |
| M3 | Ranch save: full load, save back, running day cycle | 6 | 6 | done; M3 rest (vacpack/market/player saved, sleep, one clock) is polish |
| M4 | Every zone walkable: Dry Reef, Indigo Quarry, Moss Blanket, Ancient Ruins, Glass Desert, Wilds, secret areas, teleporters | 18 | 0 | next |
| M5 | Every slime, largo, gordo and tarr; feral; food, crops and chickens growing | 15 | 0 | |
| M6 | Ranch economy: building and upgrading plots, expansions, Lab (refinery, fabricator, gadgets), Exchange, 7Zee | 15 | 0 | |
| M7 | Game flow and UI: menus, HUD, vacpack UI, Slimepedia, mail, options, save slots | 10 | 0 | |
| M8 | Sound and look: music, sound effects, clean-room landscape, foliage, water and sky | 8 | 1 | sound and text readers done |
| M9 | Story and completion: Ogden, Mochi, Viktor, Slimeulations, achievements, ending | 7 | 0 | |
| | **Total** | **100** | **26** | |

Main (2026-10-09): M0-M2 done; M3 loading, save back (`--save-out`, F5) and the running day cycle merged.
M3 plots merged too: upgrades, crops, loose actors. On main: `--m3-check` PASS (Game2_4, Logansfarm_5, re-saved
.ranch.json), `--day-check`, `--m2-check` PASS, collision 40/40, tests 134/134. M3 done.

| P | Task | Owner | Status | Done-check |
|---|---|---|---|---|
| P2 | Day cycle in game: running world clock drives sun, sky, fog and night-only objects; starts at the save's world time | SR day cycle in game | merged | headless `--day-check`: clock advances at `secsPerGameDay`, lighting at 4 hours matches the zone settings, night-only objects toggle at the data's hours; collision 40/40 |
| P2 | M3 plots: saved plot upgrades switched on (prefab upgrade children), plot contents (crops, chickens, silo), loose actors (food, plorts, chickens) from the save | SR M3 plots | merged | `--m3-check` extended: upgrades and actor counts by id match the save reader on Game2_4; PASS twice |
| P2 | M3 save back: write the live ranch (money, world time, plots, slimes, actors) to `.ranch.json`; reload gives the same state | SR M3 save | merged | `--save X --save-out Y` then `--save Y --m3-check` PASS; writer unit tests round-trip |
| P2 | M4 zones: every main zone (Dry Reef, Indigo Quarry, Moss Blanket, Ancient Ruins, Glass Desert, Wilds) builds from the install with `--zone`, with its ambience; the player can walk it | SR M4 zones (openranch-m4zones) | running | `--zone X --collision-check` PASS for each zone; one off-screen capture per zone looks like the original's |
| P2 | M4 one world: zones join into one walkable world (neighbours load as you cross, or all at once), teleporters work, the save's 15 outside plot sites appear | after M4 zones | later | walk Ranch to Dry Reef in one run; outside plots match the save |
| P2 | M3 rest: M2's clock takes its time from the world clock; vacpack contents, market saturation and player position save and load; sleeping in the ranch house; slimes skip unripe hanging produce | SR M3 rest (openranch-m3rest) | proposed | round trip keeps vacpack, market and player; sleep skips to morning |
| P2 | Produce ripens, rots and regrows on the clock; feeders, plort collectors, silos and ash run (M5/M6) | unassigned | later | produce cycle matches the data's times |
| P3 | Render redo, clean room: landscape/paint look from behaviour notes, not translated shaders. m1-render-polish is NEVER merged | unassigned | paused | 5 reference spots closer than now; UNVERIFIED rows closed with sources |
| P3 | Expansion purchase flow (barriers stay solid without `--save`; nothing lifts them yet) | unassigned | later (M4) | buy an expansion in game, barrier lifts, saved |
| P3 | Slime ids with no M2 catalog prefab are skipped; `SavedRanch.Load` opens a second GameScripts | unassigned | later | none skipped on all 15 saves; one load |

Next milestone: **M4, every zone walkable** (weight 18), started with M4 zones.

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
