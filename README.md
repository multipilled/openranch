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

Next is milestone 1: walking around The Ranch with its real geometry, textures and collision.
The full plan, with milestones M0 to M5, is in the project's rewrite plan document.

## Layout

| Folder | What it holds |
| --- | --- |
| `src/OpenRanch.Formats` | Readers for Unity serialized files and Slime Rancher saves, plain .NET 8, tested without Godot |
| `src/OpenRanch.Importer` | The `openranch-import` command |
| `game/` | The Godot 4.7 project (C#) |
| `tests/` | Unit tests, plus tests that run against your install when it is present |
| `docs/` | Format notes and clean-room behavior notes, written in our own words |
| `tools/` | The commit guard and helper scripts |

## Building

You need the .NET 8 SDK. Godot 4.7.2 (.NET build) is needed to run the game project.

```sh
dotnet build OpenRanch.sln
dotnet test OpenRanch.sln
dotnet run --project src/OpenRanch.Importer -- inventory
dotnet run --project src/OpenRanch.Importer -- saves --verify
```

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
