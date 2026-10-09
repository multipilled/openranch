# Day and night: fog, ambient light, sky and sun

How the world's look changes through the day. Read from the game's data (the zone ambience settings
assets and the world scene's lights) and checked against screenshots of the original at noon on The
Ranch, with the values the original reported at runtime.

## Zone settings

Each zone has a settings asset (`AmbianceDirectorZoneSetting`) with a day and a night value for:
fog colour, fog density, ambient light colour, sky colour and sky horizon colour. Each world cell names
the zone it belongs to; cells that name none, The Ranch among them, use the `DEFAULT` settings.

The original renders in gamma colour space. Ambient light is one flat colour, fog is exponential
(the fraction of a surface's colour that survives at distance d is e^(−density·d)), and the sky is
a gradient from the horizon colour to the sky colour.

## Blending through the day

Let f be the time of day as a fraction (0 at midnight, 0.5 at noon).

- **Night amount** n = clamp((|0.5 − f| − 0.15) × 5, 0, 1). It is 0 from about 9:36 to 14:24, rises
  to 1 by about 4:48 in the morning and from about 19:12 in the evening.
- Fog colour, ambient colour, sky colour and horizon colour go from the day value to the night value
  in a straight line as n goes from 0 to 1.
- Fog density uses n⁶ instead of n, so it stays near the day value until late dusk.

## Sun and night light

The world scene's "Range Lights" object holds two rigs, "Range Lights - Day" and "Range Lights - Night",
each marked as a day or night rig by a `TimeOfDayRotator` script. Every light under a rig follows it.
Through the day each rig turns about its own X axis by 360 × (f − 0.5) degrees, so at noon it points as
stored in the scene; a light under it keeps its place relative to the rig.

- The day rig's lights (the sun) shine from 6:00 to 18:00. They fade in over the first 5 game minutes
  after 6:00 and fade out over the last 5 before 18:00 (strength (f − 0.25) × 288, at least 0.01).
- The night rig's lights do the same from 18:00 to 6:00.
- A light's intensity is its stored intensity times the strength (times 1 − cave darkness), held between
  0.001 and 1 while the strength is above 0, and 0 when it is 0.
- Only the "Light - Main" under each rig shines on The Ranch: the "Light - Fill" lights are stored
  switched off. The night rig itself is stored switched off too; a script on "Range Lights"
  (`EnableObjectsOnAwake`) switches it on as the scene starts. The sun's stored intensity is 0.667, the
  night light's 0.8, both directional.

Checked: the rigs, their switches and the stored lights read from the world scene
(`TimeOfDayLight.Read`); the turn, strengths and clamp from static analysis of the ambience director
(`AmbianceDirector`) and its rotator and awake scripts. `--day-check` compares the lights openranch
sets at four hours with these rules.

At noon on The Ranch the original reports: fog density 0.005, a dim orange sun at intensity 0.667,
and a warm flat ambient light. Most of a surface's brightness comes from the ambient light.

## Lamps and cave lights

The scene's point and spot lamps (teal lamps on ranch machines, torches, teleporter glows) keep their
stored colour, intensity and range. Unity's lamps fade with distance d as 1 / (1 + 25 (d / range)²)
and stop at the range.

A cave trigger lists the lamps that belong to its cave (each carries a `CaveLightController`). Those
lamps are off while the player is outside every cave that lists them. On entering, they fade up to
their stored intensity over the time the ambience takes to change zone; on leaving, they fade out again.
That time is the ambience director's `zoneSettingTransitionTime`, 1.25 seconds in the install (read by
`WorldTime.Create`); the cave darkness that dims the sun moves at the same rate.

## In openranch

`game/scripts/World/WorldTime.cs` runs the world clock (see day-cycle.md) and, every frame, hands the
hour to `WorldLighting` (fog, ambient, sky, both rigs, the blends above) and to `TimedObjects`, which
switches the objects tied to the time of day (docs/behavior/world-visibility.md) as their hours come and
go: hidden objects draw, light and collide with nothing. On The Ranch there are 9 such objects: 6 shown
from 6:00 to 18:00, 1 from 18:00 to 6:00 and 2 from 17:00 to 7:00, holding 3 lamps between them
(`--day-check` lists them).

The sky is drawn as a gradient from the blended horizon colour to the blended sky colour.
