# Parallel sessions: who owns what

Several sessions work on openranch at once, each in its own git worktree. Edit only what you own.
If you need a change in someone else's area, describe it in your final message (or message that
session) instead of editing it. The coordinator session "SR workstreams" merges and pushes.

| Area | Owner session | Worktree / branch |
|---|---|---|
| src/OpenRanch.Formats/Scene; game/scripts/World (except WorldLighting's clock); game/scripts/CollisionCheck.cs; docs/behavior/zones.md; teleporter/region code; a small hook in game/scripts/Ranch.cs | SR M4 one world (proposed) | openranch-m4world / m4-world |
| game/scripts/Slimes (except M2World.cs); src/OpenRanch.Simulation slime rules; new Simulation tests; docs/behavior/slimes.md, largos.md, slime-traits.md; new gordo/feral files | SR M5 gordos, feral, abilities (proposed) | openranch-m5abilities / m5-abilities |
| game/scripts/SaveLoad; src/OpenRanch.Ranch; game/scripts/Slimes/M2World.cs; game/scripts/Market; new game/scripts/Ranch* UI files for plots and expansions; docs/behavior/plots.md, ranch-saves.md; new tests | SR M6 plots and expansions (proposed) | openranch-m6plots / m6-plots |
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
| M4 | Every zone walkable: Dry Reef, Indigo Quarry, Moss Blanket, Ancient Ruins, Glass Desert, Wilds, secret areas, teleporters | 18 | 7 | all 14 zone roots build and walk 40/40 alone; joining into one world next |
| M5 | Every slime, largo, gordo and tarr; feral; food, crops and chickens growing | 15 | 5 | all 113 slime definitions (91 largos), largo and tarr forming; gordos, feral, abilities next |
| M6 | Ranch economy: building and upgrading plots, expansions, Lab (refinery, fabricator, gadgets), Exchange, 7Zee | 15 | 0 | |
| M7 | Game flow and UI: menus, HUD, vacpack UI, Slimepedia, mail, options, save slots | 10 | 0 | |
| M8 | Sound and look: music, sound effects, clean-room landscape, foliage, water and sky | 8 | 1 | sound and text readers done |
| M9 | Story and completion: Ogden, Mochi, Viktor, Slimeulations, achievements, ending | 7 | 0 | |
| | **Total** | **100** | **38** | |

Main (2026-10-09): M0-M2 done; M3 loading, save back (`--save-out`, F5) and the running day cycle merged.
Main (2026-10-09): M3 rest, M4 zones and M5 slimes merged. On main: `--m3-check` PASS (Game2_4, Logansfarm_5,
re-saved .ranch.json), `--day-check`, `--sleep-check`, `--m2-check`, `--slime-zoo` PASS, collision 40/40 on The Ranch and
the main zones. Finished tasks are in git history; this table holds open work only.

| P | Task | Owner | Status | Done-check |
|---|---|---|---|---|
| P2 | M4 one world: build zone roots side by side (shared coordinates), load/unload cells by region box, region sets (Far, Far Range vs Slimeulations/Viktor), teleporters wired source to destination, kill volumes across zones, a player saved outside The Ranch starts where they saved, the save's 15 outside plot sites appear | SR M4 one world (openranch-m4world) | proposed | walk and jetpack from The Ranch into the Dry Reef in one run; a teleporter trip lands on its destination; `--save Game2_4` starts in the Glass Desert; outside plots match the save; collision PASS |
| P2 | M5 gordos, feral and abilities: gordos (feeding, bursting, rewards), feral slimes, phosphor vanishing outside 18-6, Boom/Rad/Crystal/Quantum/Dervish/Tangle/Hunter/Mosaic abilities, feeding behaviours (tabby/saber pounce, gold fleeing, lucky coins, fire eats ash, puddle drinks water) | SR M5 gordos, feral, abilities (openranch-m5abilities) | proposed | `--slime-zoo` extended: each ability fires and a gordo bursts after its meals; PASS twice |
| P2 | M6 plots and expansions: build, demolish and upgrade plots at a plot site for the data's prices; buy expansions (barriers lift); produce ripens, rots and regrows; feeders, plort collectors, silos, ash run; save on sleep through a minimal ranch house screen | SR M6 plots and expansions (openranch-m6plots) | proposed | headless `--m6-check`: build a corral and upgrade it, buy an expansion, money drops by the data's prices, produce cycles at the data's times, save-out round-trips; m3-check PASS |
| P2 | Scripted vac-and-save check: suck up a slime, save-out, reload, slime is in the vacpack | unassigned | later | check PASS |
| P3 | Render redo, clean room (landscape/paint look from behaviour notes). m1-render-polish is NEVER merged. Also glitch walls (flat white), Reef Hub terrain not drawn, transparent slime auras, mosaic glass | unassigned | paused | 5 reference spots closer than now; UNVERIFIED rows closed with sources |
| P3 | `--m3-check` market line fails on a save made just before midnight; slime fear not modelled; `SavedRanch.Load` opens a second GameScripts | unassigned | later | |

Running now: M4 one world, M5 gordos/feral/abilities, M6 plots and expansions (M6 started early: it doesn't overlap M4/M5 files).

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
