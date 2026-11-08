# The world squares: a modular puzzle solved as you explore (plan for Andrew, revised 2026-11-08)

Read against dev at `5b4d0b09`. Nothing here is built. It builds on `maps-as-data.md`, `world-nine-options.md` (and
its "As built: phase 2") and `WORLD_STREAMING.md` part 3. Revised after J-080: the world is no longer baked by a tool.
Every peer solves it from the seed, a piece at a time, as characters come near.

## The idea in one paragraph

The world is a grid of valley-sized **world squares**, 576 × 576 cells (9 × 9 chunks) each. A **square template**
names what is curated: its landmarks as intents, where they may go, its elevation band and what each side offers its
neighbour. The rest is the climate's (`BiomeField`). The world is solved at two levels. The **world plan** is coarse
and decided for the whole world when it loads: each square's band, template and turn, the features crossing its sides,
the river network, and each landmark's site. The **chunk** is fine and made only when needed: its land, cells, plants
and things, a pure function of the seed, the plan and its coordinates. So who walked where first never changes what is
there. The 3 × 3 (the valley and eight squares) is the proof; the same solve runs on a 12 × 12 torus later.

New words for `GLOSSARY.md` first: **world square** (the code already says `squareCells`), **world plan**, **square
template**, **landmark intent**, and two chunk states before active: **known** and **generated** (section 3).

## 1. What changed, and what holds

- **Gone:** one baked 1728² folder, the tool that wrote it, and "peers never run the solver".
- **New:** every peer solves the plan at load and generates chunks on demand; nine square folders, made at load
  (J-080); a known ring drawn far off behind fog; ocean landmarks; repeats allowed, weighted down, at least 3 squares
  apart; negative chunk rows north of the valley; the world can wrap later.
- **Holds:** the data shapes (templates, intents with a form per biome, edge interfaces, bands with foothills), the
  first slice (one intent three ways), the variety section, landmarks built from rows we already read (`stamps.json`
  map stamps, `villages.json`, `buildings.json`, `things.json`, `places.json`), and the theme engine's maths (D11).

## 2. The data shapes (unchanged in form)

A **square template**, `world/squares/<id>.json`:

```json
{ "id": "lakeland-1", "band": "low", "biomes": ["temperate", "foggy-marsh"], "weight": 1,
  "landmarks": [ { "intent": "village-with-landmark", "where": { "inner": 0.6, "near": "water" } },
                 { "intent": "ruin", "where": { "band": "low", "awayFrom": "village-with-landmark", "min": 120 } } ],
  "edges": { "n": { "band": "low",  "offers": [ { "kind": "river", "may": true, "flow": "in" } ] },
             "e": { "band": "low",  "offers": [ { "kind": "road", "must": true } ] },
             "s": { "band": "low",  "offers": [] },
             "w": { "band": "hills","offers": [ { "kind": "range", "may": true } ] } },
  "ends": { "river": "lake", "range": "fade" }, "rotate": true }
```

`where` gives rules, never a cell. `ends` says how a feature may stop here. `rotate` turns it a quarter (four squares
from one file; no mirroring, as sunlight and river flow make some look wrong).

A **landmark intent**, `world/landmarks/<id>.json`, one form per biome (`"by": { "temperate": {...}, "mountains":
{...}, "foggy-marsh": {...}, "*": { "use": "temperate" } }`), each a list of map stamps, a village recipe, building
rows and things, offsets in cells from the site, plus `"seenFrom"` (how far off it shows, in chunks; section 4). The
first three: a forest village by a great tree (a stand-in: a scaled tree and a ring of roots; the face is art later),
a village at a volcano's foot (cone stamp, crater dome), a fishing village on the lake (hollow, docks).

**Ocean landmarks.** An ocean square takes an `open-sea` template that may hold one intent, such as `sea-spire`: a
tall cone with ridge map stamps rising out of sea cells, a `places.json` entry, `seenFrom` 12. You reach it with Frozen Tide or Iceberg, which makes the trip a spell-chemistry puzzle.

An **edge interface** is one feature at one slot along a side, 9 slots a side, one per chunk
(`{ "side": "e", "slot": 4, "kind": "river", "width": 6, "flow": "out" }`). Two sides match when slot and kind are
equal, widths agree within a class, and a river leaving one enters the next. The **bands** (`world/bands.json`: sea
-40, low 20, hills 120, high 300, peaks 700): sides differ by at most one band; hills are the foothills between.

## 3. Rings: known, generated, active

Per character, in chunks (Chebyshev, from the chunk it stands in), all in `rules/sim.json` `streaming`:

