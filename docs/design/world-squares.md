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

**Coasts are part of the puzzle** (Andrew, 2026-11-08: "the coastline as part of the puzzle to solve for the tiling
... we also need to make sure the coastlines are consistent"). A side carries a **coast** profile beside its feature
slots: which of its 9 slots are sea, and the shoreline's offset into the square at each slot
(`{ "side": "s", "kind": "coast", "sea": [0, 1, 2, 3], "offset": [40, 40, 52, 70] }`). Two sides match when the same
slots are sea and the offsets agree within a class, so a shore runs on across a side; at a corner the four squares'
coasts must agree on the corner slot, as bands do. A square with no sea on a side offers an empty coast. Rivers end at
a coast slot or a lake; roads stop or turn at a coast. The valley's coast is read from its land like its other edges.

**Rivers and roads must make sense** (Andrew, 2026-11-08):

- **Rivers flow from high to low,** always: a river enters a square from a band at or above it and leaves to one at or
  below it, and inside a square its bed only goes down. A river starts high (a spring in the hills, high or peaks, or a
  lake) and ends at the sea or a lake.
- **Roads lead somewhere.** A road joins two destinations (landmarks: villages, the castle, a spire's shore, a pass)
  and spans **at least three world squares** end to end; a stub that starts in one square and ends in the next is
  refused. So roads are solved as routes across the plan, not per side: the edge slots a road uses come from its route.
- **Roads avoid big height changes.** A route prefers squares in the same band and crosses at most one band step at a
  time, with a gentle grade inside a square (switchbacks where it must climb). No roads up the peaks; a pass crosses a
  range at its lowest slot.
- **Roads are scenic.** Along a route, place **outcrops** to look at: a seeded feature every so often within sight of
  the road (a rock spire, a crag, a waterfall where a river meets a band step, a lone great tree, a ruin, a view over
  the sea), so walking a road always has something on the skyline. Outcrops are a new small landmark intent, cheap,
  with `seenFrom` sized for the known ring.

**Roads have a reason** (Andrew, after the paper prototype: "a lot of roads and they all just kind of go a couple spaces
and stop ... I want the roads to be able to be sort of dynamic but intentional"). The order and the kinds:

1. **Points of interest first.** The plan places its destinations before any road: towns and villages (the landmark
   intents), the castle, caves, passes, a spire's shore, and the **crossings** that nature allows (below). Roads exist
   to join these.
2. **Main roads, few and long.** At most about **six** in a world (a rule number, scaled with world size). Each joins
   two major destinations far apart (towns, the castle) across many world squares, and main roads keep apart: no two
   run side by side; they meet at a town or a junction and head different ways. A main road is well made and reads as
   one: fences along it, marker stones showing the way, graded, and **waystations** (a small shop or inn) every few
   squares along it.
3. **Lesser roads and trails** branch off: from a main road to a cave, a ruin, a spire's shore or a lone village; or
   between two small towns the main roads don't join. Narrower, unfenced, they may climb more and end at their
   destination (a trail is allowed to be a dead end; a main road never is).
4. **Nature makes roads work for it.** A tall mountain is gone round, not over (peaks and steep bands cost far more than
   distance). A **canyon has only one or two bridges**, placed by the plan, so every road crossing it routes to a
   bridge; a big river has a few fords or bridges the same way; a range is crossed only at a pass. These crossings are
   destinations in their own right, so roads converge on them and they become places (a bridge town, a toll, a ford).
5. **Tiles keep the road interface.** The routes are solved once in the world plan, at load on every peer (J-080); each
   world square's sides still carry road slots, now filled from the routes, so `ChunkGen` draws a road through a chunk
   from the plan alone. Dynamic (any seed, any world size) and intentional (every road joins two reasons).

**World shape, oceans and latitude** (Andrew, 2026-11-08). Oceans are a feature, not only an edge: the plan takes a
world shape (one island with ocean all round, a few continents, an archipelago) and lays ocean squares to suit. **The
world wraps east-west only** (a cylinder): north and south are firm edges of **impassable ice mountains**, a band no
road, river or character crosses. **Climate follows latitude:** desert and salt flats near the middle, temperate either
side, then tundra and mountains toward the poles, then the ice wall; the square templates' anchors work on top of that.
Or **half that**, Andrew's preferred start: ice mountains along the north, blending south through tundra and temperate
to desert along the south edge (a firm boundary there too: an impassable deep dune sea or salt waste), east-west
wrapping. **The edges blend, they aren't walls** (Andrew, after the prototype): the polar ice and the southern desert
edge blend into the biomes beside them with the same warped borders used everywhere else. Where the ice edge meets
sea, it's a **jagged ice cliff**, broken and uneven, not a smooth wall. Where the desert edge meets sea, it's a **sandy
beach**, with a **sandstorm** just beyond it to the south as the impassable edge. The valley stays pinned at a
temperate latitude. The full torus stays possible as an option (the earlier "torus,
keep in mind" still holds for it), but the default is the east-west wrap.

**Inland seas** (Andrew, 2026-11-08). A large body of sea water inside the world, not touching its edge: one or more
sea-band world squares (and coast profiles on their neighbours) placed by the plan as a region, so the coast rules
apply all round it. Rivers may end in it; the known ring stops at its shore unless a landmark's `seenFrom` reaches
across; main roads go round it or end at a port town on it; a crossing by boat is a later idea. A rock spire or an
island landmark may stand in it. Drawing it in the game needs maps phase 7 (the sea from `land`), like coves.

**World archetypes: describe a world, keep the best** (Andrew, 2026-11-08). A prompt such as "a cold northern
archipelago with one great inland sea and a volcano" becomes a world anyone can regenerate:

1. **Claude turns the words into constraints,** never into the world: anchor biomes, sea and inland-sea share,
   mountain cover, the number of towns and main roads, must-haves ("a volcano", "a canyon with one bridge"). These are
   plan inputs, so the solver stays deterministic and every peer gets the same world (D4).
2. **Many candidates, scored.** The plan solves in about 0.1 s, so hundreds of seeds per prompt cost seconds. Each is
   scored on numbers first: how well it meets the constraints, fillers, road lengths, landmark spacing, the castle
   reachable, variety. A judge (Claude looking at the plan picture, or Andrew) picks from the top few; Andrew decides.
3. **An archetype keeps more than the seed:** the description, the constraints, the seed, the generator version, and
   the solved world plan itself (a few KB), in `world/archetypes/<name>.json`. A seed alone stops giving the same world
   when the generator changes; the saved plan doesn't. A test re-solves each archetype and flags when a generator change
   would alter it, so the change is made on purpose (a new version) or the archetype is re-picked.
4. **In a game,** the host picks an archetype (or a fresh seed later, per J-080); peers load the same plan, and the map
   hash covers it. This is J-080's "one curated world, then a library, then a seed at start", with the library made by
   describing worlds.

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

## As built: phase 4, ChunkGen (chunk-gen, u2, 2026-11-09)

- **`Chem.ChunkGen`** (`ChunkGen.cs`, `ChunkGen.Land.cs`): `Make(cx, cy)` makes one chunk's land, cells and things from
  the plan alone, in integers on `WorldHash`, safe from any thread. A cell's land comes from its position only:
  - the bands of the four nearest world squares, blended over the 300 cells either side of each border (a sea square
    counts as low land, and the coast takes it down);
  - `BiomeField`'s shape weights (300 cells), with its anchors from the plan's templates in place of `biomes.json`'s
    fixed layout and the plan's seed. Each biome adds its base and its relief: hills either side of the base, ridged
    and dunes rising from it;
  - the coast from the plan's shore depths, wobbled along each side and back to the plan's depth at the corners, so a
    shore runs on into the next world square. The land leans down to the shore over 100 cells, and the sea bed falls to
    the sea band over 90;
  - rivers from each side's slot to the world square's hub, with the water at each end a pure function of that point
    (bands and biome base, 2 m down) and the hub's set below every river flowing in. The channel is carved, and the
    land either side leans down to the water over 110 m (73 cells). There is a lake at the hub where a river ends;
  - roads from their slots to the hub, levelled across to the land at their middle and banked back over 6 cells;
  - last, the pinned valley's own land, met over 90 cells outside it.
  - Then come the landmarks' map stamps (`LandmarkIntent`'s rows, baked into the land), the cells, and the things.
    Cells are the biome's ground with seeded trees and brush, stone where a face is steep, and water where the sea, a
    river or a lake is. Things are the landmarks' rows first, then a seeded boulder at some of the 64 spots, appended
    per chunk so ids hold.
- **The valley pinned:** its 81 chunks are its folder's land, cells and things byte for byte (a test checks every
  cell), and the chunks beside it meet its edge (the valley-side test: no step bigger than the valley's own).
- **Negative rows: none.** The map's origin is the nine's north-west corner, not the valley's, so the valley is
  chunks (0..8, 18..26) and no coordinate goes below zero. The cell store's `y × W + x` from zero is the one thing that
  can't take a negative row, and every loop over the world, in the core and the view, runs from zero. Moving the origin
  costs one shift of the valley's rows as the nine is put together (1152 cells south). `Chunks.ChunkOf` already floors,
  and `ThingId` already packs signed 16-bit chunks, so negative chunks would only be a cell-store question if a world
  ever needs them.
- **The world file** `world/maps/nine/map.json` holds the seed, the size in world squares, the generator version and
  the pinned valley. `GeneratedMap` reads it, solves the plan and makes the generator. The **input hash**
  (`GeneratedMap.InputHash`) covers the world file, `bands.json`, `biomes.json`, every square template and landmark
  intent, the valley folder's hash and the two versions. `MapIdentity.Hash` gives it for a generated map, so a bake
  never changes the map's identity.
- **A map the PC can load:** `GeneratedMap.Bake(dir)` writes a whole 1728² map folder that phase 7 loads with `-map nine`:
  `cells.txt`, `land.json`, `things.json`, and the valley's rows moved to its place with the landmarks' added
  (`stamps`, `villages`, `buildings`, `golf`, `gauntlet`, `puzzles`, `jobs`, `rocks`, `hold`, `looks`). `places.json`
  is the hand-written places moved, then `WorldPlaces.Generate`. `GeneratedMap.EnsureBaked(world/maps/nine)` bakes into
  the folder itself (gitignored apart from `map.json`) unless `bake.json` already holds these inputs. A test bakes it
  and holds it to `MapCheck`.
- **Measured** (u2, a 2-core VM, core Release):
  - one chunk: median 4.9 ms, p95 12.5 ms;
  - the whole nine (729 chunks): 2.4-2.6 s on 2 threads, then 0.5 s to write the folder;
  - memory: 20 MB held for all 729 chunks' land, cells and things.
  - The golden of the nine's chunk hashes and four single chunks is in `ChunkGenTests`. Chunks made one at a time
    from the far corner back, and again on one thread row-major, give the same hashes as the nine made on every thread.
- **Not drawn yet:**
  - caves (no intent file), crossings, waystations and outcrops (they are in the plan, and nothing is stamped for them);
  - the valley's `ground/` and `themes/`: in the nine the valley is drawn from its land, like any map without a ground
    pack;
  - buildings' `site.metres`, which only the old valley scene read, isn't moved.

## As built: chunk-gen-polish (u2, 2026-11-09)

What the first heights picture of the nine showed, fixed (generator version 2; the goldens and the nine's `expects.highest`
changed with it):

- **The meet with the valley** was a comb of stripes: each row or column outside the valley copied the one rim cell
  straight in from it, so the rim's cell-to-cell roughness ran out 90 cells. The meet now reads the rim as a mean along
  it over as many cells either side as a cell is out from it (running sums round the rim as one loop, so the corners
  carry on), and it widens where the rim stands far above or below the land beside it, up to 240 cells, so its steepest
  pitch stays under 6 land units a cell. `TheValleysMeetIsSmoothBothWays` holds it: from 8 to 90 cells out, a cell
  bends against its neighbours (along the side and across it) by more than 2 land units on under 0.2% of cells (0.08%
  in the nine), never more than 8, where the first meet bent by 3 to 10 on about one cell in eight.
- **The grey rectangle** under the valley's north edge was stone on a steep face: the valley's north rim stands about
  117 m up, the land beside it about 23 m, and the 90-cell meet fell 1.75 m a cell, over `SteepUnits`. The wider meet
  is a slope, not a face.
- **The stripes along the south coast** were the shore's distance rounded to whole cells, so the land leaning down to
  it stepped a column at a time. The shore is now in fractions of a cell (`Shore`, Q16).
- **Rivers** (`ChunkGen.Rivers.cs`) are curves, not ruler lines: each leaves its side square on for 110 cells (as the
  paper prototype draws them), runs straight through the hub where one river comes in and one goes out, and wanders
  either side by seeded noise, up to 44 cells, none at either end so the next square's and the hub's carry on from it.
  They widen downstream with the squares they drain (half a width of 2 cells at a spring, 4 for one square, one more for
  each square upstream, at most 8), and the water only goes down: over the whole plan, every crossing stands at least
  a metre below the hub upstream of it and every hub a metre below each crossing flowing in, worked out before any
  chunk. `RiversWanderDownhillAndWidenDownstream` checks levels, widths, the joins, the corridor and the wander.
- **The central plateau** (the tundra plateau, band hills plus tundra's 105 m base) was one dome from the bands' and
  biomes' smooth blends. Land more than 60 m up is now a **massif**: ridged noise stretched three times along the world's
  **grain** (a seeded direction) and warped, so crests run as ranges instead of closing into rings, with finer spurs on
  them; broad noise drops some of it to saddles and lifts summits; a quarter-strength terrace leaves shoulders. River
  levels take the massif too. `HighGroundReadsAsMountains`: lines across the square rise and fall by 10 m 55 times on 24
  lines (the dome: 34).
- **Still to look at:** a river whose level is far below the land beside it (a spring on a massif) leans its banks over
  the same 73 cells, so a gorge wall can be stone; the radial streaks round a spring are its bank leaning.
