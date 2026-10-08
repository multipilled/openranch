# Corrals

How a corral keeps slimes in. Checked by reading the world scene and the game's physics settings;
the rule for shot items was studied on a developer's PC from how the original works.

## Walls

- A corral on the ranch is a land plot (`landPlot` with a `patchCorral` child in the world scene).
  Its visible fence posts are ordinary scenery. What keeps slimes in is a set of invisible boxes
  around the plot (`corralBarrier`) on the physics layer named "Pen Walls".
- The game's physics settings (the layer collision matrix in its project settings) let "Pen Walls"
  stop actors (slimes, plorts, food: the "Actor" and "ActorIgnorePlayer" layers) but not the player,
  who walks straight through.
- The plot comes in versions: the base corral with low walls (`Base Corral`) and a higher-walled one
  (`High Wall Corral`) that the wall upgrade switches on. A new game shows the base corral.
- Items the player shoots are on the "Launched" layer, which "Pen Walls" don't stop, until they first
  touch something solid. That is how slimes are shot into a corral over or through the wall.

openranch builds every solid collider of the area that collides with actors but not with the player
into a separate actor-only wall, so corral walls (and any other such barrier) work the same way.
