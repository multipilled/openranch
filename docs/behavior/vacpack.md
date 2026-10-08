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

## Not modelled yet

- Slimes in the vacpack keep their averaged mood (hunger, agitation) and get it back when shot out.
- The Nimble Valley mode's separate three-slot pack.
