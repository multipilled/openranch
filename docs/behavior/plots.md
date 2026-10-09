# Land plots: types, building and upgrades

The ranch's land plots: what can stand on one, what each type's menu sells, and what a save keeps
about each plot.

Code: `src/OpenRanch.Ranch/PlotCatalog.cs` (menus and prices read from the install),
`PlotRules.cs` (buying), `RanchState.cs` (`Plot`), `PlotLayout.cs` and `PlotUpgrades.cs` (plots and
their upgrades in the world), `PlotCrops.cs`, `ZoneActors.cs` and `PlotContents.cs` (what a loaded
plot holds and the loose things around it); `game/scripts/SaveLoad` puts them into The Ranch.

## Plot sites

- The ranch has a fixed set of **plot sites**, each with an id shared between the world scene and the
  save (for example `plot1234567890`). The main ranch and each expansion (the Lab, the Overgrowth,
  the Grotto, the Docks) have their own. Every version 12 save on the development PC (15 saves of
  three different games) lists the same **41** sites, built on or not, in the same order.
- Something always stands on a site: an empty plot or one of the six buildings. Building or
  demolishing replaces what stands there; the site stays.

Checked: read from the development PC's saves (`InstalledRanchTests`).

## Plot types

The plot type is the game's enum `LandPlot.Id`, read from the install. It has eight values:

- **None**: the "nothing" value of the enum. A save never stores it for a site.
- **Empty**: bare ground, which sells the six buildings.
- **Corral**: holds slimes.
- **Coop**: holds chickens.
- **Garden**: grows one kind of fruit or veggie.
- **Silo**: stores items.
- **Pond**: a water source for puddle slimes and water.
- **Incinerator**: burns what is thrown in.

Checked: the values read from the install's assembly metadata (`GameEnums`, printed by
`Every_stored_enum_is_in_the_install`); what each building is for is from playing the original.

## Menus and prices

Each plot type's prefab carries a `LandPlot` script naming its type, and an activator that opens a
menu. The menu prefab holds a menu script (`EmptyPlotUI`, `CorralUI`, `CoopUI`, `GardenUI`,
`SiloUI`, `PondUI`, `IncineratorUI`, all kinds of `LandPlotUI`). Its items each have a `cost`, plus:

- a `plotPrefab` for items that replace the plot: on the empty plot's menu, one per building
  (`corral`, `garden`, `coop`, `silo`, `incinerator`, `pond`); on every building's menu,
  `demolish`, which turns it back into an empty plot;
- an `upgrade` (a `LandPlot.Upgrade` value) for upgrades;
- neither, for other actions (the garden's `clearCrop`).

openranch reads all of these from the install (`PlotCatalog.Read`); no price is kept in openranch.

What the menus sell:

| Plot | Upgrades |
| --- | --- |
| Empty | none (builds the six buildings) |
| Corral | high walls, music box, air net, solar shield, plort collector, auto-feeder |
| Coop | high walls, feeder, vitamizer, deluxe coop |
| Garden | rich soil, sprinkler, scareslime, miracle mix, deluxe garden; also clearing the crop |
| Silo | three storage upgrades |
| Pond | none |
| Incinerator | ash trough |

Checked: read from the install by `Plot_menus_are_read_from_the_install`, which prints every menu
with its items and prices.

## Rules that are not in the data

These are code-only facts, from static analysis of the plot menu scripts (`SiloUI`, `GardenUI`,
`CoopUI`, `CorralUI`) and the plot model, and are named in `PlotRules.cs` with that source:

- An upgrade can be bought once per plot.
- **Silo storage is bought in order**: the second storage upgrade needs the first, the third needs
  the second. openranch applies this to any upgrades whose names differ only by a final number.
- **Rancher rewards unlock three upgrades.** Miracle mix goes on sale once the player has one level
  of Ogden's rewards (progress counter `OGDEN_REWARDS` at 1), the deluxe garden at two, and the
  deluxe coop at two levels of Mochi's rewards (`MOCHI_REWARDS` at 2).
- Buying needs the full price in hand; the price is taken when the purchase happens.
- Building replaces the empty plot with a fresh building: no upgrades, nothing stored or planted, the
  feeder at its normal speed. Demolishing does the same in reverse, and whatever the plot held (silo
  contents, a crop, upgrades) is lost. The silo's menu warns about this when the silo is not empty.
- A corral can't be demolished until the player has finished the shooting tutorial, unless the game
  mode assumes an experienced player. (Not modelled yet.)

## What a save keeps per plot

Each plot in the ranch block (`SRLP`) stores:

- the site id and the plot type;
- the upgrades bought, as a list of `LandPlot.Upgrade` values in the order bought;
- the **auto-feeder**: when it next drops food (world time), how many drops are still queued, and its
  speed (`SlimeFeeder.FeedSpeed`: normal, slow or fast; normal is the enum's first value, 0, which
  is also what a fresh plot holds);
- when the **plort collector** next empties the corral (world time);
- for a garden, the **crop** planted (`SpawnResource.Id`) and when it dies (world time);
- for a silo, its **contents** per storage kind (`SiloStorage.StorageType`: non-slimes, plorts,
  food, crafting materials or elder items; the plot's silo uses one kind, other stores share the
  same scheme), each a list of slots with item and count, and which slot each of the silo's buttons
  has selected;
- for an incinerator, the **ash** collected in its trough.

Fields that don't apply to a plot's type keep their zero values. openranch keeps all of these in
`Plot` and its own save format.

Checked: layout from the save reader (`docs/formats/save-files.md`, byte-exact on 15 saves);
values compared plot by plot against the raw save for the development PC's Game2 ranch.

## Upgrades in the world

Every plot prefab is built with all its upgrade pieces in it, switched off (and the plain pieces they
replace switched on). The prefab's root carries one **upgrader script** per kind of upgrade, whose
fields point at those pieces; buying an upgrade, or loading a plot that has it, makes the upgraders
switch them. The pieces each field points at are read from the install; what each upgrade does to
which field is only in the scripts' code:

| Upgrader | Upgrade | Switches |
| --- | --- | --- |
| Walls (corral, coop) | high walls | the standard walls off, the high walls on |
| Music box (corral) | music box | the music box on |
| Air net (corral) | air net | both air nets (on the standard and on the high walls) on |
| Solar shield (corral) | solar shield | both shields on |
| Plort collector (corral) | plort collector | the collector on |
| Feeder (corral, coop) | auto-feeder; the coop's feeder | the feeder on (the coop's is its spring grass) |
| Vitamizer (coop) | vitamizer | the vitamizer on |
| Deluxe coop (coop) | deluxe coop | the deluxe pieces on; also makes the coop's regions deluxe |
| Mineral soil (garden) | rich soil | the mineral soil on |
| Sprinkler (garden) | sprinkler | the sprinkler on |
| Scareslime (garden) | scareslime | the scareslime on |
| Miracle mix (garden) | miracle mix | the miracle mix on, the plain soil off |
| Deluxe garden (garden) | deluxe garden | the deluxe pieces on; bought later, it also replants the crop as its deluxe kind |
| Storage (silo) | storage 2, 3, 4 | each its own addition on; the top piece swapped for the one for 2, or for 3 and 4 |
| Ash trough (incinerator) | ash trough | the trough on |

