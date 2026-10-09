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
  or closed; `AccessDoor.State`).
- **Palettes.** The colour palette chosen for each paintable part of the ranch.
- **Area catch-up times.** For each ranch area the player has left, when it was last simulated.
- **Actors.** Every slime, animal, piece of food, plort, crafting material and so on lying in the
  world: a unique id, its type, position and rotation, moods (for slimes: hunger, agitation, fear),
  when it next changes (a chick grows up, a hen lays, a plort or food disappears), how ripe produce
  is, whether it is feral or a Slimulations glitch, fashions worn, and which world it is in.
  Gordos are not actors; they are part of the world.
- **Slimepedia.** The entries unlocked, tutorials completed, unlock popups still to show, and how
  many unlocks have already counted toward progress.
- **Mail.** Each letter's type (personal, upgrade or exchange; `MailDirector.Type`), its message key
  and whether it has been read.
- **Gadgets.** Gadgets placed on gadget sites, by site id, with what they hold, extractor timers,
  bait, a snared gordo, fashions and drones (position, cargo, battery and programs); gadgets owned
  but not placed are counted per gadget in the player part.
- **Progress counters.** Named counters such as ranch expansions bought or rewards received from the
  other ranchers; anything not stored counts as 0.
- **The world.** The plort market's seed and how saturated it is with each plort, how much each
  gordo has eaten, treasure pods, switches, puzzle slots, teleporters, phase sites, oases, ginger
  patches and echo note gordos; and each crop's next batch time and stored water, by the crop's
  position (`resourceSpawnerWater`; see [produce.md](produce.md)).

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

## Saving the live ranch

openranch saves a ranch it is running in its own format only (`.ranch.json`,
[openranch-saves.md](../formats/openranch-saves.md)), never as the original's `.sav` and never into
the original's save folder. `RanchWriter.Merge` (src/OpenRanch.Ranch/RanchWriter.cs) starts from the
ranch as it was loaded and lays over it what the running world changed; the game's side is
`game/scripts/SaveLoad/SaveWriter.cs`.

- **From the world:** money on hand (money earned in play is also added to the money earned over
  the game), the world time (the world clock moves the loaded ranch's own time), every actor the
  world holds (position, rotation, type, and for slimes hunger and agitation), the vacpack's slots
  of normal play (vacpack.md, "Saved"), every plort's market saturation (plort-market.md, "Saved")
  and, when the player was placed from the save, where the player stands and looks.
- **Actors.** A saved actor the world took in is written where it is now, in its saved place in the
  list; one that left the game (eaten, sold, sucked up) is dropped. The loader tells the writer the
  save's id of every actor it put into the world (corral slimes and the zone's loose actors), so each
  is written once. Saved actors the world never took in (outside the zone, or with no prefab yet) are
  written as saved. An actor that appeared in play
  gets the next free id, the way the original numbers them: dynamic ids start at 100 and loading an
  actor moves the next id past it (static analysis of the original's actor registry). Its region set
  is the player's, and the timers the world doesn't model stay 0, the value the original's actor
  record starts with before each kind fills its own members.
- **Moods.** Only hunger and agitation are modelled; fear and anything else keep the saved value.
  The loaded slimes start with their saved hunger and agitation, so a load and save without play
  keeps them.
- **Rotation.** Written as the original stores it: Euler angles in degrees from 0 to 360, applied
  about z, then x, then y, in its left-handed axes (Godot's rotation is mirrored in z first).
- **Plots, progress and doors** are written as the ranch holds them: the world builds, upgrades,
  plants and buys expansions on the loaded ranch itself, and fills the plots' stores, feeders,
  collectors and ash troughs there.
- **Clocks.** Produce is written with its stage and when the stage ends, hens with their next laying
  time and chicks with their growing-up time (`LiveActor.Timers`); rotten produce is left out, as the
  original drops it from its records when it rots. Every crop's clock is written
  (`LiveRanch.ResourceSpawners`); crops not in play keep the saved clock.
- **The player** is written where their feet are and with the view as the original stores it
  (pitch and yaw in Euler degrees, the saved roll kept), only when the loader stood them there: a
  player who saved in another zone than the one running keeps the saved place and view.
- **Everything else passes through** unchanged: upgrades, mail, Slimepedia, gadgets, the Nimble
  Valley's vacpack, the market's seed and the rest of the world.

`--save FILE --save-out OUT` writes OUT once the world has settled (4 s, as `--m3-check`) and quits.
In play, F5 writes to openranch's user folder (`user://saves/<game name>.ranch.json`); the original
has no save key (openranch's choice, UNVERIFIED.md).

Until the loader hands over the save's ids, SaveWriter matches each actor the loader puts into the
world to the saved actor of the same type standing exactly there (within 1 cm, during the first
second).

## Opening a saved ranch

- The player stands where the save left them and looks the same way, when they saved in the zone
  being opened (in its region set and inside one of its cells); otherwise they start at the zone's
  spawn point. The save holds the feet and the view as pitch (down is positive) and yaw
  (player-camera.md).
- The vacpack holds the saved slots with the capacity of the player's upgrades, the market its saved
  saturation, and the clock the saved world time (day-cycle.md).

## Checked

- Enum names quoted here (moods, door states, mail types) were read from the install's assembly
  metadata by `Every_stored_enum_is_in_the_install`.
- `tests/OpenRanch.Ranch.Tests/InstalledRanchTests.cs` imports the newest save of the development
  PC's 2022 ranch (`20220508105930_Game2`, picked by save counter) and compares with the raw save:
  money (player and summary), world time and day, all 41 plots by id, type and upgrades, and the
  number of actors of each type. With the install present it also names the slimes and checks the
  slime counts by type. It prints a summary.
- The same test file imports every version 12 save on the PC, checks money, actor, plot, Slimepedia
  and mail counts against the raw save, and round-trips each through openranch's format.
- `RanchWriterTests`: a world that changed nothing writes the ranch as loaded, byte for byte; actors
  are moved, dropped, kept and added with the original's next ids; the written file reads back
  through `RanchFiles.Read` as the merged ranch; Euler angles match the original's convention.
- In Godot on `20220508105930_Game2_4` and `20181115184455_Logansfarm_5`: `--m3-check` (which
  also compares the vacpack's slots, the market's saturation, the player's place and view and the
  two clocks with the save), then `--save-out` after 4 s, then `--m3-check` on the written file:
  all pass. A second `--save-out` from the written file differs from the first only in
  the world time and the 17 corral slimes' places and moods; the other 2640 actors and every other
  member are the same.
- `SaveImportTests` builds a small save by hand in the original's layout, writes it to bytes, reads
  it back and imports it, checking each field.
