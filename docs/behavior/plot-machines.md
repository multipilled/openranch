# Plot machines: silos, feeders, plort collectors, the incinerator

What the plots' machines do once bought. Each machine is a script in the plot prefab, on an object
an upgrade switches on ([plots.md](plots.md), "Upgrades in the world"); it runs while its object is on.

Code: `src/OpenRanch.Ranch/PlotMachines.cs` (the rules and `PlotMachineData`, read from the plot
prefabs), `game/scripts/RanchEconomy/RanchMachines.cs` (in play).

## Stores

A store (`SiloStorage`) has a kind (non-slimes, plorts, food, crafting, elder), `numSlots` slots and
holds `maxAmmo` of one item per slot (silo: printed by `Install_plot_machines`). The kinds take:
non-slimes anything but slimes; plorts the plorts; food fruit, veggies, meat and chicks; crafting
plorts and crafting materials; elder the elder hen and rooster. Quicksilver plorts go in none.

An item goes into the first slot already holding it; if that slot is full it is refused, even with
an empty slot free; otherwise into the first empty slot. A plot keeps its stores' slots by kind
(`Plot.Silo`), which the save keeps.

## Silo catchers

A silo catcher (`SiloCatcher`, a trigger box) takes a vacuumable item thrown in, when nothing holds
it, into one slot of the store above it, and the item is used up. The corral's feeder hopper and its
plort collector's outlets are catchers too.

The silo's store has 12 slots of 300. Each of its 4 catchers has a button (`SiloStorageActivator`)
that cycles it through 3 slots (0/4/8, 1/5/9, 2/6/10, 3/7/11); the plot keeps each button's choice
(`Plot.SiloSlotSelections`), 0 when new. The storage upgrades switch more catchers on (one to start).

A vacpack pulling at a catcher's front (within 45 degrees) gets one item of the catcher's slot every
quarter second, put 1.2 m out towards it (the original speeds this up while held; not modelled).

## Auto-feeder (corral)

Every cycle the feeder (`SlimeFeeder`) queues `itemsPerFeeding` drops (6). A cycle lasts 6 game hours
at normal speed, 9 slow, 3 fast (the game's code: `hoursByFeedSpeed` is a dictionary, which Unity
doesn't save). A feeder switched on feeds one cycle later. Queued drops come out one every half
second from the first slot of its food store, pushed out of the feeder's front; with the store
empty the queue runs down one drop a frame. The plot keeps the next feeding time, the queued drops and the speed.

## Plort collector (corral)

Every `collectPeriod` game hours (1) the collector (`PlortCollector`) sweeps the plorts lying in its
area (`collectionArea`) into its plort store; a collector switched on sweeps one period later. The
plot keeps the next sweep time. openranch takes the plorts in at once; the original reels them to
the collector over a few seconds.

## Incinerator and ash trough

The incinerator (`Incinerate`) burns anything that falls in except fire plorts, fire slimes and the
charcoal brick. With the ash trough (`FillableAshSource`) on, each food burnt adds
`ashPerIncineration` ash, up to `maxUnits` (20). A new incinerator's trough starts full. Fire slimes
eat one unit at a time from it (docs/behavior/slimes.md). The plot keeps the ash.

Checked: static analysis of `SiloStorage`, `Ammo.MaybeAddToSlot`, `SiloCatcher`, `SlimeFeeder`,
`PlortCollector`, `Incinerate` and `FillableAshSource`; values read from the plot prefabs
(`Install_plot_machines`). The headless `--m6-check` buys the feeder and collector on a corral, fills
the hopper, waits for the drops and a sweep, fills a silo and burns a carrot.

## Not modelled yet

- Pressing the silo's slot buttons (the saved choices are used); the speed-up of giving items out.
- The feeder speed button; the coop's feeder (its `FeederRegion` speeds chicks up).
- Drones and other gadgets using stores.
