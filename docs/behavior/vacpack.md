# The vacpack

How the player's vacpack stores what it sucks up.

Code: `src/OpenRanch.Simulation/Vacpack.cs`.

Checked: the slot count, slot rules and limits were studied on a developer's PC from how the
original works; they are not in the game's data files. Not yet compared against a recording.

## Slots

- The vacpack has **five slots**. Four are usable from the start; the fifth opens with the liquid
  slot upgrade.
- The first four slots take anything solid that can be vacuumed and never liquids. The fifth slot
  takes only liquids.
- Each slot holds one kind of item at a time and empties (becomes free for any kind) when its last
  item is shot out.
- The game keeps a list of which items can be vacuumed at all (the player object's list of
  potential ammo, in the world scene). openranch's vacpack takes a filter for this.

## Limits

Every slot has the same limit, raised by the vacpack capacity upgrades:

| Upgrades bought | Items per slot |
| --- | --- |
| none | 20 |
| 1 | 30 |
| 2 | 40 |
| 3 | 50 |
| 4 | 100 |

These numbers are in the game's code, not its data, so they are named constants
(`Vacpack.SlotLimits`). A few glitch items in the Slimulations area use their own limits from data;
they are not modelled yet.

## Sucking things up

- A new item joins the slot that already holds its kind. If that slot is full, the item is refused,
  even when another slot is empty.
- Otherwise it goes into the first empty slot that accepts it.
- If neither works, the item is refused and stays in the world.
- One item fills one count, except water, which fills five per suck (also code-only,
  `Vacpack.WaterPerVac`). Magic water (from a magic water source) fills its slot at once.

## Shooting and choosing

- The player picks a slot with the number keys or by cycling forward or back through the usable
  slots; cycling wraps around.
- Shooting takes one item from the selected slot.

## In the world: sucking things up

The player's `WeaponVacuum` component (on the player rig in the world scene) holds the tuning:
`maxVacDist`, `captureDist`, `minJointSpeed` and `maxJointSpeed`, `ejectSpeed` and `shootCooldown`.
The suction zone is the rig's "vac shape" object, a child of the first-person camera: a row of
trigger capsules that widen with distance, together a cone pointing where the player looks. Read
from the game's data.

Studied on a developer's PC from how the original works:

- While the vac button is held, every item in the cone that can be vacuumed, and that the nozzle can
  see (nothing solid between them, within `maxVacDist`), is caught: it stops falling and is reeled
  toward the nozzle. The reel moves at `maxJointSpeed` when the item is close and slows to
  `minJointSpeed` at `maxVacDist`, so far items come in slower.
- Once a caught item is within `captureDist` of the nozzle, it shrinks into the nozzle over 0.2
  seconds and joins the vacpack (the slot rules above). If the vacpack has no room for it, it is let
  go again and grows back.
- Letting go of the vac button releases everything still being reeled in.
- Items carry a size: ordinary items go into the vacpack; large ones (largos, for example) are held
  in front of the nozzle instead. openranch doesn't hold large items yet.

## In the world: shooting

- While the shoot button is held, the vacpack shoots one item from the selected slot every
  `shootCooldown` seconds, the first one at once.
- A shot item appears just in front of the nozzle (pushed a little further out when shooting
  downward) and flies along the view at `ejectSpeed`, plus the player's own velocity. It grows from a
  fifth of its size to full size in a tenth of a second.
- A shot item is "launched" until it first touches something solid other than the player. While
  launched it passes through corral walls (see `corrals.md`), so slimes can be shot into a corral.
- Shooting from an empty slot does nothing (the original plays an empty click).

The keys are openranch's own: hold the right mouse button to vacuum, hold the left button to shoot,
1 to 4 pick a slot and the mouse wheel cycles slots.

## Not modelled yet

- Slimes in the vacpack keep their averaged mood (hunger, agitation) and get it back when shot out.
- The Nimble Valley mode's separate three-slot pack.
