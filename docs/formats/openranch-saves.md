# openranch ranch saves

openranch keeps each ranch game in its own save file, separate from the original's saves. The
format is a UTF-8 JSON document, written and read by `src/OpenRanch.Ranch/RanchSave.cs` from the
in-memory model in `RanchState.cs`. Ranches from the original game come in through the importer
(`SaveImport.cs`, see [ranch-saves.md](../behavior/ranch-saves.md)) and are then saved in this
format; openranch never writes the original's `.sav` format and never writes into the original's
save folder (`RanchSave.WriteFile` refuses any path inside it).

File names end in `.ranch.json`. Where the files live is up to the game (its user data folder).

## Top level

```json
{
  "format": "openranch-ranch",
  "version": 1,
  "ranch": { ... }
}
```

| Member | Meaning |
| --- | --- |
| `format` | Always `"openranch-ranch"`. Anything else is not a ranch save. |
| `version` | Whole number, the format version. This document describes version 1. |
| `ranch` | The ranch itself (below). |

`format` and `version` are always written first, so a reader can check them before reading the rest.

## Values

- Member names are camelCase forms of the model's property names (`worldTime`, `player`, `plots`).
- **Game enums are numbers.** Item types, plot types, upgrades, progress counters, mail types and
  so on keep the original game's own enum numbers, exactly as its saves hold them. Names are not
  stored; openranch looks them up in the player's install when it needs them (`GameEnums`). Which
  enum a number belongs to is noted on each property in `RanchState.cs`.
- Maps keyed by an enum number are JSON objects whose member names are the number as text
  (`"palettes": { "0": 4 }`). Maps keyed by an id from the world (plot sites, doors, gordos) use the
  id.
- Times are world time, in game seconds since midnight before day 1 (see
  [day-cycle.md](../behavior/day-cycle.md)). The original uses infinity for "never"; JSON has no
  infinity, so it is written as the string `"Infinity"` (or `"-Infinity"`, `"NaN"`). This applies to
  every number member.
- Positions and rotations are `{ "x": .., "y": .., "z": .. }` in the original's world units and
  degrees.
- Numbers are written in the shortest form that reads back to the same value, so a save read and
  written again comes out byte for byte the same.
- Absent optional values are `null`.

## The ranch object

| Member | Holds |
| --- | --- |
| `gameName`, `displayName` | The game's file-name part and the name the player gave it |
| `gameVersion` | Version of the game that last wrote it (the original's, for an imported ranch) |
| `gameMode`, `gameIconId` | Game mode and the save-menu icon item |
| `worldTime` | The world clock |
| `player` | Money, keys, health, energy, radiation, position and rotation, personal upgrades (bought, offered, locked), vacpack contents per vacpack mode, mail, progress counters and delayed progress, blueprints, gadgets in the inventory, crafting materials, the current world (region set), unlocked zone maps, end-game time and the decorizer |
| `plots` | Every land plot site in stored order: `id`, `type`, `upgrades`, the garden crop (`attachedResource`, `attachedDeathTime`), the auto-feeder (`feeder`: `nextTime`, `pendingCount`, `speed`), `collectorNextTime`, silo contents per storage kind (`silo`), the silo buttons' selected slots (`siloSlotSelections`) and `ashUnits` |
| `accessDoors` | Expansion doors by id, with their state |
| `palettes` | The chosen colour palette for each paintable part of the ranch |
| `areaCatchUpTimes` | For ranch areas the player has left, when they were last simulated |
| `actors` | Slimes, animals, food, plorts and other loose things: `actorId`, `typeId`, position, rotation, moods (`emotions`), timers, ripeness (`cycleState`, `cycleProgressTime`), `disabledAtTime`, `isFeral`, `fashions`, `isGlitch`, `regionSetId` |
| `pedia` | Slimepedia entries unlocked (entry names, as the original stores them), tutorials completed, popups waiting, progress already given |
| `placedGadgets` | Gadgets standing on gadget sites, by site id, with contents, extractor timers, bait, snared gordo, fashions and drone state |
| `world` | The market's seed and saturation per plort, gordos, treasure pods, switches, puzzle slots, teleporters, phase sites, oases, ginger patches and echo note gordos |
| `appearances` | Secret slime styles unlocked and chosen, per slime |
| `instruments` | Instruments unlocked and chosen |

Vacpack, silo and gadget contents are lists of slots: `{ "id": item, "count": n, "emotions": {..} }`.
An empty slot is the original's "none" item with count 0, kept as stored.

## Reading rules

- A document whose `format` isn't `openranch-ranch`, or with no whole-number `version`, is refused.
- A version newer than the reader knows is refused with a message; it is never guessed at.
- An older version is upgraded step by step to the current one before it is read (none exist yet).
- Members not described here are an error: a version 1 file holds exactly these members.
- Members that are missing take the value a new ranch has, so `{"format": "openranch-ranch",
  "version": 1, "ranch": {}}` is a new game at 9:00 on day 1.

## Changing the format

Every change to what is stored bumps `version` and adds an upgrade step in `RanchSave.Upgrade` that
rewrites the previous version's JSON, plus a test with a document of the old version. Old saves must
keep loading.

## Writing

`RanchSave.WriteFile` writes the whole document to a temporary file beside the target and then moves
it over the target, so a crash while saving never leaves a half-written save.

## Checked

`tests/OpenRanch.Ranch.Tests/RanchSaveTests.cs` writes a ranch with every member filled in
(including infinity, negative zero and absent values), reads it back and writes it again: the bytes
match and the values are the same. `InstalledRanchTests` does the same for every version 12 save on
the development PC after importing it.
