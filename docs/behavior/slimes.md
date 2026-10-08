# Slimes: hunger, eating and plorts

How an ordinary slime (Pink, Tabby, Rock and the like) decides to eat and what it makes. Largos,
tarrs, gordos and feral slimes are only touched on at the end.

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
- **Slimes** (`NONTARRGOLD_SLIMES`): every slime except tarrs (the tarr and glitch tarr), the gold
  slime and the lucky slime.

A slime eats the union of its food groups and its additional foods. The exceptions above are
code-only, so they are kept as named constants in `Items.cs`.

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

## Not modelled yet

- **Largos**: a slime that can become a largo (`CanLargofy`) also eats other slimes' plorts, which
  are wanted whenever it is at least a little agitated (a floor of 0.5), and turns it into the largo
  of the two plort types instead of producing anything. A largo that eats a third plort type becomes
  a tarr.
- Feral slimes eat whatever touches them regardless of hunger.
- Toys, music boxes and nearby pollen change how fast agitation settles; mods (game-mode cheats)
  scale hunger speed.
- Slimes in the Slimulations area have hunger switched off.