- On load, every upgrader of the root runs, in component order, over the saved upgrades in saved
  order, so the last switch of a piece wins.
- An upgrade that no upgrader of the plot knows does nothing. Saves can hold such upgrades: the
  development PC's Game2 ranch lists a sprinkler and the numbers 18 to 23 (not in the install's enum)
  on its corrals; those change nothing.
- Loading applies the upgrades before the crop is planted, so the deluxe garden's replanting doesn't
  happen on load; a deluxe garden's saved crop is already its deluxe kind.

Checked: the upgraders and their pieces are read from the install
(`The_installs_plot_prefabs_carry_an_upgrader_for_every_upgrade`: every upgrade has an upgrader on
some plot); the table is from static analysis of the 15 upgrader scripts and `LandPlot`. The headless
`--m3-check` compares, on every plot of the zone, which upgrade pieces are drawn with what an
independent read of the save asks for (Game2 and Logansfarm saves).

## Crops

- A plot whose save names a crop (`SpawnResource.Id`, a veggie patch or fruit tree) gets the crop's
  prefab, looked up by id in the world's resource spawner list, placed at the plot root's position
  and rotation with the prefab's own scale. The game does this for any plot type, not only gardens:
  the Game2 ranch has carrot patches and pogo trees standing in corrals.
- A crop has **spawn joints**, the points its produce hangs from while it grows. The scene places a
  few crops of its own too (the Overgrowth's patches and trees).
- Produce in the save that is **unripe or ripe** goes back onto a crop: the nearest crop closer than
  10 m, at that crop's nearest joint closer than 0.1 m. With none in reach it falls loose and counts
  as edible. Unripe produce is drawn at a third of its size and can't be vacuumed; ripe produce lets
  go of its joint once the vacpack has pulled at it for its prefab's release time, then falls.
  Edible and rotten produce lies loose.

Checked: from static analysis of `LandPlot.SetModel`, `ResourceCycle` and `SpawnResource`; the
crop prefabs and joints are read from the install. On the Game2 ranch 135 pieces of produce hang
from 14 crops (94 unripe), checked headless with `--m3-check`.

## Loose things on a loaded ranch

- The save lists every actor of the game with its position and its **region set** (the Far, Far
  Range, the desert, the valley, the lab, the Slimulations). An actor is on the ranch when it is in
  the ranch's region set ("HOME") and inside the bounding box of one of the ranch's cells; the box is
  stored on each cell's `Region` script in the world scene.
- On The Ranch everything but slimes comes back where it was saved and turned the way it was:
  food, plorts, chickens (in the coops or not), toys, ornaments. Slimes come back with the corrals
  (docs/behavior/corrals.md).

Checked: `--m3-check` compares the actors spawned, by type, with an independent read of the save
(416 on the Game2 ranch).

## Not modelled yet

- Produce ripening, rotting and new produce growing; the rotten look.

- What each upgrade does beyond its pieces showing up (walls stop jumping slimes, the music box
  calms, the feeder and plort collector run on timers, and so on). A loaded plot keeps its feeder,
  collector, store and ash values (`PlotContents`), but nothing uses them yet.
- The feeder's timing: each cycle queues a batch of drops; the batch size is on the feeder script
  (`itemsPerFeeding`), the hours per cycle depend on the speed setting.
- Gardens growing, ponds, incinerating and ash.
