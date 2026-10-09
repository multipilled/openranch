# Zones: the areas of the Far, Far Range

How the world scene is split into zones and cells, which ambience each part of the world shows, and
where the player arrives in each zone. Read from the world scene (`level3`) and its scripts' data, with
the rules from static analysis of the ambience director, the region scripts and the teleport network.
openranch builds one zone at a time with `--zone NAME`; joining them is the next step (see the end).

## Zones and cells

Every zone is a root object of the world scene named `zone...`, holding one `ZoneDirector` (its zone id)
and a number of cells (`cell...`). Each cell carries a `CellDirector` and a `Region`:

- The region's `bounds` is a box in world space (centre and half size). The player is in every cell whose
  box holds its position, edges included; boxes overlap where cells meet.
- The cell director's `ambianceZone` names the ambience settings the cell asks for (AmbianceDirector zones:
  0 DEFAULT, 1 QUARRY, 2 MOSS, 3 DESERT, 4 RUINS, 5 WILDS, 6 OGDEN_RANCH, 7 VALLEY, 8 MOCHI_RANCH,
  9 SLIMULATIONS, 10 VIKTOR_LAB, 1000 AUX1, ...). This is a different list from the zone ids of
  `ZoneDirector` (where 1 is the Dry Reef): the Dry Reef and the Slime Sea have no ambience of their own
  and use DEFAULT, like The Ranch.

The world scene's zone roots (objects counted with inactive ones; "cells" are cell directors):

| Root | Area | Objects (active) | Cells | Cells' ambience |
|---|---|---|---|---|
| zoneRANCH | The Ranch | 7,385 (7,055) | 10 | all 0 |
| zoneREEF | Dry Reef | 10,589 (10,251) | 16 | all 0 |
| zoneQUARRY | Indigo Quarry | 8,415 (8,129) | 15 | 1, except the Crystal Volcano cell, 1000 (AUX1) |
| zoneMOSS | Moss Blanket | 11,450 (11,238) | 14 | 2, except Hidden Isle, 0 |
| zoneRUINSTransition | Ruins transition (Garden of Tranquility and the passage from the Reef) | 1,761 (1,698) | 2 | garden 4, passage 0 |
| zoneRUINS | Ancient Ruins | 10,852 (10,587) | 16 | all 4 |
| zoneDESERT | Glass Desert (floating about 1,000 m up) | 17,076 (15,531) | 25 | all 3 |
| zoneWILDS | The Wilds | 8,116 (8,047) | 11 | all 5 |
| zoneSEA | Slime Sea islands and Hobson vaults | 5,022 (4,968) | 8 | all 0 |
| zoneVALLEY | Nimble Valley race tracks | 5,809 (5,617) | 2 | all 7 |
| zoneOGDEN | Ogden's retreat | 2,743 (2,712) | 1 | 6 |
| zoneMOCHI | Mochi's manor | 2,505 (2,475) | 1 | 8 |
| zoneVIKTOR | Viktor's workshop | 1,845 (1,812) | 1 | 10 |
| zoneSLIMULATIONS | the Slimeulations | 12,693 (12,598) | 21 (`GlitchCellDirector`, a cell director with tarr settings added) | all 9 |

The smaller secret areas of the main game (the Grotto, the Overgrowth, the Docks, the Lab) are cells of
zoneRANCH, not roots of their own.

## Which ambience shows

Outside caves, the ambience is that of the highest-numbered zone named by any cell the player is in, or
DEFAULT where no cell holds the player. Inside a cave trigger the cave's zone wins (see day-and-night.md).
When the zone changes, every setting (day and night fog colour and density, ambient, sky and horizon
colours) moves in a straight line from what showed at that moment to the new zone's values over the
ambience director's `zoneSettingTransitionTime` (1.25 s in the install). Checked: rules from static analysis
of the ambience director; zone of each cell read from the scene and cross-checked against the generic
script reader (`ZoneTests`).

## Where the player arrives

A teleporter destination (`TeleportDestination`) puts the player on the destination object's own position
and, when its `reorient` flag is set, turns the player to the object's rotation. Each destination has a name
(`teleportDestinationName`): the zones' own teleporters have names such as `ReefBigTree`, `QuarryCaveHub`,
`RuinsTempleExitTeleporter`, `Wilds`; every echo note gordo's teleporter is called `echoNoteGordo_source`.
Some zones also hold a `DebugTeleportDestination`, a named start point for the developers' debug menu:
"Glass Desert Start", "Wilds Start" and "Slimeulation Start".

openranch starts `--zone NAME` at the zone's debug start point if it has one, otherwise at its first
teleporter destination in scene order that isn't an echo note gordo's, otherwise at any destination
(`game/scripts/World/ZoneSpawn.cs`); `--spawn NAME` picks a destination by name. The Ranch keeps the
player rig's own place in the scene. In a new game the Moss Blanket's Hobson vault teleporter is switched off
(world-visibility.md), so the Moss Blanket starts at the echo note gordo teleporter in its entrance cell; the
Ruins transition has no other destination either.

## Water and kill volumes

Water (`LiquidSource`) is a trigger: it floats bodies inside it and draws as a surface, but it is not
ground; the ground under it is. Seas end in a `KillOnTrigger` box (the Slime Sea's and the Glass Desert's
sand sea, `seaKillCube`) that kills what falls in. Triggers only meet bodies on layers that collide with
theirs: the Ranch's `NonPlayerKillVolume` boxes are on a layer the player's doesn't meet.

