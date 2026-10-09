# What the world shows when a game starts

Notes on objects in the world scene that the game switches on or off while it runs, so openranch
can start from the same picture. Checked by reading the world scene's script data and by comparing
screenshots with the original game.

## Gadget build sites

Every gadget build site on a ranch has a projected glow, a circle and an icon. These only appear
while the player is in gadget mode. Each site carries a small script (`DeactivateBasedOnGadgetMode`)
that names one object, `toDeactivate`, and a flag, `activateOnModeOff`:

- With the flag off, the named object is visible only in gadget mode.
- With the flag on, it is the other way round: visible only outside gadget mode.

The player starts outside gadget mode, so openranch leaves out every object named by a script with
the flag off, together with its children.

## Ranch upgrades and other progress-based objects

Many objects exist in several versions, for example the porch in front of the ranch house
(a plain wooden deck at first, a bigger stone-and-wood porch later) and the ranch gates. Each version
carries a script (`ActivateOnProgressRange`) naming one of the player's progress counters
(`progressType`) and a range (`minProgress` to `maxProgress`). The version shows only while the
counter is inside the range, both ends included. The counters are the ones stored in the save under
the player's progress. A counter the save doesn't mention counts as 0.

## Objects tied to the time of day

Some decorations, such as lamps on the upgraded gates and the slime statue's night glow, are listed by
a script (`EnableOnlyDuringTimeWindow`) with a start and an end hour. They show only while the hour of
the day is between the two, both included. When the start is later than the end, the window wraps
past midnight (for example 18 to 6 means evening through early morning).

openranch starts from a new game at 9:00 on day 1 (see [day-cycle.md](day-cycle.md)) unless it is given a
save, in which case it uses that save's progress counters and world time. The clock runs, so objects with
a time window switch on and off as the hours pass.
