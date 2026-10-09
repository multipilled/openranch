# Parallel sessions: who owns what

Several sessions work on openranch at once, each in its own git worktree. Edit only what you own.
If you need a change in someone else's area, describe it in your final message (or message that
session) instead of editing it. The coordinator session "SR workstreams" merges and pushes.

| Area | Owner session | Worktree / branch |
|---|---|---|
| game/scripts/SaveLoad; game/scripts/RanchEconomy; game/scripts/Home; game/scripts/Slimes/M2World.cs, ItemCatalog.cs, SlimeZoo.cs, ZooTrial.cs; src/OpenRanch.Ranch; the joined-world hooks in game/scripts/World/WorldMap.cs; game/scripts/Ranch.cs | SR integration: saves and checks across M4/M5/M6 (proposed) | openranch-integ / integ |
| new game/scripts/UI (menus, HUD, vacpack UI, Slimepedia, mail); game/scripts/Main.cs; game/scripts/Market/Hud.cs; scenes for menus; docs/behavior/ui.md | SR M7 menus and HUD (proposed) | openranch-m7ui / m7-ui |
| new game/scripts/Audio; src/OpenRanch.Formats audio readers (existing); docs/behavior/audio.md; one-line sound hooks in others' files listed in the final message | SR M8 sound in game (proposed) | openranch-m8audio / m8-audio |
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
| M4 | Every zone walkable: Dry Reef, Indigo Quarry, Moss Blanket, Ancient Ruins, Glass Desert, Wilds, secret areas, teleporters | 18 | 12 | one joined world: cells stream by region, teleporters, saves start where the player saved, all 41 plot sites; left: Quarry route, Ruins temple exit, zone spawners, fall damage |
| M5 | Every slime, largo, gordo and tarr; feral; food, crops and chickens growing | 15 | 10 | all slimes, largos, tarr, 14 gordos, feral, abilities, feeding habits; produce and coops (M6); left: zone spawners, effects, glitch/quicksilver, water calming |
| M6 | Ranch economy: building and upgrading plots, expansions, Lab (refinery, fabricator, gadgets), Exchange, 7Zee | 15 | 6 | build/upgrade plots, expansions, produce, coops, silo, feeder, collector, incinerator, save on sleep; left: upgrade effects, ponds, Lab, Exchange, 7Zee |
| M7 | Game flow and UI: menus, HUD, vacpack UI, Slimepedia, mail, options, save slots | 10 | 0 | |
| M8 | Sound and look: music, sound effects, clean-room landscape, foliage, water and sky | 8 | 1 | sound and text readers done |
| M9 | Story and completion: Ogden, Mochi, Viktor, Slimeulations, achievements, ending | 7 | 0 | |
| | **Total** | **100** | **54** | |

Main (2026-10-09): M0-M2 done; M3 loading, save back (`--save-out`, F5) and the running day cycle merged.
Main (2026-10-09): M4 one world, M5 gordos/feral/abilities and M6 plots/expansions/produce merged (redo workers after the
power loss). On main: `--m3-check` PASS (Game2_4, Logansfarm_5 x3), `--world-check`, `--save-check`, `--day-check`,
`--sleep-check`, `--m2-check` PASS, `--m6-check` PASS (run it as `--new-game --money 20000 --day-speed 720 --m6-check
--sleep-save FILE`), collision 40/40, tests 225/225. Known failures are the P1 row below.

| P | Task | Owner | Status | Done-check |
|---|---|---|---|---|
| P1 | Integration across M4/M5/M6: (a) `--save Game2_4 --save-out X` from the joined world (player in the Desert), then `--save X --m3-check`: 10 loose actors fall, every time (from The Ranch alone it passes); (b) the `--m6-check` sleep save reloaded with `--m3-check`: 6 loose actors fall; (c) `--slime-zoo` is flaky (94-750 s; GOLD vanished once; CRYSTAL/QUANTUM/DERVISH/TANGLE trials failed in the joined world); (d) `--m6-check` without `--sleep-save` hangs instead of failing; (e) `M3Check` plot-ground test should skip plots in unloaded cells so it can run in the joined world; (f) wire `Catalog.InCave`, `OnRanchOrWilds` and `Patches` (ash troughs from the incinerator, water) in M2World; (g) Logansfarm_5 slime-outside-corral flake | SR integration (openranch-integ) | proposed | (a), (b) PASS 3 of 3; `--slime-zoo` PASS 3 of 3 under 150 s; (d) fails fast; `--m3-check` also runs in the joined world; fire slimes eat incinerator ash in game |
| P2 | M7 menus and HUD: main menu, new game / load game slots, pause, options, the original's HUD (money, clock, health/energy, vacpack slots) from its UI data, Slimepedia, mail | SR M7 menus and HUD (openranch-m7ui) | proposed | `--ui-check`: start a new game and load a save through the menus, HUD shows the save's money/time/slots; captures look like the original's layout |
| P2 | M8 sound in game: zone and time-of-day music from the install's music data, ambience, and sound effects for slimes, vacpack, market, plots, through one event bus | SR M8 sound in game (openranch-m8audio) | proposed | `--audio-check`: the right track plays per zone and hour; each hooked event plays its clip from the install |
| P2 | M5 zones come alive: DirectedActorSpawner (wild and feral slimes), wild produce, zone gordos at their scene spots, slimes outside corrals in saves | unassigned | after integration (shares Slimes files) | slimes and food appear in each zone at the data's rates |
| P2 | M6 upgrades and Lab: what plot upgrades do (walls, music box, air net, solar shield, scareslime, miracle mix), ponds, silo/feeder buttons; then Lab: refinery, fabricator, gadgets | unassigned | after integration (shares SaveLoad files) | upgrade effects match the data; fabricate a gadget from refinery stock |
| P2 | M4 rest: on-foot route Reef to Quarry (or confirm jetpack), Ruins temple exit, fall damage | unassigned | later | |
| P3 | Render redo, clean room. m1-render-polish is NEVER merged. Also glitch walls, Reef Hub terrain, slime auras/effects, mosaic glass | unassigned | paused | |

Next: the integration worker first (P1), with M7 menus/HUD and M8 sound in parallel (separate files).

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
