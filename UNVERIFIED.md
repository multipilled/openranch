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
