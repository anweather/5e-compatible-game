# Trees as octahedral impostors: the trial (J-090)

Andrew asked for many realistic trees without the render cost, with detail dropping off with distance (J-090,
`docs/handoffs/briefs/tree-impostors-trial.md`). This is the trial on one species, built by us end to end: our own
baker, URP shader and renderer, nothing bought or imported. It is **off by default**; nothing a player sees changes
until we decide to roll it out.

## What was built

- **The baker** (Prototype > Bake Tree Impostor, `Editor/TreeImpostorBaker.cs`). It draws a species' LOD0 from
  12 × 12 directions over the upper hemisphere (hemi-octahedral, `TreeImpostorMath`), orthographic and unlit, with
  `Hidden/Prototype/TreeImpostorBake`, into two 2048 px atlases: albedo with coverage, and the tree-space normal with,
  in alpha, the depth from each view's plane. Each view is spread into the empty pixels round it so mips don't go
  dark at the edges, and the albedo keeps its coverage across mips. A material on `Prototype/TreeImpostor` goes with
  them. Everything is written under `Generated/Resources/TreeImpostors/`, which git ignores: the trees come from a
  pack, and the menu makes them again. Frames and atlas size are arguments (`TreeImpostorBaker.Bake(species, 12, 2048)`).
- **The shader** (`Shaders/TreeImpostor.shader` and `.hlsl`). One camera-facing quad a tree. Per pixel it picks the
  three views nearest the way the tree is seen, finds where the eye's ray crosses each view's plane, and blends the
  three. The pixel's depth moves onto the tree's surface, so the trunk meets the ground. It is lit by the main light
  (with a wrap and some light through the leaves) and the ambient, takes its shadows at 0.7, and is fogged. It casts
  its shadow from the light's own three views.
- **The renderer** (`Scripts/TreeImpostors.cs`, on with `-tree-impostors [near]`, `-tree-impostors-far`,
  `-tree-impostors-no-shadows`, or `TreeImpostors.On`). When it is on, the terrains draw no trees. Within `Near` (60 m)
  it draws each tree's full mesh, instanced: LOD0 within 25 m and LOD1 past that. From 60 to 500 m it draws impostors,
  one instanced call per chunk and species. The trees come from the view. Where a terrain is made, they are the
  terrain's own trees, so burnt and felled ones are already gone. Elsewhere it puts one tree in each tree cell, placed
  exactly as the ground places it (`GroundFromLand.Spot`, now shared) and standing on the map's land. It reads cells
  only on the main thread, through `World.ChunkCells`. A packed chunk is read from `World.Source` and never unpacked.
  The probe checks this: 0 extra unpacks over all 729 chunks of the nine.
- **The board's probe** (`tools/probe.sh treeimpostors --shots <dir>`, on the nine). It shoots a lone Conifer near
  the spawn. The other trees along the cameras' line are cleared for those shots and put back afterwards.

The trial species is the **Conifer**: 53,643 of the nine's 105,951 trees (the Cypress has the other 52,308). The
renderer draws whatever species are baked; the walk below ran with both baked, so every tree past 60 m is an
impostor.

## The board

Every picture is 1280 × 720 in the Editor, the same camera and moment for each pair, with the haze the valley has.
Lone-tree pairs crop the middle at the same pixel scale for every distance (full frame at 50 m, 2× at 120, 4× at 250,
8× at 500), so a pixel there is a pixel on screen.

| Picture | What it shows |
|---|---|
| [lone-morning-ground.png](tree-impostors/lone-morning-ground.png) | 8 am, from the ground, 50 / 120 / 250 / 500 m: today's tree and its impostor |
| [lone-morning-up.png](tree-impostors/lone-morning-up.png) | 8 am, 100 m up |
| [lone-noon-ground.png](tree-impostors/lone-noon-ground.png) | noon, from the ground |
| [lone-noon-up.png](tree-impostors/lone-noon-up.png) | noon, 100 m up |
| [circle-30m.png](tree-impostors/circle-30m.png) | circling the tree at 30 m: the mesh and the impostor every 45°, then the impostor 3° apart |
| [forest.png](tree-impostors/forest.png) | the forest as it would be: today against the trial (mesh within 60 m, impostors past it) |

What the board shows:

- **50 m:** the impostor is softer than the mesh (170 px a view for a 30 m tree) and a little yellower. The
  silhouette, the trunk and the branch gaps hold. You can tell them apart side by side; on its own it reads as the
  same tree.
- **120 m:** hard to tell apart, from the ground or from above. The colour is a shade warmer.
- **250 m:** the same. Today's tree has become SpeedTree's flat billboard here (past 160 m), and the impostor keeps
  its shape from above, where the billboard can't.
- **500 m:** today shows **no tree**: the terrain stops drawing trees short of it. The impostor still shows it,
  faint in the haze. Past the drawn tier (about 256 m from the wizard) today draws nothing at all, and the impostors
  keep the forest out to their `Far`.
- **Circling at 30 m:** the impostor turns with the tree at every angle and its rotation matches the mesh. There is no
  jump in the 3° steps: the three-view blend changes smoothly, so there is no pop as the camera moves round.
