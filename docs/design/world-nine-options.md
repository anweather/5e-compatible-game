# The bigger world: nine valleys on Punchy's biome logic (playtest 2026-11-08, W1-W4)

For Andrew. Read against dev at `78e97241` and Punchy's local clone at `b183507`. Nothing here is built.

## Today

- **Size.** The valley is 576 × 576 cells (`world/maps/valley/map.json`): 9 × 9 chunks of 64, at 1.5 m a cell, so about
  864 m square. The size is fixed in code in two places:
  - `MapCheck.Size` = 576 (other sizes are refused by name);
  - `TerrainGrid.Valley()` (9 × 9).
- **Cells in memory.** Every cell is in memory all the time (`World` "cells everywhere"), and `Cell` is a class with about
  20 fields, two of them strings.
  - Rough estimate: 50-70 MB for the valley, so about 0.5 GB for nine, plus the work for the garbage collector.
- **Active chunks.** A chunk is active within `reach` 2 of a character and stays so until no one is within `keep` 3
  (`rules/sim.json` `streaming`). Online, the host stamps that plan into its ticks.
  - Every peer runs every active chunk, and a late joiner replays from tick 0.
- **Maps as data** (`docs/design/maps-as-data.md`):
  - Phases 1-5 landed: the folder, the identity hash, live land stamps, the `ground/` export, and `GroundBuilder` behind
    its switch.
  - Phases 6-9 wait: the scene lets go, sea and life from `land`, a second map, keeping.
  - Start-up is 7.4 s on the PC. The land takes about 2 s to work out (Mac); nine valleys could make that about 18 s if
    it all stays up front.
- **Land around the valley.** The theme engine lays out the land, ported into the core with exact maths (D11,
  `ThemeBlueprint`, `WorldNoise`). We already have **a piece of Punchy's biome code**:
  - `ThirdParty/Punchy/Terrain/Editor/BiomeTerrain.cs` (330 lines, float, used only in the Editor's bake through
    `ValleySurroundings`) paints the four looks in `world/looks.json`.
- **Sea and far distances.** The sea is a half-plane (`Ocean.cs`). Mountains don't stop raiders or sight (J-073). Nine
  valleys is about 2.6 km across, under the 5-10 km where `WORLD_STREAMING.md` says we'd need a floating origin.

## Punchy's biome logic (`C:\Users\Andrew\Development\Punchy\Assets\Game\Scripts\Terrain\`)

- **Biomes:** `BiomeSampling` (`Biomes/BiomeData.cs`, 584 lines, static, pure maths).
  - The world is cut into domain-warped Voronoi regions: cells of 1400 m, warped 350 m.
  - Each region reads four climate channels at its centre: altitude, temperature (falling with altitude), humidity, and
    volcanism (hotspots).
  - Each biome scores Frequency × exp(-misfit²), where misfit is how far the climate lies outside the biome's ranges. A
    seeded weighted roll then picks one.
  - Blending keeps the top 4 biomes per point: 120 m wide for the surface, 450 m for the shape.
  - The data is `BiomeDefinition` ScriptableObjects, 13 of them: Temperate, Grassland, Mountains, Dunes, Sonoran,
    SaltFlat, Tundra, FoggyMarsh, PoisonSwamp, SparklingForest, Ashlands, Volcano, Ocean.
  - It doesn't use distance to water or latitude.
- **Height** comes after the biome. Each biome blends its own BaseElevation plus a relief shape (Hills fBm, Ridged, or
  wind-aligned Dunes), with sharpness and terraces. Rivers are carved in after (`TerrainSource.SampleHeight`).
- **Rivers:** `Rivers/RiverBaking.cs` (624) and `RiverData.cs` (243). Rivers are a drainage model on a 16 m grid; no A*
  and no erosion. The steps:
  1. Rain from each biome's Rainfall, more on high ground.
  2. A priority flood from the sea gives each point its downhill neighbour.
  3. Flow is summed down that tree, and a river's width (4-34 m) comes from its catchment.
  4. Filled hollows become lakes.
  5. The bed only ever runs downhill, cutting gorges through rims.
  6. The channel is carved as a parabola that fades out over 110 m.
- **Flora:** `Flora/FloraSystem.PlaceJob`, about 180 lines of rules. Each species carries its biome densities, max slope,
  height band (a tree line), near-water needs and patches. Placement is a jittered grid per layer (0.7 m, 2 m and 4.5 m)
  with one seeded roll per spot, so it's a pure function of position. The meshes and drawing (`FloraGenerator`, 1501
  lines, compute culling) are tied to Unity.

| Part | What to do | Why |
|---|---|---|
| `BiomeSampling` climate, regions and pick | **Port to the core** | Pure maths, but floats and `Unity.Mathematics` simplex aren't the same on every machine. Rewrite on our `WorldNoise` and `WorldHash`, in fixed point |
| The biome rows (13 `BiomeDefinition`s) | **Vendor the numbers** as rows | Export once to JSON, as `fauna.json` was |
| Height shapes (Hills, Ridged, Dunes) | **Port to the core** | About 150 lines; their output is `land` integers |
| River drainage | **Port to the core** | Arrays, a heap and loops; Burst is only the wrapper. Its output is `land` plus `river` and lake cells |
| Flora placement rules | **Port** into the generator that places things and trees | Per cell, seeded |
| Flora meshes, water shader, terrain streaming, caves | **Leave out** | Unity- and GPU-bound; we have our own trees, water and `GroundBuilder` |
| Our `BiomeTerrain.cs` | **Retire** once the core port paints the looks | Two copies would disagree |

The scale has to change. At Punchy's 1400 m regions, 2.6 km would hold about four biomes, so a region should be about one
valley square (about 860 m).

## His ask

