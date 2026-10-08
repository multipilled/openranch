# Item prefabs and slime appearances

How the game's items (slimes, plorts, food) are stored in its data, as far as openranch reads them.
Checked by reading Slime Rancher 1.4's data files with openranch's readers.

## Finding an item's prefab

- The world scene has one `LookupDirector` component. Its `identifiablePrefabs` field points at a
  prefab list (a scriptable object with an `items` list of game objects). These are the prefabs the
  game spawns items from, about 300 of them, kept in `sharedassets2.assets`.
- Each prefab's root object carries an `Identifiable` component whose `id` is the item id (the
  `Identifiable.Id` enum, so a number that names `PINK_SLIME`, `CARROT_VEGGIE` and so on).
- Copies of the same prefabs placed in scenes, and slime "modules" in `resources.assets`, also carry
  `Identifiable` components, so the lookup list is the reliable way in.

## What a prefab holds

- The root object: a `Rigidbody` (mass, drag, angular drag, then flags), solid colliders (a sphere for
  slimes and plorts; a capsule and a sphere for a carrot), a `Vacuumable` component (`size`: 0 normal,
  1 large) and the item's behavior components, whose tuning openranch reads by name.
- Models: plain objects with a mesh filter and mesh renderer, sometimes on children. Some children
  are switched off in the prefab and turned on at runtime by the game (alternative looks); openranch
  leaves switched-off objects out. Lower levels of detail are listed by `LODGroup` components and are
  left out too.
- `Shadow` children are blob shadows drawn with Unity's default material; openranch skips renderers
  whose only material is Unity's `Default-Material`.
- Animated models use a skinned mesh renderer (Unity class 137). Its layout after the shared renderer
  fields: the static-batch root, probe anchor and light-probe volume references, sorting layer id,
  sorting layer and order (padded), quality, update-when-offscreen and skinned-motion-vector flags
  (padded), then the mesh reference, the bone list and the blend-shape weights. openranch draws
  skinned meshes in their bind pose at their own object's place for now.

## Slime appearances

A slime prefab has no body model of its own. Its `SlimeAppearanceApplicator` component names the
slime definition and lists the prefab's bones (`Bones`: a bone kind and the object standing for it)
and the appearance root object (`RootAppearanceObject`). The slime definition's
`AppearancesDefault[0]` is a `SlimeAppearance` scriptable object:

- `Structures`: the parts of the body. Each has `Element` (a `SlimeAppearanceElement` whose
  `Prefabs` list holds `SlimeAppearanceObject` components, one per level of detail), the
  `DefaultMaterials`, per-object `ElementMaterials` (`OverrideDefaults` and `Materials`),
  `SupportsFaces` and per-object `FaceRules` (`ShowEyes`, `ShowMouth`).
- Each `SlimeAppearanceObject` names `ParentBone` (0 for none: it hangs from the appearance root),
  `LODIndex` (0 is the most detailed) and, for skinned models, the bones it is bound to.
- `Face`: a `SlimeFace` with `ExpressionFaces`, each an expression number and the `Eyes` and `Mouth`
  materials. These are drawn as extra layers on the face-showing models. Expression 15 is the
  default, happy face.
- `ColorPalette`: the slime's top, middle and bottom colours.

The body model (`slime_default`) is about 1 m tall with its origin at the bottom, and hangs from the
appearance root, which sits half a metre below the prefab's centre, so the body is centred on the
slime's sphere collider. Its first UV set wraps around the body like a cylinder: u = 0.5 is the
front (Unity's +Z), u runs from 0 to 1 across the front half and beyond that range at the back, and v
runs from 0 at the bottom to 1 at the top.

The eyes and mouth materials sample a small face texture (`_FaceAtlas`, 64 × 64, uncompressed) with
those UVs, so the face covers the front half only. The texture holds shapes as soft masks rather
than colours: the red channel peaks inside the two eyes, the alpha channel dips along the mouth, and
the materials give the colours (`_EyeRed`, `_EyeGreen`, `_EyeBlue`; `_MouthTop`, `_MouthMid`,
`_MouthBot`) and the cut-off points (`_EyeSmoothStepBase`, `_MouthSmoothStepBase`). The texture's
rows run the same way as the body's v (bottom to top): sampled unflipped, the face shows two eyes above a smile
(seen in an openranch capture of a starter pink slime). openranch draws the layers opaque with a 0.5 alpha cut-off,
because its full-screen fog pass repaints over blended objects.

Body and plort materials have no texture; they colour the model with `_TopColor`, `_MiddleColor` and
`_BottomColor` from top to bottom. Food and chicken materials colour by ramps: a mask (`_Mask`, or the
vertex colours when `_VERTEXCOLORMASK_ON` is set) picks per channel between ramp textures
(`_RampRed`, `_RampGreen`, `_RampBlue`, `_RampBlack`), sampled by how lit the surface is.

Code: `src/OpenRanch.Formats/Game/ItemPrefabs.cs` reads a prefab into meshes, colliders and script
fields; `RanchSites.cs` finds the market's deposit hole, the corral insides and the vacpack's
suction zone in the world scene.
