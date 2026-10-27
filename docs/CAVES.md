# Caves: how to represent the world below

Backlog #14 (§3, "Layered terrain"). A design spike, 2026-10-27, World lane.
Nothing here is built; the spike's prototype is on branch `caves-spike`
(`CavesSpikeTests`), never merged. Decide the representation first: it is
the biggest change the core has had.

**The goal scene:** an underground lake in a cave, reached three ways: through
a maze of tunnels, by digging down to it, or through a metal gate opened with
a key, a lever, Knock, or by melting or shocking the lock.

## What we have

- **2.5D cells.** One `Cell` per 1.5 m column: terrain, water (units), ice,
  cover, fire and fuel, gas, charge, `Height` (whole 0.5 m steps of earth
  raised or dug, `sim.height` −6 to +8, a wall from +3) and life. Water
  flows on `ElevationUnits` (terrain elevation + height, in water units):
  a heightfield, so there is exactly one floor per column.
- **Chunks** of 64 × 64 cells, active or inactive by each character's
  reach, with catch-up and still-water skipping (`docs/WORLD_STREAMING.md`).
- **Determinism and goldens.** `World.CellHash` (FNV-1a over the cells, in
  order) is pinned against the TS sandbox in shared goldens; the sandbox's
  `Cell` mirrors ours field for field.
- **Saves** (`World.Save`, branch `stream-disk`): per chunk, the cells that
  differ from the chunk source, keyed by local index.
- **The view.** A Unity Terrain per chunk for the ground, `CellWaterMesh`
  for water and ice (with colliders), meshes for walls. Unity Terrain can't
  do overhangs, **but it can cut holes** (`TerrainData.SetHoles`), so a
  shaft or a cave mouth can open the ground over a cave drawn as meshes.

## Three ways to have a below

### A. A stack of cells per column (a few levels)

Each column holds its surface cell and, where there is one, up to a few cells
below it, each a floor with a ceiling (spans, as in Dwarf Fortress' sparse z
or a heightfield with layers). Neighbours are the cells in the next column
whose spans overlap, so a tunnel can slope from one depth to another.

- **Sim cost:** nothing where there are no caves, but every system that looks
  at a neighbour (fire spread, gas, water flow, charge, raiders' paths, the
  terraform reach) has to find the right one among a column's spans. That is
  an extra lookup on every neighbour of every surface cell too, about 10 to
  20 % on the hot loops, and a rewrite of each of them.
- **Lockstep and hashing:** fine if spans are kept in a fixed order. Surface
  cells could hash as today, with spans appended only where they exist (as
  `Height` and `Life` are), so the goldens can stand.
- **The TS sandbox:** the hardest. Its sim mirrors ours line by line, so
  every system's neighbour logic is rewritten twice, and its top-down board
  needs a way to show a column's spans.
- **Streaming and saves:** chunks keep their shape; a save keys cells by
  (index, span). Easy.
- **The view:** a mesh per chunk from every span's floor and ceiling (marching
  squares on each), joined where spans meet; holes in the Terrain over open
  columns. The most general cave shapes, and the hardest meshing.
- **Water down a dug shaft:** natural: digging the floor until it meets the
  ceiling of the span below merges them, and water flows into the lower one.

### B. Vertical chunks (64 × 64 × N voxels)

A true 3D grid: cells in 64 × 64 × N chunks, each a voxel 1.5 m across and a
height step (0.5 m) tall, as Minecraft or Teardown.

- **Sim cost:** the heaviest by far. The valley's height range is 15 steps and
  a cave a few metres down needs 20 to 30 more, so N is 32 or so: 32 times
  the cells. From the spike's numbers (a chunk is about 0.055 ms a tick when
  water moves) that is 1.8 ms per active chunk column, 44 ms a tick for a
  wizard's 5 × 5 reach, before fire and gas; still-water skipping and
  settled chunks help, but most of the volume is solid rock that still has
  to be stored and checked. Memory: the valley's 330 000 cells become about
  10 million, near a gigabyte as today's `Cell` objects: not on the PC's
  32 GB with an Editor open.
- **Lockstep and hashing:** a new hash over voxels; **every golden moves**,
  in both engines, and the 2.5D ones would need a "z = surface" reading to
  be kept.
- **The TS sandbox:** a new 3D sim, not a mirror of today's. Its top-down
  board would show one slice.
- **Streaming and saves:** chunks become 3D and the reach becomes a box;
  saves key cells by (x, y, z). Mechanical.
