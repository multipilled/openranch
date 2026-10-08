# openranch

An open source rewrite of the engine and game logic of Slime Rancher (Monomi Park, 2017), built
with Godot 4.7 and C#. It plays the game using the models, textures, sounds and text from **your
own copy** of Slime Rancher, which the importer reads on your PC. Nothing from the game is in this
repository, and nothing from the game is ever committed.

You need a legal copy of Slime Rancher (the Steam release, 1.4.x) to use openranch.

openranch is not affiliated with or endorsed by Monomi Park.

## Status

Milestone 0, the foundation, is done:

- `openranch-import inventory` reads the game's 11 Unity data files (Unity 2019.4.29f1) and
  counts every asset by type.
- `openranch-import saves --verify` reads your saves and re-writes each supported one in memory to
  prove the reader understands every byte. All 15 version-12 saves on the development PC re-write
  byte for byte. Saves from before game 1.4 (versions 8 and 9) are listed but not read yet.
- The Godot project starts and lists the ranches it found.

Milestone 1, walking around The Ranch, is in progress:

- The world scene is read straight from your install: The Ranch's 7,241 objects become 4,636
  rendered instances (static batches, multimeshes) and 2,291 collision shapes in about 2 seconds.
- Crunched textures (Unity's DXT1/DXT5 "crunch" format) are decoded by a C# port of crunch's
  decoder; everything else goes to the GPU as stored.
- A first-person controller with a jetpack uses the original player's capsule size and slope limit.
- `--collision-check` drops the player across the area's ground and checks it lands every time (40 of 40).
- Objects the game switches on and off at runtime follow the same rules: ranch upgrades by progress,
  night-only decorations by the hour, gadget-site markers by gadget mode. `--save FILE` shows the
  world as in one of your saves (read only).
- Sky, fog, ambient light and the sun follow the original's zone settings and time of day, including
  cave lighting. Fog is drawn with the original's formula in a full-screen pass.
- Reference check: screenshots from 5 fixed spots (home deck, corrals, overgrowth, grotto, docks)
  line up with the original game's view of the same spots. Colours still differ in places (ranch
  palette recolouring, landscape detail textures, clouds).
- Script data: every one of the game's 37,299 script components (758 script types, from slime
  diets to zone ambience) reads cleanly, with layouts worked out at runtime from the game's own
  assemblies through metadata. No game code is loaded or run. `openranch-import scripts --dump
  SlimeDefinition` shows an example.

Milestone 2, vacpack and slimes, works in The Ranch: Pink, Tabby and Rock slimes (built from the
game's own item templates) hop, eat and make plorts; the vacpack has 4 slots to suck up and shoot
items; corral walls keep slimes in; the plort market buys plorts at the price read from your copy.
`--m2-check` runs it headless: a Pink slime eats a carrot, its plort is vacuumed up and sold, and a
Rock slime thrown at the corral wall stays in. The rules underneath, headless:

- `src/OpenRanch.Simulation` holds slime hunger and eating, the 4-slot vacpack and the plort market
  with saturation, as plain .NET. Diets, favorites, plorts, eating tuning and market prices are read
  from your install (`SlimeData`, `MarketData`); the rules are in `docs/behavior/`.
- A test feeds a Pink slime a carrot, vacuums up its plort and sells it at the price read from your
  copy.

Sound and text: `openranch-import sounds` reads all 953 of the game's sound clips and converts them
(FMOD banks to Ogg/WAV, in memory or exported to a folder outside the repo), and `openranch-import
text` reads its 101 text bundles in every language. Formats are in `docs/formats/audio.md` and
`text.md`.

The full plan, with milestones M0 to M5, is in the project's rewrite plan document.

## Layout

| Folder | What it holds |
| --- | --- |
| `src/OpenRanch.Formats` | Readers for Unity serialized files and Slime Rancher saves, plain .NET 8, tested without Godot |
| `src/OpenRanch.Simulation` | Game rules (slimes, vacpack, plort market), plain .NET 8, no engine dependency |
| `src/OpenRanch.Importer` | The `openranch-import` command |
| `game/` | The Godot 4.7 project (C#) |
| `tests/` | Unit tests, plus tests that run against your install when it is present |
| `docs/` | Format notes and clean-room behavior notes, written in our own words |
| `tools/` | The commit guard, low-priority runner, and off-screen capture and headless check scripts |

## Building

You need the .NET 8 SDK. Godot 4.7.2 (.NET build) is needed to run the game project.

```sh
dotnet build OpenRanch.sln
dotnet test OpenRanch.sln
dotnet run --project src/OpenRanch.Importer -- inventory
dotnet run --project src/OpenRanch.Importer -- saves --verify
```

Run the game from the Godot editor (open `game/project.godot`), or from the command line:

```sh
godot --path game -- --scene ranch
```

Scripted runs never take focus: `tools/godot-capture.ps1` renders a screenshot in an off-screen
window, and `tools/godot-headless.ps1` runs checks such as `--collision-check` with no window.

The game is found through the `OPENRANCH_GAME_DIR` environment variable or your Steam libraries.
On a shared PC, `tools/run-low.ps1` runs any of these at below-normal CPU priority, for example
`powershell -File tools/run-low.ps1 dotnet test OpenRanch.sln /m:2`.

## Keeping game data out

The repository holds only new code and notes. Please enable the commit guard once after cloning:

```sh
git config core.hooksPath .githooks
```

`tools/check_no_game_files.py` blocks Unity asset files, sound banks, saves, the game's DLLs and
anything that looks like decompiled game code. CI runs the same check on every push.

The rewrite is clean-room: the original's behavior is studied on a developer's own PC and written
down in plain words under `docs/`, and new code is written from those notes. Decompiled code is
never copied into this repository.

## License

GPL-3.0. See [LICENSE](LICENSE).

## Third-party code

`src/OpenRanch.Formats/Unity/Crunch.cs` is an altered version of the crunch texture decoder
(Copyright (c) 2010-2016 Richard Geldreich, Jr. and Binomial LLC), translated to C#. It keeps the
original ZLIB license notice at the top of the file.

Sound banks are read with [Fmod5Sharp](https://github.com/SamboyCoding/Fmod5Sharp) (MIT license),
a NuGet dependency of `OpenRanch.Formats`.
