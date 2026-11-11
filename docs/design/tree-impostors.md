# Trees as octahedral impostors (J-090, J-091)

Andrew asked for many realistic trees without the render cost, with detail dropping off with distance (J-090,
`docs/handoffs/briefs/tree-impostors-trial.md`). We built it end to end: our own baker, URP shader and renderer,
nothing bought or imported. The trial (one species, off by default) is below the rollout; after it Andrew chose near
trees as SpeedTree GameObjects with their wind and impostor shadows only within the drawn tier (J-091), and the
rollout (`docs/handoffs/briefs/tree-impostors-rollout.md`) turned it on for every species.

## The rollout: on by default, every species (J-091)

- **On by default.** `-no-tree-impostors` (or `TreeImpostors.On = false`) gives the terrains their trees back.
- **Every species** the maps use (the template's: Conifer and Cypress; the valley and the generated land share them)
  is baked by the baker when its impostor is missing or was baked by an older baker (`TreeImpostorBaker.BakeVersion`
  on each material): when the Editor starts, after the yard builds itself, and before a player build. The atlases stay
  under `Generated/` (ignored); the menu item is Prototype > Bake Tree Impostors (every species, 12 x 12).
- **Near trees (within 150 m since #220; 60 m before, where the swap showed in a sparse wood) are their species' prefab**,
  a GameObject with its LOD group and SpeedTree's wind, from a pool: a tree that leaves Near (plus 8 m, so one on the edge
  doesn't swap each step) gives its GameObject to one that
  enters, nearest first; new ones are made at about 1 ms a frame, and a tree waiting for one is drawn as its impostor
  meanwhile. Their colliders are taken off (the terrain's tree colliders stand there). The instanced near meshes are
  gone. On the nine's spawn about 30-50 trees stand as prefabs.
- **The wind.** SpeedTree's wind comes from a WindZone, and the Wind module had been dropped as unused
  (`editor-lean`), so no tree ever swayed, the terrain's included. It is back on (`com.unity.modules.wind`) and
  `TreeWind` makes a WindZone that follows the weather: a breeze when still, harder in a windy stretch or a storm,
  blowing the way the world's wind does (Gust of Wind's while it blows), eased over 6 s.
- **Shadows:** impostors cast within `ShadowFar` (256 m, the drawn tier) and none past it; they still receive.
- **The 60 m seam.** The impostors were brighter and yellower than the mesh for two reasons: SpeedTree keeps its baked
  occlusion in the vertex colour (Conifer 0.67, Cypress 0.82 on average) and darkens the albedo by it, which the bake
  didn't; and what's left, measured on the board's pairs (the pixels that differ between a pair at 50 and 120 m, from
  the ground and above, at 8 and at noon), is taken out by a tint a species (`TreeImpostorBaker.Tints`: Conifer
  0.94/0.945/1.0, Cypress 0.965/0.966/1.0). After it the lone pairs differ by 2-3.5% (Conifer +2.5%, Cypress -2% and
  3.5% less blue), and the forest view at the join by 0.5% (it was about 10%). The prefabs within 60 m are 1-3% brighter
  than the terrain drew the same trees.
- **#213, the far trees as they are now.** A packed chunk's cells are its source's with its changes laid over: the
  changes are read from the chunk's save record (`World.SaveChunkOf`, which reads a packed chunk where it lies), so a
  tree felled or burnt before its chunk packed doesn't show far off, and nothing is unpacked (the probe fells a tree
  ~500 m out, packs its chunk, and checks it's gone with 0 unpacks). The pinned valley's chunks without a terrain take
  their trees from the valley's own ground (`GroundBuilder.Pinned`, from `PinnedGround.LayLazily`), but those whose
  cell was a tree as the world started and isn't now; the probe checks every terrain tree of the valley's 81 chunks
  (3,150) is where the far read put it.

### The board

From `tools/probe.sh treeimpostors --shots <dir>` on the nine (1280 x 720 in the Editor, the valley's haze). Lone-tree
pairs crop the middle at the same pixel scale for every distance (x1 at 50 m, x2 at 120, x4 at 250, x8 at 500).

| Picture | What it shows |
|---|---|
| [lone-morning-ground.png](tree-impostors/rollout/lone-morning-ground.png) | 8 am from the ground, both species: off (SpeedTree on the terrain) against the impostor |
| [lone-morning-up.png](tree-impostors/rollout/lone-morning-up.png) | 8 am, 100 m up |
| [lone-noon-ground.png](tree-impostors/rollout/lone-noon-ground.png) | noon, from the ground |
| [lone-noon-up.png](tree-impostors/rollout/lone-noon-up.png) | noon, 100 m up |
| [forest-ground.png](tree-impostors/rollout/forest-ground.png) | the forest from the ground, 120 m out, morning and noon: off against on |
| [forest-up.png](tree-impostors/rollout/forest-up.png) | the forest from 100 m up |
| [seam-60m.png](tree-impostors/rollout/seam-60m.png) | the 60 m join at 1:1, off against on |
| [circle-30m.png](tree-impostors/rollout/circle-30m.png) | circling a Conifer at 30 m: its prefab, then the impostor |
| [wind-30m.png](tree-impostors/rollout/wind-30m.png) | the prefab at 30 m in a windy stretch, a still every quarter second (about 30% of the crown's pixels change from one to the next) |

### The cost

A development player built from this branch (727917f), drawn offscreen at 1080p (`-perf-offscreen`), one run each,
back to back on the Windows PC with the Editor open; off is the same build with `-no-tree-impostors` (so its terrain
trees have the new wind too). Walks are the playtest bot (`-perf-scenario bot -playtest -playtest-seed 1
-playtest-seconds 120`, ~40 s measured), standing is `-perf-scenario stand -perf-seconds 30`.

| | Off: mean / p95 ms | On: mean / p95 ms | Draw calls (mean) | Triangles (mean) |
|---|---|---|---|---|
| The nine, walk | 9.56 / 15.09 | **8.69 / 14.49** | 3,255 -> 1,200 | 18.0 M -> 11.4 M |
| The nine, standing | 7.33 / 9.11 | **5.45 / 6.42** | 4,860 -> 799 | 17.4 M -> 6.1 M |
| The valley, walk | 7.68 / 13.01 | **7.54 / 12.54** | 1,241 -> 873 | 12.9 M -> 11.1 M |

GPU memory on the nine's walk: 550 -> 572 MB (the two species' atlases). The trees draw to 500 m against the
terrain's ~256 m.

## The trial: what was built

- **The baker** (`Editor/TreeImpostorBaker.cs`). It draws a species' LOD0 from
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

## The trial's board

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

## The trial's cost

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

## The trial's rollout plan

What the trial proposed; all but the far tier is done in the rollout above:

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

## The trial's decisions for Andrew (answered in J-091: near trees as GameObjects with their wind, impostor shadows within the drawn tier; 60 m kept)

1. **Wind:** accept still impostors past 60 m, or add the quad's sway (cheap)? And for the full mesh within 60 m:
   draw it as GameObjects with a LOD group (wind kept, more draws) or keep it instanced without wind?
2. **Shadows:** impostors cast and receive (at 0.7) in these numbers. Keep them, or cast only within the drawn tier?
3. **How far out the full mesh is needed:** the board says 60 m is about right with 12 × 12 at 2048 px. Below that the
   impostor is soft. An 8 × 8 bake (256 px a view) would allow 40 m.
