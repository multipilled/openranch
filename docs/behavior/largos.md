# Largos and tarrs

How a slime turns into a largo, and a largo into a tarr. Ordinary eating is in `slimes.md`.

Code: `src/OpenRanch.Simulation/Largos.cs`, `SlimeSpecies.cs`, `Slime.cs`; in the world
`game/scripts/Slimes/SlimeActor.cs`. Data reader: `src/OpenRanch.Formats/Game/SlimeData.cs`.

## What the install holds

Slime Rancher 1.4.4 defines 113 slime types (`SlimeDefinition`): 22 ordinary ones (tarr and glitch
tarr among them) and 91 largos. Every one has a prefab in the item lookup list and eating settings.

- A largo's definition has `IsLargo` set and names its two **base slimes** (`BaseSlimes`). Its diet
  is stored already combined: food groups, favourites, extra foods and products are the two parents'
  together (in the first parent's order, then the second's new ones), and the favourite production
  count is the first parent's. So a largo eats what either parent eats and makes both plorts.
- A slime that can become a largo has `CanLargofy` set: pink, rock, tabby, phosphor, honey, boom,
  rad, crystal, hunter, quantum, dervish, mosaic, tangle and saber. Gold, lucky, tarr, glitch tarr,
  fire, glitch, puddle and quicksilver slimes can't.
- Largo and tarr prefabs are scaled 2 at the root (so twice the size and, with the same body, twice
  the collider radius; mass 1 rather than 0.5). `PrefabScale` in the definition is 2 for largos and
  tarrs and 1 otherwise; no code applies it at runtime.
- A largo's appearance is stored, already combined, as its own appearance object (for example
  "PinkNormalRockNormal"): a body part with the largo's own body material ("slimePinkRockBase"),
  plus the parts of both parents (rock spines, tabby ears and tail, honey plate and so on), each with
  largo-specific materials, and its own face. It lists the parents' appearances as
  `DependentAppearances`. (The game's code can also combine two appearances at runtime: the first
  parent's face, then both parents' parts, each part once; the stored largos follow that pattern.)
  openranch draws a largo with the same prefab reader as any slime.

Checked: read from the install (`InstalledLargoTests`, and a listing of every definition).

## Which plort does what

Each slime has a set of eat rules, built when the game starts. Besides one rule per diet food and
product (see `slimes.md`), a slime that makes plorts and either can become a largo or already is one
gets one rule per plort:

- Plorts considered: every plort in the plorts food group (all plorts except the puddle, gold and
  fire plorts), except the **quicksilver plort** and except the slime's own products.
- A base slime eating such a plort **becomes the largo** whose two base slimes make that plort and the
  slime's own first product (either order). If no largo has that pair, there is no rule and the plort
  is ignored.
- A largo eating such a plort (one that isn't one of its own two) **becomes a tarr** (`TARR_SLIME`).
- These rules are driven by **agitation**, with a floor of 0.5: the slime wants the plort at its
  agitation or 0.5, whichever is higher. With the usual eating threshold of 0.333 that means it
  always eats such a plort that touches it, and it goes for one nearby (0.5² × 0.95 beats wandering).
- The **honey plort** carries an extra drive of 0.5 for every slime that has a rule for it, so it is
  wanted at 1 or more.

These numbers and ids are in the game's code, not its data (named constants in `Largos.cs`).
Checked by static analysis of SlimeDiet, SlimeDefinitions and SlimeEat.

## Turning

When the bite finishes (a quarter of a second after touching, like any bite) the slime turns at once,
with no digesting:

- The eaten plort is gone; the slime is removed and the new one (largo or tarr) appears at the same
  place, facing the same way.
- The new slime takes over all of the old one's feelings (hunger, agitation and the rest).
- It springs from the old one's size to its own over 0.5 s with an elastic ease (overshooting, then
  settling).
- Like any meal, the bite lowers the driving feeling (here agitation) by `drivePerEat`, and agitation
  again by `agitationPerEat`, on the old slime before its feelings pass over.

openranch: the new slime's collider is full size at once and it starts lifted by the difference in
radius, so it doesn't start inside the ground (the original grows the whole object, collider
included). The transform effect, sound and the "largo transformed" hooks (feral hunter largos) are
not done yet.

## Largos eating food

A largo's food matches two eat rules, one per plort. Each matching rule runs in turn: each makes its
plort (one, or the favourite count for a favourite) and each counts as a meal, so a largo's hunger
drops by twice `drivePerEat` per food and its agitation by twice the calming amount. Each product may
be skipped on its own when `chanceToSkipProduce` is above zero. Static analysis of SlimeEat.

## Tarrs

The tarr's diet is the slimes food group: every slime and every largo except tarrs, the gold slime
and the lucky slime. It "produces" another tarr. A tarr's bite only hurts a slime: the bitten slime
loses the tarr's `SlimeEat.damagePerAttack` (20) from its health (`SlimeHealth.maxHealth`, 100), and
only the bite that takes the last of it swallows the slime and makes a new tarr. Every bite still
counts as a meal for the tarr's hunger. A meal that makes nothing (the lucky slime's diet produces
"nothing") likewise just counts as a meal.

Not done yet: tarr grappling and the bite animation, health recovery, tarrs rotting food and the
"no hostiles" game mode, which drops the tarr rules.

## The zoo

`--slime-zoo` puts every slime and largo in its own pen with a food (see `game/scripts/Slimes/
SlimeZoo.cs`) and checks that each eats and makes its plorts, and that a pink slime given a rock plort
becomes the pink-rock largo and then, given a tabby plort, a tarr.
