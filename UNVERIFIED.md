# Unverified against Slime Rancher: audit list

Rule: every value and behavior comes from the game's own data (read at runtime) or from static analysis
of its code, written up in docs/behavior. Anything OPEN is a stand-in that must be replaced or proven
before other work relies on it.

## Done (sourced from the game)

| Item | Source |
|---|---|
| Fog, ambient, sky colours and density by hour; cave ambience | AmbianceDirectorZoneSetting assets; runtime dump at noon on The Ranch |
| Sun direction, colour, intensity at noon | Range Lights / Light - Main in the world scene; runtime dump |
| Camera height 1.75 m, 0.1 m ahead, FOV 75 | runtime camera readout from the original (reference captures) |
| Which upgrade, night-only and gadget-mode objects show | ActivateOnProgressRange, EnableOnlyDuringTimeWindow, DeactivateBasedOnGadgetMode data |
| Colour-mask regions and their material colours | RanchDirector palette slots (_Color00.._Color71), mask texture channels |
| Lamps and cave lights (colour, intensity, range, which lamps a cave switches) | Light components, CaveTrigger / CaveLightController data |

## Open

| Item | Stand-in in use | What would settle it | Owner |
|---|---|---|---|
| Landscape layer blending (detail and noise thresholds, strata strength, height tints, topper coverage) | hand-tuned against 5 reference shots | the compiled SR environment shader's logic | render |
| Lamp falloff | Unity legacy curve 1/(1+25(d/r)^2), fitted at 20% and 50% of the range | Unity 2019's built-in attenuation in gamma mode | render |
| Sun colour conversion from gamma-space lighting | per-channel match of ambient + light | side-by-side measurements at more spots and hours | render |
| Corral activator glow colour (blue in openranch, green in the original) | material glow ramp | what sets the glow at runtime | render |
| House window interior | room texture under tinted glass | the window shader's interior mapping | render |
| Vegetable ramp lookup position | 0.15 + 0.7 x occlusion | the vegetable shader | render |
| Fog distance measure | view depth | the original fog's distance (depth vs radial) | render |
| Daily plort price noise | own smooth noise with the same spread | the original's noise function, or a documented deliberate difference | m2 |
| Which expansion barriers a save has opened (Lab, Grotto, Overgrowth and the passages between them) | open when every `UNLOCK_<NAME>` progress named in the barrier's cell or object name is > 0; door barriers follow the save's door state | the original's barrier rule (code only) | m3 |
| Saved slime facing | Godot yaw = -Unity euler Y; pitch and roll ignored | slimes' saved rotation compared in game | m3 |
| In-game save key and folder | F5 writes `user://saves/<game>.ranch.json` | the original saves when sleeping; decide the save flow (M7) | m3 |
| Timers of actors created in play (plort `destroyTime`, transform/reproduce times) | written as 0 (the record default) | model plort expiry and slime timers | m3 |
| Fear of slimes created in play | not written; loaded slimes keep their saved fear | model fear | m3 |
| HUD clock readout | "Day N, HH:MM" top right, openranch's own | the original's HUD clock | day cycle |
| Several time-window scripts on one object | shown only while all windows are open | the original's last-script-wins order (doesn't occur on The Ranch) | day cycle |
| Sky day/night blend | Godot gradient between blended horizon and sky colours | the original's sky shader blend values | day cycle |
| Which day/night lights cast shadows | all directional lights, 150 m | the Light component's shadow setting | day cycle |
| Produce re-attaching to crops | only crops inside the zone; one just outside within 10 m is ignored | the original's attach search | m3 |
| Ripe produce let go by the vacpack | drops straight down; the release kick and spin are skipped | the crop joint's release code | m3 |
| Unripe produce size | a third of full size, collision shapes shrunk the same | the original's growth scale | m3 |
| Rotten produce look | looks like normal produce | the rot material switch | m3 |
| Deluxe coop | its regions are not made deluxe | the deluxe coop's behaviour | m3 |
| `--m3-check` floor test | things 1.5-3 m above a plot's middle count as clear | (check only, not game behaviour) | m3 |
| Interact key | E | the original's default binding | m3 rest |
| Ranch house screen | none: using the door sleeps at once; no save on sleep | the ranch house UI and save-on-sleep | m3 rest |
| Ranch house door collider | an area on its own layer (bit 20) rays hit but the player passes | the original's solid box | m3 rest |
| Daily plort price drift | openranch's drift seeded from the save's seed (same every load, differs from the original) | the original's noise function | m3 rest |
| Vacpack slot fear | only hunger and agitation modelled; saved fear kept unchanged | model fear | m3 rest |
| Player frozen while asleep | movement processing switched off | the original's freeze | m3 rest |
| Zone start point | the debug start if any, else the first non-echo-gordo teleporter destination | (openranch's own choice; destinations are data) | m4 zones |
| Moss Blanket start | echo-gordo teleporter in the entrance cell (the Hobson vault teleporter is hidden in a new game) | the original's new-game arrival | m4 zones |
| Terrain height sample order | rows along z | a terrain that is the only ground in a spot with asymmetric data | m4 zones |
| Largo/tarr growth on transform | full collider at once, lifted by the radius difference | the original's 0.5 s scale-up | m5 slimes |
| Transparent slime parts (render queue > 2500, e.g. rad aura) | not drawn | the effect shaders | m5 slimes |
| Trait vs food-seeking tie | the trait wins | the original's prefab component order | m5 slimes |
| `--slime-zoo` set-up | floor, 4 m pens, items-only walls, every slime fully hungry | (test set-up only) | m5 slimes |
| Largo meal effect | counts as two meals (hunger and agitation drop twice) | a recording of a largo eating | m5 slimes |
| Slime health recovery between tarr bites | none | the health component's recovery | m5 slimes |
| Teleporter trigger layer | an openranch-only collision bit (`WorldMap.TriggerBit`) | the original's trigger layer | m4 world |
| Teleporter with several destinations | picked with a fixed seed | the original's shared random generator | m4 world |
| Actors outside every loaded cell | frozen | the original's wake box (50 x 200 x 50 m) | m4 world |
| Fall damage | none | the original's `FallDamager` | m4 world |
| `--world-check` route planner limits (2 m grid, 12 m jetpack climb, 40 m drop) and end point | test settings only | (check only) | m4 world |
| Player health, radiation and knockback | a value plus an event; knockback is a velocity nudge | `PlayerVitals` and the player's physics | m5 abilities |
| Ability visuals, auras, faces, sounds, poofs | nothing drawn | the effect prefabs and shaders | m5 abilities |
| Water calming | not modelled | the water-calm component | m5 abilities |
| Dervish whirlwind and mini tornado motion | approximated; no vortex | the whirlwind component | m5 abilities |
| Quantum qubits | appear in place, simplified timing | the qubit spawner | m5 abilities |
| Tangle vine | drops the food onto the slime | the vine path | m5 abilities |
| Mosaic glints | one object for all three phases, never burst | the glint component | m5 abilities |
| Ash and water for fire/puddle slimes | simple boxes (`FeedingPatch`), poof clock simplified | the original's patches | m5 abilities |
| Golden Sureshot, gold slime chomp pause | not modelled | the vacpack upgrade and gold slime code | m5 abilities |
| Feral aura, glaring, "no hostiles" mode, bites while held | missing | the feral components and game settings | m5 abilities |
| Gordo snares, event gordos, holiday crates | not modelled | their components | m5 abilities |
| Random draws for produce, coops, machines | openranch's own `System.Random` | the original's shared random source | m6 plots |
| Rotten produce look | dark tint | the prefab's rotten material | m6 plots |
| Ripe produce falling off | small upward push, spawned without the joint's rotation | 80 N along the joint plus random spin | m6 plots |
| Plort collector | takes plorts at once; centre ± radius against the area box | its reel-in over a few seconds | m6 plots |
| Silo output and "vacpack is pulling" | no hold speed-up; suction cone on the catcher's centre within 45° | the silo's output code | m6 plots |
| Feeder push | one physics step of the original's force, Godot noise | the feeder's force code | m6 plots |
| Hens finding a rooster and counting the crowd | by distance over all live actors | the original's same-cell lists | m6 plots |
| Incinerator trigger | a copy of its solid collider | its own trigger shape | m6 plots |
| Food counted for ash | approximated by item kind | the incinerator's rule | m6 plots |
| Rain | none; crops' stored water only drains | the weather/rain system | m6 plots |
| Far areas | never paused; everything keeps growing | the original's area catch-up | m6 plots |
