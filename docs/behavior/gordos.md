# Gordos

Checked by static analysis of GordoEat, GordoRewardsBase, GordoRewards and BoomGordoEat, and by
reading every gordo prefab in the install (`GordoData`). openranch: src/OpenRanch.Simulation/Gordo.cs,
src/OpenRanch.Formats/Game/GordoData.cs, game/scripts/Slimes/GordoActor.cs. Checked by `--slime-zoo`
(part "gordos") and InstalledGordoTests / GordoTests.

## What the data says

- The LookupDirector's gordo list holds one prefab per gordo kind (14 in 1.4.4). Each prefab has a
  `GordoIdentifiable` (its item id, e.g. `PINK_GORDO`), a `GordoEat` (or a subclass such as
  `BoomGordoEat`) and a `GordoRewards`.
- `GordoEat.slimeDefinition`: the slime whose diet the gordo eats. `targetCount`: how much it must
  eat. `growthFactor`: how many times its starting size it is when full. `vibrationFactor`: how
  strongly the inner model shakes near the end.
- `GordoRewards.rewardPrefabs`: the rewards, in order. `slimePrefab`: the slime that fills every spawn
  point the rewards leave over. `rewardOverrides`: per game mode, a list that replaces the rewards.
- Gordos placed in the world scene carry their own `GordoRewards` (keys, slime-specific crates), which
  differ from the prefab list's.
- `BoomGordoEat` adds `explodePower`, `explodeRadius`, `minPlayerDamage`, `maxPlayerDamage`.

## Rules (from the code)

- A gordo eats only ids in its slime's diet. On a touch it walks its slime's eat map and takes the
  first row for that food: the meal counts that row's production count (a favourite counts the
  favourite production count, anything else 1). Hunger plays no part; it eats any time.
- It stops eating once the count reaches the target. Reaching it starts the strain: two seconds
  later it bursts. A boom gordo explodes as it bursts (after the strain, before the rewards).
- Size: lerp from its starting scale to starting scale × growthFactor by the share fed. Past 70% fed,
  the inner model's scale wobbles with Perlin noise × vibrationFactor, growing to full at 100%.
- Burst: 13 spawn points, each at the gordo's position + (unit offset × 1.2) + (0, 1.7, 0) in world
  axes. Unit offsets: the centre; six around the middle every 60°; three above (half out, 0.866 up)
  at 30°, 150°, 270°; three below (half out, 0.866 down) at −30°, 90°, 210°. The first reward goes
  to the centre point facing the world's forward; each later thing takes a random free point and faces
  outward along its offset. Rewards go first, then the fill slime until all 12 outer points are used,
  so a gordo always drops 13 things. Each gets a random torque of up to ±10 about each axis.
- During a holiday event, a standard crate may be swapped for the event crate (not modelled yet).
- After bursting the gordo is switched off for good; its save count becomes −1 (burst).

## Not done yet

- The inner model's vibration (drawn), the face's strain animation, sounds and burst effects.
- Gordo snares (gadget gordos) and event/echo-note gordos.
