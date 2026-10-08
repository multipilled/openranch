# Parallel sessions: who owns what

Several sessions work on openranch at once, each in its own git worktree. Edit only what you own.
If you need a change in someone else's area, describe it in your final message (or message that
session) instead of editing it. The coordinator session "SR workstreams" merges and pushes.

| Area | Owner session | Worktree / branch |
|---|---|---|
| game/scripts/Slimes, Vacpack, Market; src/OpenRanch.Simulation; tests/OpenRanch.Simulation.Tests; new files in src/OpenRanch.Formats/Game; small hooks in game/scripts/Ranch.cs and Player/PlayerController.cs | SR slimes, vacpack and market in game | openranch-m2 / m2-in-game |
| game/shaders, game/scripts/World, src/OpenRanch.Formats/Scene; rendering notes in docs/behavior | SR Ranch look: shaders and lighting polish | openranch-render / m1-render-polish |
| src/OpenRanch.Ranch, tests/OpenRanch.Ranch.Tests, docs/formats/openranch-saves.md, plot and ranch notes | SR ranch state and save import | openranch-m3 / m3-ranch-state |
| src/OpenRanch.Formats/Audio, src/OpenRanch.Formats/Text, docs/formats/audio.md and text.md, importer `sounds` and `text` commands | SR sound and text readers | openranch-assets / assets-audio-text |
| README.md, OpenRanch.sln (except adding your own new projects), COORDINATION.md, UNVERIFIED.md structure, tools/, merges | coordinator (SR workstreams) | main checkout |

## Rules

1. Heavy jobs (builds, tests, Godot runs, big scans) go through `tools/with_cpu.sh <you> ...`: machine-wide
   slots (2 by default) shared with every session of every project on this PC, at below-normal priority.
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