- **The view:** voxel or marching-cubes meshes for everything, and the Unity
  Terrain (and Punchy's biomes, D11) given up or rebuilt as meshes.
- **Water down a dug shaft:** natural, and true 3D flow, but the 2.5D water
  (units and `ElevationUnits`) is replaced by a voxel fluid: a new water sim.

### C. Levels: a few separate grids below, linked by shafts

Each level is its own 2.5D grid of cells, exactly as the surface is (terrain,
water, `Height`, chunks), at a fixed depth below the surface: level −1 a few
metres down, maybe −2 deeper. A level has chunks only where there are caves,
since the chunk store is already a sparse dictionary. A level is linked to the
one above wherever a column is open: dug down to the bottom of the height
range over a cave cell that isn't rock, or an authored cave mouth or
stairway.

- **Sim cost:** a level's chunk costs what a surface chunk costs, and only
  where caves are. Every system runs on a level unchanged. One new pass, the
  links, moves what crosses between levels: water falls (as much as the cell
  below can hold), gas and smoke rise. The spike measured it per tick:
  - the surface, 2 × 2 chunks with a pond draining: 0.22 ms;
  - a one-chunk cave level with the water arriving: 0.057 ms;
  - the links, scanning all 4 096 columns over the cave: 0.05 ms (a list of
    open shafts would make that next to nothing).
- **Lockstep and hashing:** levels step in a fixed order (surface, then down),
  with the links last, in integers. `World.CellHash` stays the surface's, so
  **no golden moves**. The lockstep checksum adds each level's hash only
  where there are levels, as `Height` and `Life` were added.
- **The TS sandbox:** small. It runs its levels as more boards with a level
  switcher in the UI, and the link pass is mirrored with a shared golden (the
  spike's shaft scenario). No system is rewritten.
- **Streaming and saves:** a chunk's key gains its level; a character in a
  cave keeps its level's chunks active, and the surface chunks over them so
  a shaft's water arrives. Saves gain a level per chunk record (`SaveFormat`
  2), and an untouched cave chunk regenerates like any other.
- **The view:** each level's chunks drawn as meshes at the level's depth (the
  floor from `Height`, walls where cells are blocking, a ceiling slab over
  them), the same kit as the raised walls today. The surface Terrain gets
  holes at open shafts and cave mouths. Authored pieces (the gate, a
  stairway, the lake's grotto) are placed on top. What it can't do: a tunnel
  that slopes continuously from the surface down through several metres.
  Inside a level the floor has the usual 15 steps of height (7.5 m) to slope
  with, and going between levels is a link (stairs, a ramp piece, a shaft, a
  drop).
- **Water down a dug shaft:** yes, and the spike shows it. A pond drained down
  a 2 × 2 shaft dug to the bottom (`Height` −6) and ran along the tunnel into
  the hollow below. The water was all conserved, two runs gave the same cells,
  and the cave level's water reached its own still-water rest.

## Side by side

| | A. Stack per column | B. Vertical chunks | C. Levels + shafts |
|---|---|---|---|
| Sim cost with no caves | +10–20 % (span lookups) | ×30 | none |
| Sim cost per cave chunk | ≈ a surface chunk | ≈ 30 chunks | ≈ a surface chunk |
| Systems rewritten | all | all (and a new water) | none; one link pass |
| Goldens | can stand | all move | stand |
| TS sandbox | every system twice | a new sim | more boards + links |
| Streaming / saves | (index, span) | 3D chunks | (level, chunk) |
| View | span meshes + holes | all voxel meshes | level meshes + holes + pieces |
| Water down a shaft | yes | yes (new fluid) | yes (spike) |
| Cave shapes | any, sloping | any | flat-ish levels, linked |

## Recommendation: C, levels linked by shafts

C reuses everything the core does today, at its own cost, only where caves
are. It keeps every golden and the sandbox mirror, and gets the goal scene's
three ways in:

- **The maze of tunnels:** a level's cells.
- **Digging down:** Move Earth to the bottom of the surface, then the
  level's rock, at its hardness, opens a link.
- **The gate:** a machine in the level, as `machines.json` has gates.

A and B buy cave shapes we don't need yet, at the price of rewriting every
system, or the whole sim, twice. If we ever need sloping tunnels between
levels, ramp pieces and stairs as links cover the play. And C's levels could
later become A's spans locally without changing the save or the view much.

**Slices if we go with C:**

1. **Levels in the core:** `World` holds levels (a depth, its own chunk
   store), steps them in order, and runs the link pass. Shafts open when a
   column is dug to the bottom over a cave cell, and Move Earth digs into
   the level below. Tests and a shared golden with the sandbox.
2. **The sandbox:** levels as boards with a switcher; the link pass mirrored.
3. **Streaming and saves:** a level in the chunk key and the reach, and
   `SaveFormat` 2.
4. **The view:** level meshes, Terrain holes, and characters going down and up
   (a fall or a ladder at a shaft, stairs pieces).
5. **The goal scene:** the cave and its lake under the valley (authored
   cells, or the theme engine's later), the tunnels, and the gate machine.

## Andrew's questions

1. **How deep, how many?** Proposed: one level for the goal scene (about 6 m
   down, under part of the valley), with room for a second later.
2. **Can a player dig a new cave anywhere,** carving a level's rock under any
   column (making level chunks as they dig), or only reach caves that are
   there (authored, or generated later)?
3. **Fixed levels, or must a tunnel slope continuously** from the surface
   down? C needs links (stairs, ramps, shafts) between levels.
4. **What crosses a shaft?** Proposed: water falls, gas and smoke rise, a
   wizard falls (taking SRD falling damage past 10 ft) or climbs a ladder,
   and spells don't cross.
5. **Light:** proposed that caves are dark (`LightLevel.Dark`, whatever the
   clock), so fire, Light and the lantern matter there (backlog #8).
6. **Online:** caves solo first, as saves are, or online from the start?
   Under C, lockstep is the same shape as today.