| Ring | Rule | State | Cost |
|---|---|---|---|
| **Active** | within `reach` 2, kept to `keep` 3 (today) | simulated | 0.02-0.4 ms a tick each |
| **Generated** | within `sight` 4 (new) | cells, land, things built; inactive, held as generated | one make, ~5-10 ms (estimate) |
| **Known** | within `know` 7 (new): five past reach | decided, not built: biome, a coarse height grid, landmarks | ~0.1 ms, ~1 KB |
| Beyond | in the plan only | nothing per chunk | none |

- **The trigger (J-080's "line of sight").** A chunk is generated when a character comes within `sight` of it: one
  chunk past `keep`, so a chunk is always generated before it can activate, with two chunks (~30 s at a run) of slack.
  The host stamps a **generate switch** into its tick batches beside the activate switches (`ChunkIntent`), so every
  peer generates the same chunks by the same tick, and a late joiner's replay does too. The work runs on a worker
  thread; the result is handed to the world at a tick boundary, in chunk order.
- **Known chunks** need no switch: they decide nothing shared. Each holds the dominant biome, heights on a 9 × 9 grid
  (every 8th cell: `BiomeField` at 81 samples, not 4096, about 0.06 ms, plus relief) and the plan's landmarks in it.
  A lone character knows 15 × 15 chunks, 144 of them past `sight`: about 15 ms at load, then 15 more per chunk crossed.
- **Hard edges.** The known ring stops at the coast: past one chunk of open sea it holds nothing, except landmarks
  whose `seenFrom` reaches (the spire).
- **Dropping.** A generated chunk nobody changed, past `sight` + 1 from every character, is dropped and made again
  when needed. A changed one is kept (and saved, section 7).

## 4. The far ring in the view (after Punchy)

Punchy draws a disc of terrain chunks out to `ViewRadiusChunks` (12 × 64 m) and hides its edge in haze. In
`C:\Users\Andrew\Development\Punchy\Assets\Game\` (local clone at `b183507`):

- `Scripts/Terrain/TerrainStreamingSystem.cs`: nearest chunks first, `MaxBuildsPerFrame` and a vertex budget, Burst
  jobs in the background picked up a frame or two later, unloading at radius + 1.5 so the edge doesn't thrash;
- `Scripts/Terrain/TerrainConfig.cs`: LOD 0 within 160 m, each LOD covering double the distance at half the quads,
  geomorphing over the last 40% of a band, skirts against cracks; `Scripts/Terrain/BuildChunkMeshJob.cs` builds them;
- `Scripts/Weather/WeatherSystem.cs`: exponential-squared haze, density 1.98 / draw distance, ~98% at the edge, so it
  never shows; `Shaders/TerrainSurface.shader` applies it;
- `Scripts/Flora/FloraSystem.cs`: trees to 1500 m, LODs at 55 and 220 m, placement a pure function of position;
- `Scripts/Terrain/Landmarks/LandmarkBaking.cs`: jittered candidate grid, a roll per spot, suited ground, spacing, and
  every type at least once where the land allows (our site scoring follows it).

Ours: active and generated chunks keep today's chunk terrains (257 near, 65 far, `ValleyGround`); known chunks get a
mesh from their 9 × 9 heights, painted by dominant biome, no colliders, built in a job; haze reaches ~98% at `know`
(about 670 m). Landmarks are drawn as far meshes from the plan out to their `seenFrom`, so a volcano or the spire shows
through the fog before its chunks are known. All float, all view: nothing shared depends on it.

## 5. The solve, order-free

**Level 1, the world plan, at load, for the whole world.** The world is bounded (an ocean-ringed 3 × 3 now, a torus
later), so the plan is small: 9 squares now, 144 at 12 × 12. Every peer solves all of it the same way, in a fixed
order, before play. Nothing in it depends on exploration.

1. **Pin the valley** as square (0, 0); its sides are read from its `land.json` at the borders.
2. **Bands.** A raw band per square from a low-frequency integer field (`BiomeMath.Noise` on the seed), then the
   envelope `band(s) = min over n of raw(n) + distance(s, n)`: neighbours differ by at most one, worked out within a
   window of 4 squares, so it is a pure function of the seed. Ocean squares are sea.
3. **Templates.** Wave-function style over the whole plan: the square with fewest options first (ties by coordinate),
   a seeded weighted roll, edges pruned, backtracking, a filler per band after 10,000 steps (logged). **Repeats:** a
   template already used weighs ÷4 each time, and no two copies within 3 squares (distance taken across the wrap later);
   none in the 3 × 3. The curated world may pin a square's template.
4. **Coarse rivers, decided ahead.** Rivers need the wide area, so they are planned on a chunk-sized grid (27 × 27 now,
   108 × 108 later, ~12,000 nodes, a few ms): heights from each square's band and biome base elevation, a priority
   flood from the sea, flow summed, a river where flow passes a threshold (width from catchment, as Punchy's
   `RiverBaking`). Each river crosses chunk sides at fixed points; the edge slots come from these, so squares agree.
5. **Ranges, canyons, roads** as polylines through the matched slots, wandering by seeded noise; roads after landmarks.
6. **Landmark sites.** Each intent's site scored on the coarse land, best one taken; its form from the biome there.
7. **The plan hash** (`WorldHash` over the plan in fixed order) is offered at join next to the map hash.

**Level 2, a chunk, on demand.** `ChunkGen(seed, plan, cx, cy)` reads only the plan and pure functions of position:
`BiomeField` weights, band heights blended over 300 cells, relief, river channels carved by distance to the plan's
polyline (the parabola fading over 110 m), map stamps of any landmark whose footprint reaches the chunk, then plants
and things seeded per spot, appended per chunk so ids hold. It never reads a neighbouring chunk's cells, so chunks can
be made in any order and agree at their seams. Golden: generate a set of chunks in two orders, compare chunk hashes.

**If a plan ever gets too big** (an endless world): solve it in blocks of squares whose shared sides are pure
functions of the seed and the side, so blocks never wait on each other. Not needed for any bounded world here.

## 6. Folders, hash and approval

- **Committed:** `world/maps/valley/` (pinned, as now); `world/squares/`, `world/landmarks/`, `world/bands.json`,
  `world/biomes.json`; and a world file `world/maps/nine/map.json`: seed, size in squares, generator version, pinned
  squares, the anchor biome per square (J-079) and any pinned templates.
- **Generated per game, never committed:** nine square folders under the player's data folder
  (`worlds/nine/<world hash>/<sx>,<sy>/`), one per square, made at load: `square.json` (its slice of the plan:
  template, turn, band, edges, river and range points, landmark sites) and `known.json` (each chunk's biome and coarse
  heights). As chunks are generated, their `land`, `cells`, `things` and `places` rows are added. It is a cache of a
  pure function: deleting it changes nothing; `MapCheck` can read it.
- **The hash.** `MapIdentity` for a generated world hashes the inputs, not the output: the world file, every committed
  file it reads, the pinned valley's folder hash and the generator version. A peer with another generator or another
  template is refused as today. The plan hash (5.7) catches a generator that differs anyway, before a chunk is made;
  each active chunk's hash already joins the checksum.
- **Coordinates.** The valley keeps chunks (0..8, 0..8); squares north take negative rows (y −18..−1), east ones
  x 9..26. `Chunks.ChunkOf` already floors; the cell store's `y × W + x` indexing and `ThingId`'s packing must take
  negatives (to check first in phase 4).

## 7. Variety without stamping (holds)

- The curated 3 × 3 needs 8 distinct templates; 12 to start (48 with turns), 16-20 for the library. At 12 × 12 with the
  3-square rule a template recurs at most 16 times, so 20+ keeps repeats rare.
- 6-8 landmark intents with 3 forms each are 18-24 distinct landmarks before placement differs.
- Per instance, seeded: the site, the village's seed, the place's name, every path's wander.
- From nine-options: a rare feature per region, a nudge to a biome's relief per region, borders that vary in width.
- A template says little (one or two intents, a band, edges); the land between is the field's. Two "lakelands" share a
  story, not a shape.

## 8. Torus (stretch: what not to rule out)

12 squares a side, 108 chunks, 6912 cells, about 10.4 km; the size a setting. Don't build it; keep these open:

- **One wrap function** for cell, chunk and square coordinates; no code assumes a world starting at 0 or a rectangle.
- **The solver's neighbour lookups** (bands, templates, the 3-square rule, the river flood, edges) all go through it.
  A torus has no sea edge, so every torus world needs ocean (or lakes) as the flood's sinks.
- **Noise must be periodic:** `BiomeField`'s lattices (regions 576, warp, climate 1234 cells) must divide the world
  size or hash coordinates modulo it. `climateCells` 1234 would change.
- **Rings across the seam:** distances wrapped, and the known ring and far landmarks drawn across it.
- **Floating origin:** 10 km is past the 5-10 km where `WORLD_STREAMING.md` says floats jitter; the view re-centres
  on the players, and the seam is just one more re-centre. Cell coordinates stay integers.

## 9. Phases

| # | Phase | What Andrew sees | Size | Needs | Where |
|---|---|---|---|---|---|
| 1 | **First slice: one intent, three ways.** `LandmarkIntent` and its materializer in the core | Three top-down PNGs: great-tree, volcano and fishing villages | S | Nothing | u2 |
| 1b | **The slice in the valley** (a branch never merged) | Three 30 s clips walking up to each | S | 1 | PC |
| 2 | **The world plan.** Bands, 12 templates, edges, repeats, fillers, plan hash; the curated nine as a world file | Ten seeds as diagrams (3 × 3, and a 12 × 12 plan) | M | 1 | u2 |
| 3 | **Coarse features.** River flood on the chunk grid, ranges, canyons, roads, landmark sites; `BiomeField` takes anchors from the plan, not the fixed `layout` | Plan pictures with rivers and landmarks across squares | M | 2 | u2 |
| 4 | **`ChunkGen`.** A chunk from seed and plan, valley pinned, negative rows, square folders and `known.json`, the input hash | Height pictures of chunks made in two orders, the same; times per chunk | L | 3; nine-options 1 and 3 | u2 |
| 5 | **Rings in the core.** Known and generated in `ChunkPlanner`, the generate switch, dropping, worker thread | A replay test; memory and time for a walk across the nine | M | 4 | u2, then Mac (threading golden) |
| 6 | **Play it.** Size from `map.json`, chunk terrains built on demand, the far ring and landmarks behind haze, biomes painted | Walking from the valley into a second square, a volcano in the fog; a two-peer run | L | 5; maps 6-8; nine-options 7 | PC (Mac second) |
| 7 | **Saves for a generated world** | Save in square two, quit, load: the same burn marks | M | 5; save slot | u2, then PC |
| 8 | **More of it.** Ocean spire, 20 templates, 8 intents, a library of seeds, then a seed at start | New worlds that don't repeat | M, ongoing | 6 | u2; Andrew writes templates with Claude |

Phases 1-5 are core only (u2, Linux) while the PC does maps 6-8; the PC waits only at 1b and 6.

## 10. Risks

- **Memory.** About 0.7 MB of `Cell` objects per chunk (the valley's 50-70 MB over 81): a lone character's 9 × 9
  generated chunks is ~60 MB, four players apart ~240 MB, plus terrains, beside an Editor on the 32 GB PC. Dropping
  untouched chunks bounds it; if not enough, `Cell` becomes arrays (D9). Measured in phase 5.
- **Frame time.** A chunk's make is `BiomeField` 3.2 ms (M2 Pro) plus relief, carving, stamps and things, maybe 5-10
  ms; crossing a chunk generates a row of 9, ~50-90 ms of worker time every ~15 s. Unity terrain building costs more
  than the core: jobs, a build budget per frame and nearest first, as Punchy does. A slow peer delays the activate
  switch rather than desyncing (the host waits for "generated" before it stamps "active").
- **Lockstep of on-demand generation.** Safe only if a chunk reads nothing but the seed, the plan and its coordinates,
  and the worker hands results over at tick boundaries in chunk order. The two-orders golden and a PC/Mac golden on the
  plan and chunk hashes guard it. Floats in the view only.
- **Saves.** A save records the world file's hash, seed, generator version and plan hash, then only what changed:
  per chunk, the cells that differ from the generated chunk (`World.Save` already diffs against `IChunkSource`, which
  `ChunkGen` becomes) and things broken or moved by id. Untouched chunks and the square folders are never saved. A new
  generator version is a new world: old saves are refused, not misapplied.
- **The solver.** No band fits (the envelope can't fail); a river uphill (the flood forbids it); a landmark with no
  site (relax once, then fail by name); a road across a canyon (detour, no bridge row yet); corner pinches (the
  300-cell blend, a corner check). Fillers keep the template solve from failing.
- **The Hold and content.** The castle stays in the pinned valley. Great tree, lava and smoke, docks, the spire:
  stand-ins first.

## 11. Decisions for Andrew

1. **Plan the whole world at load, or in blocks?** (a) The whole plan at load on every peer (9 squares now, 144 at
   12 × 12; expect well under a second). (b) Blocks of squares solved as they are first known. **Recommend (a)**; (b)
   only if phase 2 measures over a second at 12 × 12, or the world goes endless.
2. **Where the nine square folders live.** (a) On disk as a cache in the player's data folder, rebuilt when missing.
   (b) In memory only, with a tool that writes them out to look at. **Recommend (a):** it is his pick, and `MapCheck`
   and the pictures can read it.
3. **How far is "line of sight"?** (a) `sight` 4 (generated one past `keep`), `know` 7 with haze to its edge
   (~670 m). (b) Larger, `sight` 5 and `know` 9 (~860 m), about 1.6 times the generated chunks and memory.
   **Recommend (a)** to start; both are numbers in `sim.json`, tuned in phase 6.
