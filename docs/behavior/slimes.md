# Slimes: hunger, eating and plorts

How an ordinary slime (Pink, Tabby, Rock and the like) decides to eat and what it makes. Largos and
tarrs are in `largos.md`, species traits in `slime-traits.md`; gordos and feral slimes are only
touched on at the end.

Code: `src/OpenRanch.Simulation/Slime.cs`, `SlimeSpecies.cs`, `Items.cs`. Data readers:
`src/OpenRanch.Formats/Game/SlimeData.cs`.

## Where the numbers come from

Each slime type has a **slime definition** (script `SlimeDefinition`, in `resources.assets`). Its
`Diet` holds:

- `MajorFoodGroups`: whole food groups the slime eats (see below).
- `AdditionalFoods`: single extra items it also eats.
- `Favorites`: items it likes best.
- `Produces`: what it makes after a meal (normally its own plort).
- `FavoriteProductionCount`: how many of each product a favorite meal makes.

Each slime's prefab carries two more script components, read from the install:

- `SlimeEat`, linked to the definition by its `slimeDefinition` field: `minDriveToEat` (how hungry it
  must be to eat), `drivePerEat` (how much one meal satisfies it), `agitationPerEat` and
  `agitationPerFavEat` (how much a meal calms it) and `chanceToSkipProduce` (chance a meal makes
  nothing; zero for the common slimes).
- `SlimeEmotions`: for hunger and agitation (`initHunger`, `initAgitation`), the starting value
  (`currVal`), the value it drifts back to on its own (`defVal`) and the drift speed per game hour
  (`recoveryPerGameHour`).

Checked: read from the game's data with `openranch-import scripts --dump SlimeDefinition` and the
install tests in `tests/OpenRanch.Simulation.Tests`. Pink, Tabby and Rock share the same eating and
emotion tuning in the release we read.

## Item kinds

Every item in the game has an id with a name such as `CARROT_VEGGIE` or `STONY_HEN` (the enum
`Identifiable.Id`, read from the game's assembly metadata). The game sorts items into kinds by the
end of the name:

| Name ends with | Kind |
| --- | --- |
| `_VEGGIE` | veggie |
| `_FRUIT` | fruit |
| `_TOFU` | tofu |
| `_SLIME` | slime |
| `_LARGO` | largo |
| `_GORDO` | gordo |
| `_PLORT` | plort |
| `HEN` or `ROOSTER` | meat (all chickens) |
| `CHICK` (exactly) or `_CHICK` | chick |
| `_LIQUID` | liquid |
| `_CRAFT` | craft material |

The first matching row wins. This rule lives in the game's code, not its data.

## Food groups

A diet's `MajorFoodGroups` name groups of items:

- **Fruit**, **Veggies**, **Meat**: every item of that kind.
- **Ginger**: only the ginger veggie.
- **Plorts**: every plort except the puddle, gold and fire plorts.
- **Slimes** (`NONTARRGOLD_SLIMES`): every slime and every largo except tarrs (the tarr and glitch
  tarr), the gold slime and the lucky slime. (The game's "is a slime" test counts largos.)

A slime eats the union of its food groups and its additional foods. The exceptions above are
code-only, so they are kept as named constants in `Items.cs`. The game builds one eat rule per food
and product, so a diet with no products (fire, glitch, puddle and quicksilver slimes) eats nothing
this way, and a product of "nothing" (the lucky slime) means it eats and makes nothing.

## Hunger

Hunger and agitation are numbers from 0 to 1.

- A new slime starts at its starting hunger. On its own, hunger moves steadily toward its rest
  value (1, fully hungry, for ordinary slimes) at the drift speed per game hour; from a fresh slime
  that is several game hours. Agitation likewise settles toward its rest value (0).
- A slime counts as **hungry** at 0.666 and **starving** at 0.99 (these thresholds are in code, not
  data; they drive faces and moods). An **angry** slime has agitation of at least 0.9.
- While a slime is starving, its agitation stops settling and creeps up instead: starvation pushes it
  up at 0.416667 per game hour, against the normal settling speed, so the net climb is the
  difference. (Code-only constant; `Slime.StarvingAgitationPerHour`.)

## Deciding to eat

- A slime **wants to eat** (goes looking for food) when its hunger is above its `minDriveToEat`.
- It **will eat** a particular item that touches it when the item is in its diet and its hunger is
  at least `minDriveToEat`. The one exception is spicy tofu: slimes that eat it want it at any hunger,
  and eating it does not lower hunger. (Code-only; `SlimeSpecies.AlwaysWantedFood`.)
- Things outside the diet are never eaten (largo-making plorts aside, below).

## Eating and plorts

- Each meal lowers hunger by `drivePerEat` and agitation by `agitationPerEat`, or by
  `agitationPerFavEat` if the food is a favorite.
- About two seconds after swallowing (code-only, `Slime.DigestSeconds`) the slime produces its
  products: one of each item in `Produces` for an ordinary food, or `FavoriteProductionCount` of
  each for a favorite. For the common slimes this means one plort per meal and two per favorite
  meal.
- If the slime's `chanceToSkipProduce` is above zero, a meal may produce nothing.

Checked: counts read from the data (the install test feeds Tabby and Rock slimes their favorites
and gets `FavoriteProductionCount` plorts). Not yet compared against a recording.

## Game time

Hunger drifts per game hour. The `TimeDirector` component (in `level2`, beside the other directors) sets how many real seconds a
game day lasts (`secsPerGameDay`; 24 real minutes in the release we read), so one real second is
24 ÷ `secsPerGameDay` game hours. Read from the game's data.

## In the world: the body

- A slime is a ball that rolls and bounces under physics. Its prefab (the game object the game's
  item lookup list gives for the slime's id) carries the body's collider (a sphere), mass, drag and
  angular drag. Its collider's physics material ("Slime": friction 0.8 dynamic, 0.5
  static, bounciness 0.6; friction combined by taking the larger value) makes it grip and bounce, so
  it rolls rather than slides. Its `KeepUpright` component (`stability`, `speed`) rights it: every
  physics step it twists the slime's up direction, as it will be after spinning for spin ×
  `stability` ÷ `speed` seconds, toward the world's up, with a strength of `speed`² × mass. Turning
  toward a heading works the same way with the behaviour's `facingSpeed` and `facingStability`
  (looking ahead spin × `facingStability` × 0.1 ÷ `facingSpeed`). Read from the game's data and
  static analysis; in openranch the world counts as Unity's default material (friction 0.6, no
  bounce) when combining.
- What the slime looks like is not stored on the prefab itself. The slime definition names its
  default appearance (`AppearancesDefault`), which lists parts (`Structures`); each part names a set
  of model objects, one per level of detail (`Element.Prefabs`, each with a `LODIndex`), the bone of
  the slime's skeleton it hangs from (`ParentBone`, or the prefab's appearance root when none) and
  the materials to draw it with (`DefaultMaterials`, unless the part overrides them). Parts that
  support faces draw the face's eyes and mouth as extra layers on the same model. The default face
  expression is "happy". See `docs/formats/prefabs.md`.

