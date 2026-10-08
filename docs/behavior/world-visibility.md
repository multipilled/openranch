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