- Keep the valley at the bottom left by the ocean, and add squares the valley's size, three across and three up, joined.
- Intentional biomes, made from the world's properties so the land feels organic.
- A better river, and one more flowing into the lake from the other side.
- A flat, open desert for the Hold.
- Make sure the game works on the bigger area.

## Phased plan (backbone: Punchy's biome logic in the core)

| # | Phase | What it delivers | Size | Needs |
|---|---|---|---|---|
| 0 | **Rivers in the valley now** | The thin strip by the castle becomes a real river (also B4, where the water slows you too much). A second river comes into the lake from the far side, laid with map stamps (`stamps.json` canyon path) and river cells | S | Nothing |
| 1 | **Size from `map.json`** | `MapCheck` and `TerrainGrid` read `size`; a flat, empty 1728² test map measures memory, start-up, a two-peer run and the ground built on demand round the camera | M | Maps phases 6 (the scene lets go) and 7 (the sea from `land`) |
| 2 | **Biome field in the core** | `Chem.BiomeField`: warped regions, four climate channels, the scored pick, top-4 weights, in integers. `world/biomes.json` holds the 13 Punchy rows. A golden on the PC and the Mac | M | 1 for scale only; can start now |
| 3 | **Land from biomes** | The relief shapes write `land`, with the hand-made valley pinned and blended over 90 m as today | M | 2 |
| 4 | **Rivers and lakes** | Drainage on a 12 m grid (8 cells) carves `land` and lays `river` and lake cells into `places.json` | M-L | 3 |
| 5 | **Flora and things per biome** | The placement rules lay trees, rocks and props, with stable ids from chunk and spot | M | 3; can run beside 4 |
| 6 | **Desert Hold** | A SaltFlat or Sonoran square holds a second Hold site (the map's `hold.json`): open routes, desert raiders (`fauna.json` already has camel, fennec fox, gila monster, rattlesnake) | M | 3, and C3's second path for the routes |
| 7 | **The view paints biomes** | Terrain layers and looks from the core's weights; `BiomeTerrain.cs` and the Editor bake retire | M | 3 |

**What the map folder needs:**

- `map.json` gains:
  - `size` (any multiple of 64);
  - `generate`, holding the seed, the climate scales, the biome set, and `pinned` (the valley's chunks, kept as made);
  - an optional `layout` (see decision 2).
- The generator's output lands in the folder: `land.json`, `cells.txt`, `places.json` and `things.json` for the new
  chunks. `MapCheck` holds them together.
- Things ids are chunk plus index, so the valley must keep its chunk coordinates. Grow the world north and east of
  chunk (0, 0), or ids and saves shift once, as they did in October's (3, 4) move.

**W4, Punchy's creatures.**

- Punchy has no models. Its creatures are about 58 species and 75 limb assets (`Assets/Game/Fauna/**/*.asset`, YAML that
  points at other assets by GUID), drawn as procedural capsules.
- Ours are already rows: `rules/fauna.json` (68 species) and `rules/parts.json`. They were exported once from Punchy at
  `c52d9ff`, and the exporter was deleted (`docs/handoffs/mac-2026-11-02.md`).
- **The script** (`tools/punchy-sync`, a small .NET tool, size S):
  1. `git fetch` Punchy, since the local clone is at `b183507`, older than what we ported.
  2. Read the species and limb `.asset` YAML, resolving GUIDs through their `.meta` files.
  3. Write candidate rows, and print a diff against our files: new species, changed numbers.
  4. Never overwrite our own rows (lily pads, reeds, raider sizes).
- Code changes upstream (`CreatureAnimator`, `CreatureBrain`) stay hand-ported, listed in
  `ThirdParty/Punchy/README.md`.
- Credit is already in `Credits.cs`.
- Elsewhere from Punchy: the sky, clouds, thunder synth, precipitation, fireflies and butterfly shaders.

## Decisions for Andrew

1. **Generate at start-up, or bake into the map folder?**
   - (a) Each peer generates at start-up: lockstep-safe in integers, but it costs start-up.
   - (b) A tool runs the generator in the core and commits the output to `world/maps/<id>/`; start-up just reads the
     files.
   - **Recommend (b)** now. It's also the generator that Part 3's chunks-on-demand would later run.
2. **How intentional?**
   - (a) Pure climate, like Punchy: organic, but the seed picks.
   - (b) A 3 × 3 layout he names per square.
   - (c) Both: he names an anchor biome per square (valley SW, desert for the Hold, mountains N...) and the climate draws
     the borders and the variety inside.
   - **Recommend (c).**
3. **Which biomes?**
   - (a) All 13.
   - (b) A curated 7: Temperate (the valley), Grassland, Mountains, Tundra, SaltFlat/Sonoran (the Hold), FoggyMarsh,
     Ashlands/Volcano as the far, late square.
   - **Recommend (b);** the rows for the rest stay in the file, off.

## Risks

- **Start-up:** nine times the land. Bake (decision 1) and build the ground on demand, or the 7.4 s start-up roughly
  doubles. Phase 1 measures it before any biome work.
- **Memory on the 32 GB PC:**
  - About 0.5 GB of cells, plus terrains, beside the Editor and one more Editor.
  - If phase 1 shows too much, make `Cell` arrays (D9), or drop untouched far chunks and reload them from the folder.
- **Lockstep:** the port must be integer and seeded. Punchy's float simplex would differ between the PC and the Mac. A
  golden on both catches it. The bigger map's hash and approval already work per folder.
- **Phases in flight:**
  - Maps phases 6-7 must land first and are the critical path.
  - Phase 0 touches the valley's `stamps.json` and `places.json`, so it needs one owner with `castle-cliff-canyon` and
    `hold-east-path` (C3).
- **Raiders and mountains:** raiders still walk over mountains (J-073). In a bigger world that shows more.
