# A 12 x 8 world: what it takes (scoping, 2026-11-09)

Andrew asked (2026-11-09) what it would take to grow the nine (3 x 3 world squares) to a 12 wide x 8 tall grid,
laid out like the paper prototype (`tools/world-paper/`): an ice cap north, a desert south, east-west wrap; and to
make the three distance tiers dials for performance testing. This is the lead's research on dev at 1b0244b2; "(inf)"
is inferred. 12 x 8 is 96 world squares, 7776 chunks, 6912 x 4608 cells, about 10.4 x 6.9 km.

## What exists

- **The plan already wraps, but only square plans.** `WorldPlan.Grid.cs` `PlanGrid` has `PlanWrap.None/EastWest/
  Torus`; neighbours and `ChunkDistance` take x modulo W. `PlanInputs.Size` is one int (N x N); with EastWest the grid
  is N by N+2, the two extra rows walls (`Wall` 1 ice north, 2 dune sea south). `Cold[]` gives latitude;
  `LatitudePeak` weighs templates by row (salt-flat at 0, tundra 0.88). 12 x 12 EastWest and Torus plans are tested
  (`WorldPlanTests`, golden seed-2 EW 0x7925e04d, < 1 s). `ice-wall`, `ice-crown`, `dune-sea` templates exist.
- **Square-only spots:** `GeneratedMap.Load` refuses W != H and always passes `PlanWrap.None`; `PlanInputs.For` turns
  wrap off at size 3 or less.
- **ChunkGen has no wrap and no ice or desert land:** `ChunkGen.Make` throws off the map; wall squares are just 900 m
  high (`WallMetres`); glacier, dunes, sonoran, glass-desert are off in `biomes.json`; `climateCells` 1234 doesn't
  divide 6912, so climate noise would break at the seam (world-squares.md section 8).
- **The paper does more than the core:** ragged passable fringes in the wall rows (`edgeAt`), ice cliffs and a beach
  with a sandstorm where an edge meets the sea. Not ported. It offers 3 x 3 and 12 x 12 only.
- **Generated chunks aren't streamed:** the whole map is baked at Play (`GeneratedMap.EnsureBaked`), `World` holds
  `Cell[w*h]` (63 flat uses), and `MapTerrains.Make` places a Terrain for every chunk at start (729 on the nine).

## The three tiers today

| Tier | Today | Setting | Shared? |
|---|---|---|---|
| Active (cells simulate) | `reach` 2 / `keep` 3 chunks (linger 16), `ChunkPlanner` | `rules/sim.json` `streaming` | Lockstep: the host stamps it |
| Visitable and drawn | Every baked chunk within `know` gets its Terrain; `sight` 4 exists but draws nothing; dressing culled at 500 m, trees 900 m, grass 120 m | None of its own | View |
| Known (far meshes, haze, landmarks) | `know` 7 chunks (`KnownRing`, `FarRing`); far landmarks by `seenFrom` 9-22 chunks (`world/horizon.json`) | sim.json, horizon.json | View |

None changes per peer at runtime. Costs: KnownRing 0.24-0.28 ms a chunk; the nine's farringclip ~31 ms mean, 38 ms
p95. A cell is 64 B; ChunkGen ~27 KB and ~5 ms a chunk.

## At 12 x 8 (inf, from those numbers)

Every cell in memory is ~8.5 GB and 7776 Terrains ~2-3 GB: that doesn't fit beside an Editor in 32 GB. So a **sparse
World** (only generated chunks held, a generate switch) is the critical path. Float precision past ~5 km wants the
origin re-centred on the player (with E-W wrap the eye stays within ±5.2 km E-W, ±3.5 km N-S).

## Missing, with rough worker-days

| Piece | Days | Who |
|---|---|---|
| Plan W x H, map.json `squares`/`wrap`, 12 x 8 pictures, goldens | 1-1.5 | u2 |
| ChunkGen wrap, periodic noise (generator v6), seam golden | 1.5-2 | u2 |
| Ice and desert edges in ChunkGen (biomes, ragged fringe, cliff, beach, impassable wall) | 2-3 | u2 (paper exists) |
| KnownRing and FarLandmarks across the wrap; tier dials, with view overrides | 1 | u2 + PC |
| Sparse World, generate switch, dropping untouched chunks, replay test | 5-8 | u2, then a Mac threading golden |
| The sim across the seam (52 bounds checks in 23 files, chunk links, planner, spans), lockstep golden | 3-4 | u2 |
| Chunk Terrains made and dropped on demand (budget, nearest first) | 3-5 | PC |
| Origin re-centre; far ring, landmarks and camera across the seam | 2-3 | PC |
| A tier-sweep perf probe (frame mean/p95, memory, chunks per tier) | 1-2 | PC |

About 20-30 worker-days. Risks: memory (sparse World), the seam (one missed bounds check desyncs), generation time
when crossing chunks, float jitter without re-centring, save size per chunk (never measured).

## A cheap first proof

1. u2, ~2 days: a 12 x 8 E-W plan and a known-only world (`squares [12,8]`, `wrap "ew"`, ChunkGen wraps), KnownRing
   timed over all 7776 chunks, a plan and heights picture showing ice north, desert south and an unbroken seam.
2. The PC, ~2 days, in parallel: the three tier dials and a sweep probe on the nine as it is, for today's frame time
   and memory per setting.
3. Then the sparse World and on-demand Terrains, a walk on a 12 x 8 world without wrap; then the seam; then the ice
   and desert looks, paper first.

Decided (J-083): 8 land rows plus an ice row north and a desert row south, 12 x 10 world squares in all.
