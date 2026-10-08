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
