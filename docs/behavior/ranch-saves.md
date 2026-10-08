# How a ranch is stored, and bringing one over from the original

What the original keeps about a ranch game in its save, how openranch holds the same game, and
what the importer carries over. The byte layout of the original's saves is in
[save-files.md](../formats/save-files.md); openranch's own format is in
[openranch-saves.md](../formats/openranch-saves.md).

Code: `src/OpenRanch.Ranch/RanchState.cs` (the model), `SaveImport.cs` (the importer),
`RanchCensus.cs` (counts), `GameEnums.cs` (names).

## One game, one state

The original saves a game as one block made of a summary, the world, the player, the ranch, the
loose things in the world (actors) and a few smaller parts (Slimepedia, statistics, holiday gordos,
slime appearances, instruments). openranch keeps the same game as one `RanchState`, grouped the same
way, so that nothing has to be looked up across parts.

Every value from one of the game's enums (item types, plot types, upgrades, progress counters, mail
types and so on) stays as the game's own number. The names, such as `PINK_SLIME` or `CORRAL`, are
read from the player's install when they are needed (`GameEnums`), never kept in a table in
openranch. The Slimepedia is the exception the original makes itself: it stores its entries by name.

## The parts of a ranch

- **Money.** The player's newbucks are stored twice: in the summary (which the save menu reads) and
  in the player block. They agree in every save on the development PC; openranch takes the player
  block's.
- **The clock.** One number, the world time (see [day-cycle.md](day-cycle.md)). The summary keeps a
  copy for the save menu.
- **The player.** Health, energy, radiation, keys, money earned over the game, where the player
  stands and faces, the personal upgrades bought, offered and held back, the vacpack's contents for
  each vacpack mode, mail, progress counters, blueprints, gadgets not yet placed, crafting
  materials, which world the player is in (the Far, Far Range or the Slimulations), which zone maps
  are unlocked, when the game was finished, and the decorizer.
- **Plots.** One entry per land plot site, in a fixed order, whether built on or not; 41 on every
  version 12 save on the development PC. Each has the site's id (shared with the world scene), what
  is built there, the upgrades bought and the state of what is built. See [plots.md](plots.md).
- **Access doors.** The doors to the ranch's expansions, by id, each with its state (locked, open
  and so on).
- **Palettes.** The colour palette chosen for each paintable part of the ranch.
- **Area catch-up times.** For each ranch area the player has left, when it was last simulated.
- **Actors.** Every slime, animal, piece of food, plort, crafting material and so on lying in the
  world: a unique id, its type, position and rotation, moods (for slimes: hunger, agitation, fear),
  when it next changes (a chick grows up, a hen lays, a plort or food disappears), how ripe produce
  is, whether it is feral or a Slimulations glitch, fashions worn, and which world it is in.
  Gordos are not actors; they are part of the world.
- **Slimepedia.** The entries unlocked, tutorials completed, unlock popups still to show, and how
  many unlocks have already counted toward progress.
- **Mail.** Each letter's type, its message key and whether it has been read.
- **Gadgets.** Gadgets placed on gadget sites, by site id, with what they hold, extractor timers,
  bait, a snared gordo, fashions and drones (position, cargo, battery and programs); gadgets owned
  but not placed are counted per gadget in the player part.
- **Progress counters.** Named counters such as ranch expansions bought or rewards received from the
  other ranchers; anything not stored counts as 0.
- **The world.** The plort market's seed and how saturated it is with each plort, how much each
  gordo has eaten, treasure pods, switches, puzzle slots, teleporters, phase sites, oases, ginger
  patches and echo note gordos.

## What the importer carries over

`SaveImport.ReadFile` copies the save file into memory, reads it with the version 12 reader and
fills a `RanchState`. It never opens the original's file for writing, and openranch never writes
into the original's save folder.

Carried over: everything listed above, plus slime appearances and instruments.

Not carried over yet (stays at a new game's values): Range Exchange offers, weather and the
firestorm, animal and resource spawner timers, water levels of liquid sources and spawners,
quicksilver generators, the Slimulations' glitch state, game statistics, holiday gordos, and the
save summary's own copies (save time, save counter, game-over flag). Saves from before game 1.4
(versions 8 and 9) can't be read yet; loading such a ranch in the original and saving it upgrades
it to version 12.

## Checked

- `tests/OpenRanch.Ranch.Tests/InstalledRanchTests.cs` imports the newest save of the development
  PC's 2022 ranch (`20220508105930_Game2`, picked by save counter) and compares with the raw save:
  money (player and summary), world time and day, all 41 plots by id, type and upgrades, and the
  number of actors of each type. With the install present it also names the slimes and checks the
  slime counts by type. It prints a summary.
- The same test file imports every version 12 save on the PC, checks money, actor, plot, Slimepedia
  and mail counts against the raw save, and round-trips each through openranch's format.
- `SaveImportTests` builds a small save by hand in the original's layout, writes it to bytes, reads
  it back and imports it, checking each field.
