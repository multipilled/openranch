# Slime species traits

What makes some slime species move or act differently from a pink slime. A slime has a trait when
its prefab carries the trait's component; a largo's prefab carries both parents' components, so it
has both parents' traits. Each trait is one more activity the slime can choose when it reconsiders
(`slimes.md`, "Moving and finding food"); while a trait runs, the slime doesn't reconsider until the
trait is over.

Code: `src/OpenRanch.Simulation/SlimeTraits.cs`; in the world `game/scripts/Slimes/SlimeActor.cs`.

## Which species have which components (Slime Rancher 1.4.4)

Compared with a pink slime's prefab:

| Species | Extra components |
| --- | --- |
| Phosphor | `SlimeHover`, `DestroyOutsideHoursOfDay` |
| Rock | `RockSlimeRoll`, `DamagePlayerOnTouch` |
| Crystal | `CrystalSlimeLaunch`, `DamagePlayerOnTouch` |
| Tangle | `GroundVine`, `PollenCloudController` |
| Quantum | `QuantumSlimeSuperposition`, `GenerateQuantumQubit`, `QuantumVibration` |
| Boom | `BoomSlimeExplode`, `BoomMaterialAnimator` |
| Rad | `RadSlimeExpand` |
| Dervish | `DervishSlimeSpin` |
| Hunter | `SlimeStealth`, `StalkConsumable`, `FeralizeOnLargoTransformed` |
| Saber, Tabby | `StalkConsumable`, `GatherIdentifiableItems` |
| Mosaic | `GlintController`, `DisableAttractorInDarkness` |
| Gold | `GoldSlimeFlee`, `GoldSlimeProducePlorts` |

Read from the install's prefabs.

## Hovering (phosphor)

- Hovering matters 0.3 whenever it is due (more than wandering at 0.2, less than food for a slime
  hungrier than about 0.56).
- It is due again a random time after the last hover ends: between 10 s (fully agitated) and 25 s
  (calm), from 1 − agitation plus up to 0.1 either way. The first hover is due the same way after the
  slime appears.
- A hover lasts 6 s. On starting it picks a random flat drift direction (each of x and z between −1
  and 1). Every physics step: if there is ground within 5 m straight below, it pushes up with
  1200 × mass × (1 − height ÷ 5) times the physics step; and it pushes along the drift direction with
  100 × mass times the step. So it floats up to a few metres and drifts.
- Touching something solid that isn't an item, above a quarter of its scale over its centre (bumping
  its top), ends the hover at once.
- It is not tied to the time of day; the phosphor's night-only life is a different component (below).

## Rolling (rock)

- Rolling matters 0.3 when it is due and the slime stands on something. It is due between 3 s
  (agitated) and 15 s (calm) after the last roll, picked like the hover's delay.
- On starting, it takes its flat right-hand direction as the roll axis and forward as that axis turned
  a quarter left. It spins in place for 1 s (the rock-mode animation), then for 2 s, while it stands on
  something, it twists about the axis with 1200 × mass and pushes forward with 720 × mass, both times
  the physics step.

The numbers are in the game's code, not its data (named constants in `SlimeTraits.cs`). Studied by
static analysis of SlimeHover and RockSlimeRoll. Being calmed by water pauses both timers; water
isn't in openranch yet.

## Night only (phosphor)

Static analysis of DestroyOutsideHoursOfDay and TimeDirector; values from the prefab (InstalledAbilityTests,
OutsideHoursTests); checked by `--slime-zoo --zoo-part phosphor` (PhosphorTrial). openranch:
src/OpenRanch.Simulation/OutsideHours.cs, game/scripts/Slimes/TraitBehaviours.cs (`NightOnly`).

- Data: `startHour`, `endHour` (the window it lives in; for phosphors 18 to 6, wrapping past midnight),
  `minEndureHoursOutsideWindow` and `maxEndureHoursOutsideWindow` (how long it lasts outside it; the
  phosphor prefabs say 0.5 to 0.55 game hours; the code's default of 1.5 is never used). Only phosphor
  slimes and phosphor largos carry it.
- A clock starts when the slime appears: it picks an endurance in that range. If both now and now plus
  the endurance are outside the window, it vanishes after the endurance; otherwise (in the window, or
  the endurance reaches into it) it vanishes the endurance after the window next closes (the next 6:00
  strictly after now). A clock not yet started counts from the new-game 9:00.
- Entering a cave trigger (anything with `CaveTrigger`) stops the clock; leaving the last one starts a
  fresh clock from then. `cavesPreventShutdown` is set but the code never reads it.
- The corral's solar shield (roof) exception comes from the shield objects: openranch asks the world's
  owner for "in a cave" (`ItemCatalog.InCave`); whether the shield carries its own cave trigger isn't
  checked yet (UNVERIFIED.md).

## Not done yet

- Cave triggers in the world feeding `ItemCatalog.InCave` (zone work); the vanishing effect.
- Tabby and saber food carrying (`GatherIdentifiableItems`: pick up fruit or veggies in the mouth and
  carry them toward other food), tabby, saber and hunter stalking and pouncing.
- Boom explosions, rad aura, crystal spikes, quantum ghosts, dervish whirlwinds, tangle vines and
  pollen, mosaic glint, hunter cloaking, gold slime fleeing and its plorts, lucky slime coins, fire
  slimes eating ash, puddle slimes eating water, glitch and quicksilver slimes.
