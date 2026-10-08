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

The scene has a day light (the sun) and a night light, both directional. They turn about their own X
axis by 360 × (f − 0.5) degrees, so at noon they point as stored in the scene.

- The sun shines from 6:00 to 18:00. It fades in over the first 5 game minutes after 6:00 and fades out
  over the last 5 before 18:00. Its brightness is the stored intensity times that fade.
- The night light does the same from 18:00 to 6:00.

At noon on The Ranch the original reports: fog density 0.005, a dim orange sun at intensity 0.667,
and a warm flat ambient light. Most of a surface's brightness comes from the ambient light.

## Lamps and cave lights

The scene's point and spot lamps (teal lamps on ranch machines, torches, teleporter glows) keep their
stored colour, intensity and range. Unity's lamps fade with distance d as 1 / (1 + 25 (d / range)²)
and stop at the range.

A cave trigger lists the lamps that belong to its cave (each carries a `CaveLightController`). Those
lamps are off while the player is outside every cave that lists them. On entering, they fade up to
their stored intensity over the same one second the ambience takes; on leaving, they fade out again.
