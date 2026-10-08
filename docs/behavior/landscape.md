# Landscape materials

How ground and rock surfaces are textured. The layers and their scales come from the materials in
the game's data. How the layers combine is our own approximation, tuned against screenshots of the
original at the five reference spots on The Ranch; the original shader itself isn't available in
readable form.

## Layers a landscape material names

| Slot | What it holds |
| --- | --- |
| `_PrimaryTex` | rock for the sides, projected from three sides in world space |
| `_DetailTex` | a second rock texture mixed into the sides (when `_EnableDetailTex` is on) |
| `_DetailNoiseMask` | a large, slowly varying noise (clouds, curls) that decides where the detail shows |
| `_Topper_MainTex` | dirt, sand or moss on surfaces that face upward |
| `_TopperDetailTex` | a second top texture mixed in through the same noise (when `_TopperEnableDetailTex` is on) |
| `_TopperDepth` | a height map that roughens the edge where the top layer ends |
| `_VerticalRamp` | grey strata bands that run around the rock by height (0.5 is neutral) |
| `_RampTop`, `_TopRampOffset`, `_TopRampScale` | a colour that tall areas fade toward, from the offset height over the scale |
| `_SeaLevelRampLower`, `_SeaLevelRampOffset` | a colour that areas near and below sea level fade toward |
| `_TopperCoverage` | how far down the slopes the top layer reaches |

Each texture slot's scale is in world units (for example 0.0125 means one repeat every 80 m).

## What we observed

- On The Ranch the open ground is mostly the bright sand of the top detail texture, with the darker
  dirt of the top texture showing in curling swirls where the curl noise is high.
- Cliffs show the strata bands clearly. The top layer stays on surfaces that face mostly upward even
  when the coverage value is above 1.
