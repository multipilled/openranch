# Produce: crops growing, ripening and rotting

How fruit and veggies grow on crops and what happens to them on the world clock. Planting a garden,
the crops' prefabs and their spawn joints are in [plots.md](plots.md); the clock is in
[day-cycle.md](day-cycle.md).

Code: `src/OpenRanch.Ranch/Produce.cs` (the rules), `ProduceData.cs` (the values, read from the
install), `game/scripts/RanchEconomy/RanchProduce.cs` (in play), `game/scripts/SaveLoad/CropHold.cs`
(produce held on its joint).

## Stages

Each piece of produce has a `ResourceCycle` script whose fields give the game hours of each stage
(`unripeGameHours`, `ripeGameHours`, `edibleGameHours`, `rottenGameHours`). In the install every
fruit and veggie that grows in a garden is unripe 6 h, ripe 6 h, edible 36 h and rotten 6 h; ginger
and kookadoba differ (printed by `Install_produce_crops_and_hens`).

- **Unripe:** on its joint, a third of its size, can't be vacuumed, not edible.
- **Ripe:** grows to full size over 4 s, can be vacuumed; pulled at by the vacpack for its
  `releasePrepTime` (real seconds) it lets go.
- **Edible:** off its joint, falls with a small push; slimes may eat it.
- **Rotten:** not edible, drawn with the prefab's rotten material. The original drops rotten produce
  from its records at once, so a save never holds it.
- **Gone:** removed when its rotten hours are up.

Every stage after the first lasts its hours times a random 0.9 to 1.1, counted from when the stage
before ended but never from later than now, so produce that was away catches up stage by stage.
Produce saved unripe or ripe that finds no joint on load becomes edible for a fresh varied edible
time. Produce made loose (shot from the vacpack, dropped by a feeder) is edible for its varied edible
hours.

Checked: static analysis of `ResourceCycle` (`Attach`, `ProgressResource`, `AdvanceProgressTime`,
`Vary`, `SetInitState`, `SetRotten`); values read from the install. The headless `--m6-check` follows
a garden's first batch through every stage and checks each stage's length.

## Crops

A crop's `SpawnResource` script says what it grows (`ObjectsToSpawn`, and rarely
`BonusObjectsToSpawn`), how many per batch and how often:

- **When.** A crop keeps the world time of its next batch. A crop seen for the first time grows at
  once; on a new game's very first moment it starts 3 to 9 hours into its interval; a crop with a
  `SpawnResourceForceFirstRipeness` grows its first batch ripe. After a batch the next is due a
  random 18 to 24 game hours later (`MinSpawnIntervalGameHours`, `MaxSpawnIntervalGameHours`).
- **Water.** A watered crop (a sprinkler, or water stored from rain or a water tank) counts down half
  again as fast, and so does its unripe and ripe produce. Stored water drains 3/23 per hour (a full
  store of 3 lasts 23 hours).
- **Catching up.** A crop on a land plot that is a quarter of an hour or more overdue grows every
  batch it missed, one interval apart (halved when watered, at least an hour), leaving out batches
  old enough to have rotted away (twice the time off the crop with miracle mix). Other crops grow one
  batch, dated to the last interval boundary before now.
- **How many.** A whole number drawn between `MinObjectsSpawned` (or `MinNutrientObjectsSpawned` on
  rich soil) and `MaxObjectsSpawned`; none while `MaxActiveSpawns` of its produce still hang. Each
  is a bonus item for the first `minBonusSelections` or by `BonusChance`. The original picks the
  item with an upper bound one short of the list, so with two or more choices the last is never
  picked. Produce that would already have rotted by now isn't grown.
- **Where.** Each piece hangs on a free joint picked at random; with `forceDestroyLeftoversOnSpawn`
  what still hangs there is removed first.
- **Timing of a batch.** A batch dated to a time is unripe until that time plus its unripe hours
  (not varied); then the stages run as above.

The save keeps each crop's next batch time and stored water by the crop's position
(`resourceSpawnerWater`, matched within 0.1 m); openranch keeps them in `WorldState.ResourceSpawners`.

Checked: static analysis of `SpawnResource` (`SetModel`, `UpdateToTime`, `GetSpawnMetadatas`,
`Spawn`) and `SavedGame.SetSpawnTimes`; values read from the install
(`Install_produce_crops_and_hens` prints every crop: 10 to 30 per batch, every 18 to 24 h).

## Planting

A garden's planting hole (`GardenCatcher`) takes produce thrown into it when the garden has no crop,
and plants the crop its `plantable` list names for that produce (the deluxe crop in a deluxe garden).
The produce is used up. A crop planted in play grows its first batch at once.

## Not modelled yet

- Rain filling crops' water (no weather yet); the water tank.
- Miracle mix's preservative (edible produce near it keeps longer): only its catch-up rule is in.
- The rotten look is a stand-in tint, not the prefab's rotten material.
