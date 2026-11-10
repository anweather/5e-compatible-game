Needs: unity
Area: unity/Assets/Prototype/Scripts/Editor/ (an impostor baker), unity/Assets/Prototype/Scripts/ (an impostor renderer, off by default), unity/Assets/Prototype/Shaders/ (a URP impostor shader), Generated/ for baked atlases (ignored), docs/design/tree-impostors.md (new), docs/GLOSSARY.md (new entries only)
Branch: tree-impostors-trial

# Trees as octahedral impostors: a trial on one species (Andrew, 2026-11-10, J-090)

Andrew wants many realistic trees without the render cost, with detail dropping off with distance. Today: Unity
terrain trees (SpeedTree 8 samples, URP's SpeedTree8 shader), full meshes to 160 m, then SpeedTree's flat billboards
to 900 m (`MapTerrains.Configure`, MapTerrains.cs:472-473); since #205 terrain draws only to the drawn tier (~256 m),
so trees past it aren't drawn at all. On the nine's walk the trees are the biggest render cost left: 4,300 draws and
13 M triangles (#201). Terrain-instanced trees miss the GPU Resident Drawer.

**Octahedral impostors** (Ryan Brucks, 2018; shaderbits.com "Octahedral Impostors"): bake a tree once from many
directions over a hemi-octahedron (say 12 x 12 views) into an atlas of albedo+alpha, normals and depth; at run time
one quad picks the three nearest views and blends them, lit by the sun from the normals, with the depth for a correct
silhouette against the ground. One quad and one material a species: thousands of trees in a few instanced draws.

## The trial (paper first: stills before rollout)

1. **A baker** (Editor menu, Prototype > Bake Tree Impostor): renders one species' tree prefab from N directions over
   a hemisphere into render textures, packs albedo+alpha, world normals and depth into atlases under `Generated/`
   (ignored; rebuilt by the menu, never committed). N and atlas size are settings (try 8 x 8 and 12 x 12, 2048 px).
2. **A URP shader** for the impostor quad: the three-frame blend, normal-mapped lighting from the main light and the
   ambient, alpha clip, depth offset; shadows cast from the impostor if cheap.
3. **A renderer** (off by default, a dev flag or menu): draws the species' trees past a distance as impostors with
   `Graphics.RenderMeshInstanced` / `RenderMeshIndirect` from the trees the world has (the cells' trees in the drawn
   tier; for the trial, the nine's start trees), and the full mesh nearer. Trees are cells in the world; this is
   view-only and changes no shared state.
4. **A stills board** (`docs/design/tree-impostors.md`, images under 1280 px committed under
   `docs/design/tree-impostors/`): the same spot and time of day, today's tree against the impostor at 50, 120, 250 and
   500 m, from the ground and from ~100 m up, morning and noon light; and circling one tree at 30 m to show no pop.
5. **The cost:** on the nine, the bot walk (seed 1, 300 s) in a development player with the impostors on past 60 m
   against off: mean, p95, draws, SetPass, triangles. And bake time, atlas memory.
6. The design note: what each tier would be (full mesh near, impostor mid, a small atlas far, groves or forest colour
   at the horizon), wind (impostors don't sway: how far it shows), and what a rollout to every species and the far tiers
   would take, with sizes.

## Done

The stills board and the numbers; EditMode passes; smoke passes on the valley and the nine with the impostors off
(the default); the branch pushed; the lead sends Andrew the board before any rollout.

Don't buy or import an Asset Store pack for this; don't edit pack folders; don't kill the Editor or windows that
aren't provably yours; don't hand-edit `.unity`/`.prefab` YAML; don't touch `unity/Assets/_Recovery/`.