Checked: read from the game's data with openranch's prefab reader.

## In the world: moving and finding food

Studied on a developer's PC from how the original works; tuning values named here are read from the
slime prefab's components.

- A slime decides what to do about once a second, choosing the activity that matters most right now.
  It does nothing for its first three seconds in the world, and it only pushes itself around while it
  stands on something (checked with a short look straight down, a little longer than half its height,
  four times a second).
- **Wandering** always matters a little (0.2). Every ten seconds it picks one of three moods, resting
  (one time in five), scooting (three in ten) or hopping (half the time), and a heading within about
  30 degrees (half a radian) either side of where it faces. While hopping it jumps at most once a
  second, and only when it is moving slower than 5 m/s: an upward kick of between half and all of a
  jump strength of 6 (times its mass, times the prefab's `SlimeRandomMove.verticalFactor`) and a
  random sideways kick of up to half that strength in each direction. While scooting it turns toward
  its heading and slides forward in pulses, once a second.
- **Going for food** matters when there is something it wants within reach. The prefab's
  `GotoConsumable` component sets the search radius (`maxSearchRad`), the jump strength
  (`maxJump`), turning (`facingSpeed`, `facingStability`), speed (`pursuitSpeedFactor`) and patience
  (`attemptTime`, `giveUpTime`). Among the items in its diet within the search radius, it goes for the
  one with the best ratio of drive (its hunger, for ordinary food) to squared distance. Food seeking
  matters as much as the drive squared times 0.95, so it beats wandering once hunger is above about
  0.46.
- While going for food it turns toward the target. When something stands in the way it jumps toward
  the target, at most once a second, with a strength that grows with hunger: 40% of `maxJump` up to
  the hungry cutoff (0.666), rising to the full `maxJump` at hunger 1, with the square of how far
  hunger has got from the cutoff toward 1. The jump aims upward and toward the target: the direction
  is straight up plus the direction to the target scaled by the distance over 30 m (at most 1). The
  kick is the strength times the slime's mass. Otherwise it slides toward the target: within 3 m with
  a steady push (a force) of 480 × mass × `pursuitSpeedFactor` × the original's fixed physics step
  (the TimeManager's fixed timestep in `globalgamemanagers`), farther out in pulses (150 × mass
  × `pursuitSpeedFactor` times a pulse that swings between 0 and 2 once a second, plus a push of 270 ×
  mass × pulse at the bottom of the body that rolls it forward), all sized by the same fixed step.
- If it hasn't reached the food after `attemptTime` seconds it gives up: its agitation rises by 0.1
  and it ignores food for `giveUpTime` seconds.
- Tabbies don't have `GotoConsumable`; they stalk their food (`StalkConsumable`: creep up, wait, then
  pounce from about 8 m). Its food seeking matters as much as the drive squared.

The numbers without a field name are in the game's code, not its data (named constants in
`src/OpenRanch.Simulation/SlimeMotion.cs`).

openranch for now: a wandering slime rests or hops (scooting is left out); a slime going for food
slides toward it and jumps when something is in the way, as above. Tabbies go for food the plain
way, with the search radius from their `StalkConsumable` and, as it sets no jump strength, the
original's default for `GotoConsumable` (12). Stalking and pouncing are left for later.

## In the world: eating

Studied on a developer's PC from how the original works.

- When something touches a slime, the slime eats it if it is in its diet and the slime is hungry
  enough (the rule in "Deciding to eat"). It turns to face the food and bites; a bite started by
  touch takes a quarter of a second, then the food is gone and the meal counts.
- Two seconds after the bite (`Slime.DigestSeconds`) the products pop out half a metre above the
  slime's centre, in its own up direction, moving up at 1 m/s and growing from almost nothing to full
  size over half a second.
- A slime is busy while biting and can't start another bite until it's done.

## Plorts in the world

Plorts are small physics items, with their own prefab (collider, mass, drag). The prefab's
`DestroyPlortAfterTime.lifeTimeHours` says how many game hours a plort lasts before it vanishes.
openranch reads it but doesn't remove old plorts yet.

## Not modelled yet

- Feral slimes eat whatever touches them regardless of hunger.
- Toys, music boxes and nearby pollen change how fast agitation settles; mods (game-mode cheats)
  scale hunger speed.
- Slimes in the Slimulations area have hunger switched off.
