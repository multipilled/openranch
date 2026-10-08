# Land plots: types, building and upgrades

The ranch's land plots: what can stand on one, what each type's menu sells, and what a save keeps
about each plot.

Code: `src/OpenRanch.Ranch/PlotCatalog.cs` (menus and prices read from the install),
`PlotRules.cs` (buying), `RanchState.cs` (`Plot`).

## Plot sites

- The ranch has a fixed set of **plot sites**, each with an id shared between the world scene and the
  save (for example `plot1234567890`). The main ranch and each expansion (the Lab, the Overgrowth,
  the Grotto, the Docks) have their own. Every version 12 save on the development PC lists **41**,
  whether they are built on or not, and always in the same order.
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

These are code-only facts, studied on a developer's PC from how the original works, and named in
`PlotRules.cs`:

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
  speed (`SlimeFeeder.FeedSpeed`: normal, slow or fast);
- when the **plort collector** next empties the corral (world time);
- for a garden, the **crop** planted (`SpawnResource.Id`) and when it dies (world time);
- for a silo, its **contents** per storage kind (`SiloStorage.StorageType`), each a list of slots
  with item and count, and which slot each of the silo's buttons has selected;
- for an incinerator, the **ash** collected in its trough.

Fields that don't apply to a plot's type keep their zero values. openranch keeps all of these in
`Plot` and its own save format.

Checked: layout from the save reader (`docs/formats/save-files.md`, byte-exact on 15 saves);
values compared plot by plot against the raw save for the development PC's Game2 ranch.

## Not modelled yet

- What each upgrade does (walls stop jumping slimes, the music box calms, the feeder and plort
  collector run on timers, and so on).
- The feeder's timing: each cycle queues a batch of drops; the batch size is on the feeder script
  (`itemsPerFeeding`), the hours per cycle depend on the speed setting.
- Gardens growing, ponds, incinerating and ash.