- **The forest:** from above it is all but the same. From the ground there is a seam at 60 m, where the full mesh
  meets the impostors: the impostors are about 10% brighter and yellower than the instanced mesh. That's tuning
  (`_Translucency` and `_Wrap` on the material, or matching SpeedTree's hue), not a limit of the method.

## The cost

The playtest bot's walk on the nine (`-map nine -perf-scenario bot -playtest -playtest-seed 1 -playtest-seconds 300`),
in a development player built from this branch, drawn offscreen at 1080p (`-perf-offscreen`), one run each,
back to back, on the Windows PC:

| | Mean ms | p95 ms | Draw calls (mean / peak) | SetPass | Triangles (mean / peak) | Shadow casters | GPU mem |
|---|---|---|---|---|---|---|---|
| Today (`-tree-impostors` off) | 9.63 | 15.22 | 3,294 / 14,172 | 276 | 17.8 M / 60.5 M | 379 | 554 MB |
| Trial (mesh < 60 m, impostors 60-500 m) | **8.63** | **14.58** | **973 / 3,175** | 246 | **10.9 M / 39.5 M** | 198 | 576 MB |

So the trial is 1.0 ms faster on the mean and 0.6 ms on the p95, with 70% fewer draw calls and 39% fewer triangles,
and it draws trees twice as far (500 m against today's ~256 m). The frame on this walk is bound by the main thread
(the GPU's numbers can't be read offscreen), so the saved draws matter more than the saved triangles. In the Editor,
from 80 m up over the valley's forest: 6.8 M triangles and 2,185 draws today against 2.5 M and 944.

- **Bake:** 1.8 s a species (12 × 12 views, 2048 px) in the Editor.
- **Atlas memory:** BC7 with mips is 5.6 MB an atlas, so 11.2 MB a species (albedo and normal). The walk's GPU memory
  rose 22 MB with both species. The Editor reports twice that, because it keeps a copy.
- **Reading the trees:** 0.19 ms a chunk made from cells, a few chunks a frame (all 729 of the nine in 137 ms).
  A terrain's trees are read again two chunks a frame, so a burnt tree drops out within about a second.

## Tiers

| Tier | Out to | What draws | Notes |
|---|---|---|---|
| Near | 60 m | the full mesh (LOD0 to 25 m, LOD1 past it) | where wind, burning and felling are seen up close |
| Mid | 500 m (or the known ring) | the 12 × 12 impostor at 2048 px | one quad a tree, shadows on |
| Far | the far ring | a small impostor: 8 × 8 views at 512 px (0.35 MB a species), no shadows | the same shader, a smaller atlas |
| Horizon | past that | forest colour (groves) in the far ring's land | no trees, a canopy tint and a raised edge |

## Wind

Impostors don't sway. The sample trees' wind moves a 30 m tree's crown by a few tenths of a metre. On a 1080p screen
that's about 5 px at 60 m, 2-3 px at 120 m, and under a pixel past 250 m. So a still impostor shows only in a strong
wind, inside about 120 m. A cheap fix if we want one: sway the quad's top by a world-space noise (one line in the
vertex shader, in step with SpeedTree's global wind).

**The near meshes lost their wind in this trial too.** SpeedTree's wind needs per-renderer data that an instanced draw
(`RenderMeshInstanced`) doesn't give, so the full meshes within 60 m stand still. This had to change because a
terrain's tree distance does nothing to these LOD-grouped trees except at 0 (tested: 60 and 900 drew the same), so
the terrain can't hand over at 60 m by itself.

## Rollout

To every species and the far tiers:

- **Every species:** bake each prefab (the menu has a "every species" item). That's 11.2 MB of atlases a species at
  2048 px and 2.8 MB at 1024. With 10 species: 112 MB at 2048, or 28 MB at 1024 with 2048 kept for the 2-3 commonest.
- **Generated at build time:** the atlases come from pack trees, so they're baked on each machine (like the repaired
  trees) and go into the player through Resources. A build step would run the baker when they're missing.
- **The pinned valley's far chunks:** where no terrain is made yet, a tree goes in each tree cell by the ground's hash.
  The valley's own pack placed its trees its own way, so as a pinned valley chunk's terrain is made, its trees move by
  under a cell and can change species. This is invisible at 250 m or more, but the far trees should come from the
  pack's tree list there.
- **Packed chunks with changes:** a packed chunk is read from its source, as it started. A forest burnt and then packed
  before the impostors ever read it unpacked would show its trees again from far off. This doesn't happen near the
  wizard; the fix is to read the packed changes (`PackedChunk`) as well.
- **Far tier:** the same renderer with a second, smaller material past `Far`, and no shadows.

## Decisions for Andrew

1. **Wind:** accept still impostors past 60 m, or add the quad's sway (cheap)? And for the full mesh within 60 m:
   draw it as GameObjects with a LOD group (wind kept, more draws) or keep it instanced without wind?
2. **Shadows:** impostors cast and receive (at 0.7) in these numbers. Keep them, or cast only within the drawn tier?
3. **How far out the full mesh is needed:** the board says 60 m is about right with 12 × 12 at 2048 px. Below that the
   impostor is soft. An 8 × 8 bake (256 px a view) would allow 40 m.
