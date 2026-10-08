# Slime Rancher save files

Notes on the save format, written for interoperability so players can bring their existing ranches
into openranch. The reader is `src/OpenRanch.Formats/Saves`; the exact layouts live in
`SaveSchemas.cs`.

## Where saves live

On Windows the game writes saves to `%USERPROFILE%\AppData\LocalLow\Monomi Park\Slime Rancher`.
Each ranch keeps a rolling set of numbered files named `<created>_<ranch>_<n>.sav`, for example
`20220508105930_Game2_3.sav`. The same folder holds `slimerancher.prf`, the player profile and
options, which uses the same block scheme (tag `SRPF`, version 7) and isn't read yet.

## Building blocks

All numbers are little-endian. Strings are .NET `BinaryWriter` strings: a 7-bit variable-length
byte count followed by UTF-8.

Every structure is a **block**:

| Part | Encoding |
| --- | --- |
| Tag | string, such as `SRGAME` or `SRW` |
| Version | uint32 |
| Begin marker | int16 `0x3000` |
| Fields | depends on tag and version |
| End marker | int16 `0x4000` |

Tags are not unique on their own (`SRAD` is both a vacpack slot, version 2, and a world actor,
version 9), so a reader always knows from context which block comes next.

Collections inside a block:

| Kind | Encoding |
| --- | --- |
| List | int32 count, then the items |
| Map | int32 count, then for each entry the key, the value and an int16 `0x2000` element marker |
| Pair list | int32 count, then key and value with no marker (only slime emotion levels use this) |
| Optional value | a bool, then the value if it is true |
| Section marker | int16 `0x1000`, used twice in the game block around the actor list |
| Timestamp | int64 .NET ticks, then the UTC offset in minutes as a double |

Enums are stored as int32. Positions are their own small block (`SRV3` version 2: three floats),
and they are also used as map keys for spawners that are identified by where they stand.

## The game block (`SRGAME` version 12)

Written by game versions 1.4.3 and 1.4.4. In order:

1. Ranch file name and display name (strings)
2. Summary (`SRGSUMM` v4): game version, mode, icon, money, Slimepedia count, world time, save
   time, game-over flag, save counter. The save menu reads only this part.
3. World (`SRW` v22): world clock, market seed and saturation, Range Exchange offers, teleporters,
   gordos, placed gadgets (with drones), treasure pods, switches, puzzle slots, weather, the
   firestorm, oases, ginger patches, quicksilver generators, echo note gordos and Slimulations
   state.
4. Player (`SRPL` v14): health, energy, radiation, money, keys, position, upgrades, vacpack
   contents per mode, mail, progress counters, blueprints, gadgets, crafting materials, unlocked
   maps and the decorizer.
5. Ranch (`SRRANCH` v7): the 41 plot slots with their type, upgrades, feeder and silo contents,
   plus access doors, colour palettes and per-area fast-forward times.
6. Section marker, the list of world actors (`SRAD` v9: slimes, food, plorts and so on, each with
   position, type, emotions and timers), section marker.
7. Slimepedia (`SRPED` v3), game statistics (`SRGA` v3), holiday gordos (`SRHD` v2), unlocked slime
   appearances (`SRAPP` v1) and instruments (`SRINSTR` v1).

World time counts game seconds; one in-game day is 86,400 of them.

## Checking the reader

`openranch-import saves --verify` reads every supported save into memory, writes it back out with
the same layouts and compares the bytes. A single misread field shifts everything after it, so a
byte-for-byte match is strong evidence that each field is understood. Original saves are only ever
read, never opened for writing.

## Older saves

Saves from 2018 builds use game block versions 8 and 9 (game 1.3.0 to 1.3.1). Their layouts differ
in many blocks and are not supported yet. Loading such a ranch once in the original game and
saving it upgrades it to version 12.
