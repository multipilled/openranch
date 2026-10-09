# Feeding habits

Checked by static analysis of StalkConsumable, GatherIdentifiableItems, SlimeFlee, GoldSlimeFlee,
GoldSlimeProducePlorts, LuckySlimeProduceCoins, LuckySlimeFlee, SlimeEatAsh, SlimeEatWater, GotoAsh,
GotoWater and DestroyOnTouching, and by reading the prefabs (InstalledAbilityTests, AbilityTests).
openranch: src/OpenRanch.Simulation/FeedingHabits.cs, game/scripts/Slimes/FeedingBehaviours.cs; checked by
`--slime-zoo --zoo-part feeding`.

## Stalking and pouncing (tabby, saber, hunter: `StalkConsumable`)

- It replaces going straight for food (it removes `GotoConsumable`). Prey: the food it wants most (score
  drive ÷ distance² within `maxSearchRad`) or the player (drive 1 − 0.1). Stalking matters drive² and
  never within 15 s after a pounce.
- Farther than 8 m: it creeps up for up to 3 s, aiming (direction × 400 + up × 650, or × 400 once faster
  than √10 m/s) normalised, pushed with 250 × mass × `pursuitSpeedFactor` at its centre and 450 × mass at
  its base, each times the physics step; out of time, it waits 3 s and starts over.
- Within 8 m: a 2 s butt wiggle, then the pounce (standing): a leap of √(distance × gravity) × 1.2
  along (direction + up) normalised.
- `doesParkour` (sabers): after the wiggle it feints, leaping sideways at a random 55-80° (either side,
  power scaled by `feintPowerMultiplier`); if it touches something within 1.5 s it pounces off it.
- It can't rethink mid-stalk. A pounce that hits the player head-on counts toward the headbutt
  achievement. Hunters cloak while stalking (slime-abilities.md).

## Carrying food (tabby, saber: `GatherIdentifiableItems`)

- A fruit or veggie (`itemClasses`) within `maxSearchRad`, with another food of a different kind
  `minGatherDist` to `maxSearchRad` from it, makes carrying matter a random 0.3-0.5 (so hunger wins).
- It goes to the item (jumps up to `maxJump`), picks it up in its mouth when within 1 m × its scale,
  then hops toward the other food (jump × (item mass + its mass) ÷ its mass) and drops it within 3 m.
  Ten seconds without progress and it gives up; it then rests `pauseBetweenGathers` seconds.

## Gold slimes (`GoldSlimeProducePlorts`, `GoldSlimeFlee`)

- Hit by a food, chick or plort (not ginger, not a gold plort; moving into it faster than 0.02) it
  drops its prefab's plort (a gold plort) just behind itself, hops (one-step force 400 up) and runs from
  the player. Touching the player also sets it running. With the Golden Sureshot upgrade it drops three.
- Running matters 1: it turns away from where the player was and pushes off (300 × mass × flee speed at
  its centre, 540 × mass at its base). Anything blocking its way and it vanishes.

## Lucky slimes (`LuckySlimeProduceCoins`, `LuckySlimeFlee`)

- Hit by a chicken or chick (moving into it faster than 0.02) it gobbles it, then 0.35 s later hops (up 450,
  sideways up to ±225) and drops coin bundles 0.1 s apart: 2 the first time, then double the last, up
  to 6. Each bundle (`ConvertToCurrency`) turns into `amount` money after its `delay`.
- Hit, or seen by the player (the player entering its trigger), it vanishes 600 world seconds
  (10 game minutes) later.

## Fire slimes on ash, puddle slimes in water (`SlimeEatAsh`/`SlimeEatWater`, `GotoAsh`/`GotoWater`, `DestroyOnTouching`)

- Touching ash (an ash trough's ash) or water, hungry past `minDriveToEat`, every `eatRate` seconds it
  takes a bite: hunger −`drivePerEat`, agitation −`agitationPerEat`, and 2 s later one plort (the
  component's `plort`) appears 0.5 m above its centre moving up 1 m/s, growing in over 0.5 s.
- Puddles check crowding every 2 s: more slimes than `maxSlimeDensity` within `slimeDensityDistance`
  (it blushes), or else more puddle plorts than `maxPlortDensity` within `plortDensityDistance`: then a
  bite makes no plort.
- Standing on anything but its ash or water for `DestroyOnTouching.hoursOfContactAllowed` makes it
  poof (a puddle waters what is around). Heading back to the nearest ash or water within 30 m matters
  1 − the share of that time still left.

## Not done yet (openranch)

- Ash and water in the world (ash troughs, ponds): the world's owners add `FeedingPatch`es to
  `ItemCatalog.Patches`; DestroyOnTouching's contact rules are simplified to "standing outside the patch".
- Golden Sureshot; the gold slime's pause while chomping; the lucky slime's trigger size is read from its
  root trigger if it has one; coins go to `ItemCatalog.Wallet` (set by the zoo; M2World should set it).
- Glitch slimes, quicksilver slimes' path following and shock reactions.