## The one terrain

The world is built from meshes apart from one Unity terrain, `terCellReef02_01` in the Dry Reef's Hub cell
(base at y = -24.7): a 129 x 129 heightmap 45 x 45 m across and 50 m tall, mostly the floor under the edge of
the water there. Its terrain data names no terrain layers and no splat textures, and its `TerrainCollider` is
stored as a trigger and switched off, so in the original it is something to see, not ground. openranch
builds no terrain yet; walking is unaffected. Read from the scene's Terrain and TerrainCollider components
and the TerrainData object (sharedassets3), whose fields after the heights parse to the object's exact end.

## In openranch

Per zone, from `--zone X --collision-check` (Godot 4.7.2, this PC, new game at noon): reading the scene
part, building the Godot nodes, what was built, the start point and the walk check (rays straight down on a
4 m grid over the cells' boxes, then 40 drops of the player onto walkable ground, which must end standing
within 1.5 m of where they were aimed). Ground inside a kill volume the player meets is not dropped onto.

| Root | Read / build, ms (check run) | Read / build, ms (capture run) | Meshes | Multimeshes | Instances | Materials | Textures | Collision shapes | Starts at | Ambience zone at the start | Area checked, m | Drops landed |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| zoneRANCH | 2,555 / 1,989 | n/a | 496 | 487 | 2,950 | 96 | 153 | 1,525 | the player rig's place | 0 | 537 x 389 | 40/40 |
| zoneREEF | 1,878 / 3,620 | 1,396 / 2,054 | 980 | 887 | 5,845 | 95 | 154 | 3,029 | ReefBigTree | 0 | 983 x 591 | 40/40 |
| zoneQUARRY | 2,359 / 4,563 | 1,315 / 1,693 | 767 | 636 | 4,109 | 62 | 104 | 2,300 | QuarryCaveHub (in the cave hub) | 1 | 584 x 850 | 40/40 |
| zoneMOSS | 2,426 / 3,272 | 1,644 / 1,688 | 909 | 1,146 | 6,961 | 68 | 106 | 3,460 | echoNoteGordo_source (Moss entrance) | 2 | 521 x 631 | 40/40 |
| zoneRUINSTransition | 1,004 / 2,693 | 925 / 1,033 | 138 | 84 | 497 | 68 | 99 | 435 | echoNoteGordo_source (garden) | 4 | 187 x 215 | 40/40 |
| zoneRUINS | 18,657 / 3,378 | 1,479 / 1,459 | 515 | 467 | 2,549 | 65 | 108 | 2,306 | RuinsTempleExitTeleporter | 4 | 286 x 612 | 40/40 |
| zoneDESERT | 3,395 / 5,716 | 1,667 / 1,846 | 1,495 | 968 | 6,089 | 83 | 126 | 3,592 | Glass Desert Start | 3 | 514 x 1,208 | 40/40 |
| zoneWILDS | 3,232 / 7,204 | 1,345 / 1,693 | 628 | 587 | 3,733 | 70 | 133 | 1,490 | Wilds Start | 5 | 363 x 442 | 40/40 |
| zoneSEA | 4,039 / 2,217 | 1,109 / 1,142 | 320 | 326 | 1,896 | 63 | 96 | 1,409 | SeaMiniIsland | 0 | 1,985 x 422 | 40/40 |
| zoneVALLEY | 2,512 / 5,781 | 1,192 / 856 | 620 | 438 | 2,023 | 61 | 95 | 1,518 | ValleyTrackEasy | 7 | 749 x 325 | 40/40 |
| zoneOGDEN | 6,740 / 2,830 | 989 / 1,430 | 190 | 242 | 1,397 | 90 | 159 | 733 | OgdensOutpost | 6 | 245 x 257 | 40/40 |
| zoneMOCHI | 1,678 / 2,385 | 994 / 962 | 190 | 212 | 1,199 | 75 | 113 | 930 | MochiEstate | 8 | 258 x 373 | 40/40 |
| zoneVIKTOR | 1,347 / 1,217 | 974 / 1,257 | 199 | 240 | 1,114 | 78 | 125 | 644 | ViktorLabReceiver | 10 | 248 x 175 | 40/40 |
| zoneSLIMULATIONS | 2,256 / 4,048 | 1,709 / 2,185 | 1,058 | 1,320 | 7,806 | 125 | 164 | 3,762 | Slimeulation Start | 9 | 607 x 580 | 40/40 |

Read times vary with what else the PC is doing and whether the files are cached (the Ruins once took
18.7 s to read and 1.5 s on another run). No drop missed in any zone, so there are no misses to explain.

Captures at noon from each start point (kept outside the repo) show each zone as itself: the Dry
Reef's red rock, coral grass and sea; the Indigo Quarry's dark blue cave hub (and, from `QuarryMirrorIsland`,
purple rock with indigo fog); the Moss Blanket's mossy trees and green fog; the Ruins' carved stone; the
Glass Desert's temple and, from the Silent Sentinels, its canyons and dunes; the Wilds' red earth and cliffs.
The Slimeulations' glitch walls draw as flat white: their material kind isn't handled yet.

## Joining the zones

All zones share one coordinate space (the scene places them where they meet), so building several roots
into one world needs no offsets. What the original does on top: each zone belongs to a region set (the Far,
Far Range, the Slimeulations, Viktor's lab...), and only the current set's regions are live; cells load and
unload by distance through their regions (`RegionLoader`).
