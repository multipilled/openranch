# Painted objects: ranch tech, the ranch house, corrals

How the game colours its machines and buildings. Read from the materials and textures in the
game's data and from the script that applies ranch colour palettes; checked against screenshots
of the original.

## Colour mask

These materials share a colour-mask texture laid out as an atlas of flat colour bands. The mesh's
first UV set points into it. The mask's red, green and blue channels are each on or off, which marks
eight regions:

| Region | Mask colour | Material colours |
| --- | --- | --- |
| red | red | `_Color00` (dark), `_Color01` (light) |
| green | green | `_Color10`, `_Color11` |
| blue | blue | `_Color20`, `_Color21` |
| black | black | `_Color30`, `_Color31` |
| magenta | red + blue | `_Color40`, `_Color41` |
| yellow | red + green | `_Color50`, `_Color51` |
| cyan | green + blue | `_Color60`, `_Color61` |
| white | all three | `_Color70`, `_Color71` |

Within a region a paint-stroke texture (`_PaintStrokes`, tiled by its scale) picks a point between
the dark and the light colour. With the keyword `_PAINTMASKTRIPLANAR_ON` the strokes are projected
onto the object from three sides instead of following the UVs.

The ranch's colour palettes (chosen by the player at the ranch's paint station) replace these
sixteen colours per palette type (ranch tech, house and so on). A new ranch uses the colours stored
in the materials.

## Other layers

- **Ambient occlusion** (`_AmbientOcclusion`) darkens the colour. With `_AOUV1_ON` it uses the mesh's
  second UV set.
- **Override decals** (`_Override`): small painted details such as screens and labels, laid over the
  colour where the decal's alpha is set. With `_OVERRIDEUV1_ON` they use the second UV set.
- **Glow**: where the mask's alpha is below one, the surface glows with a colour from the glow ramp
  (`_GlowRamp`), scaled by `_GlowMultiplier`.

## Vegetables

Garden plants such as carrot sprouts mark their parts with vertex colours instead of a mask
texture (keyword `_VERTEXCOLORMASK_ON`): red, green and blue vertices and the remaining "black" ones
each take their colour from a small ramp texture (`_RampRed`, `_RampGreen`, `_RampBlue`,
`_RampBlack`), for example an orange ramp for the root and a green one for the leaves. openranch
reads further along the ramp where the plant's occlusion map is brighter.

## House windows

The ranch house's windows draw a fake room behind the glass (room, furniture, shadow and spotlight
textures, plus a reflection). openranch shows the room texture under a glossy pane tinted with the
glass colour; the full effect is still to do.
