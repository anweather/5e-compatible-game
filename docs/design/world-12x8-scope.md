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

## Measured

`tools/probe.sh tiersweep` on the nine (2026-11-09, the PC, Editor Play mode, 10 s a setting, clear weather at 11): the
eye flies a square a chunk a side round the spawn, 45 m up, the wizard under it. Active is streaming `reach` (keep one
more) as a rule switch; drawn and known are the peer's view dials (`FarRing.SetView`, a player's `-drawn N -known N`).
Today's dials are active 2, drawn 4, known 7. (Drawn turned out to be `sight` already: the known ring starts past
it, and a chunk drawn as a known mesh hides its Terrain, so Terrains draw within sight and known meshes beyond.) Rows run in this order, after one unrecorded warm-up lap.

| Active | Drawn | Known | Mean ms | p95 ms | Allocated MB | Reserved MB | Active chunks | Terrains drawn | Known meshes | Known settle ms | Mesh worker ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 2 | 4 | 28.6 | 40.1 | 20843 | 32814 | 16 | 25 | 47 | 536 | 1.4 |
| 1 | 4 | 4 | 26.9 | 31.0 | 20995 | 32814 | 16 | 72 | 0 | 60 | 0.0 |
| 1 | 2 | 7 | 27.8 | 33.4 | 21160 | 32854 | 16 | 25 | 107 | 111 | 2.9 |
| 1 | 4 | 7 | 28.5 | 32.5 | 21327 | 32938 | 16 | 72 | 60 | 125 | 2.1 |
| 1 | 7 | 7 | 23.3 | 27.9 | 21327 | 32938 | 16 | 132 | 0 | 56 | 0.0 |
| 1 | 2 | 12 | 22.1 | 25.1 | 21329 | 32938 | 16 | 25 | 247 | 117 | 7.0 |
| 1 | 4 | 12 | 22.3 | 25.5 | 21329 | 32938 | 16 | 72 | 200 | 91 | 4.9 |
| 1 | 7 | 12 | 23.3 | 26.8 | 21332 | 32938 | 16 | 132 | 140 | 98 | 3.6 |
| 2 | 2 | 4 | 22.8 | 28.7 | 21328 | 32954 | 36 | 25 | 47 | 87 | 1.4 |
| 2 | 4 | 4 | 23.6 | 29.1 | 21328 | 32954 | 36 | 72 | 0 | 53 | 0.0 |
| 2 | 2 | 7 | 23.1 | 28.8 | 21329 | 32954 | 36 | 25 | 107 | 100 | 2.9 |
| 2 | 4 | 7 | 24.2 | 30.2 | 21329 | 32954 | 36 | 72 | 60 | 88 | 1.9 |
| 2 | 7 | 7 | 25.0 | 31.0 | 21328 | 32954 | 36 | 132 | 0 | 58 | 0.0 |
| 2 | 2 | 12 | 22.9 | 28.5 | 21333 | 32954 | 36 | 25 | 247 | 112 | 6.3 |
| 2 | 4 | 12 | 23.8 | 29.6 | 21330 | 32954 | 36 | 72 | 200 | 114 | 5.1 |
| 2 | 7 | 12 | 25.9 | 32.1 | 21328 | 32954 | 36 | 132 | 140 | 109 | 3.6 |
| 3 | 2 | 4 | 24.8 | 30.2 | 21328 | 32954 | 64 | 25 | 47 | 100 | 1.2 |
| 3 | 4 | 4 | 26.0 | 31.3 | 21328 | 32954 | 64 | 72 | 0 | 55 | 0.0 |
| 3 | 2 | 7 | 26.4 | 32.0 | 21329 | 32954 | 64 | 25 | 107 | 114 | 3.0 |
| 3 | 4 | 7 | 26.1 | 31.8 | 21325 | 32954 | 64 | 72 | 60 | 107 | 1.5 |
| 3 | 7 | 7 | 27.2 | 33.1 | 21328 | 32954 | 64 | 132 | 0 | 66 | 0.0 |
| 3 | 2 | 12 | 25.3 | 30.5 | 21330 | 32954 | 64 | 25 | 247 | 124 | 6.0 |
| 3 | 4 | 12 | 25.9 | 31.5 | 21330 | 32954 | 64 | 72 | 200 | 126 | 4.9 |
| 3 | 7 | 12 | 27.0 | 32.6 | 21329 | 32954 | 64 | 132 | 140 | 109 | 4.1 |

What it says, so far:

- **Frame time hardly moves with any dial on the nine** (22-29 ms mean, 25-40 ms p95, today's 24.2 / 30.2). The spread
  is about the run's own noise: the slowest rows are the first two (warm-up, 536 ms to settle) and the same settings
  differ by 5 ms between active 1 and active 2. The frame is bound by something the dials don't touch.
- **Known 12 is cheap here**: 247 known meshes, the ring settles in ~120 ms and the mesh worker takes ~6-7 ms.
- **Drawn is the terrain count** (25 at 2, 72 at 4, 132 at 7) with no clear frame cost up to 7.
- **Memory is the Editor's**, not the game's: 20.8-21.3 GB allocated and 32.8-33.0 GB reserved throughout, so a
  setting's own cost is lost in it. Per-setting memory wants a player build (`-perf-scenario` with `-drawn`/`-known`).
- Active chunks are 16 / 36 / 64 at active 1 / 2 / 3 (4 x 4, 6 x 6, 8 x 8: the reach round the lap's chunks), with
  no frame cost seen at 3 on a quiet world (no fire, no weather).

## As built: the plan and the known ring (world-12x10-plan, u2, 2026-11-09)

Step 1 of the cheap first proof, core and data only. The pictures are `docs/design/world-12x10/plan.png` and
`heights.png` (the whole world from its known samples, a pixel per 8 cells, the first two world squares drawn again past
the east edge; the seam is the red tick).

- **`world/maps/world-12x10/map.json`**: seed `valley-1`, `squares [12, 10]` (wall rows included), `"wrap": "ew"`,
  `"bake": false`, generator 6, the valley pinned at the plan's own valley square [0, 5]. A world that wraps must be its
  whole plan (`GeneratedMap.Load` says so otherwise). `EnsureBaked` and `Bake` refuse it; its `expects` are read from
  the known samples (`WorldTwelveTests.ItsExpectsAreTheKnownLand`): land share 89 %, highest 9886, lowest -320.
  `ChemGrid.Maps()` (view) still lists it: picking it in play throws the refusal, so the menu wants to skip
  `"bake": false` maps (PC, a one-line filter).
- **`PlanInputs`** has `Width` and `Height` (land rows; `Size` is the longer). A square world's seed and noise are as
  before, so the nine, its goldens and the valley are byte-identical. Plan golden 0x3e847c28; it solves in ~0.5 s.
- **Generator 6 (`ChunkGen.WrapX`)**: land is the same at x and x + 6912, bit for bit (tested near the seam). Every
  noise lattice repeats round the world: `BiomeMath.Noise` takes periods, and an axis that repeats takes a whole number
  of control points a period, the lattice stretched to fit (climate 1234 cells goes round as 6 of 1152; regions and
  warp divide 6912 already, hotspot columns stretch the same way). `BiomeTable.WrapCells` makes regions repeat (12
  across). The massif's grain and the dunes' wind snap to an axis in a world that wraps (a slanted lattice can't
  repeat). Rivers and roads near the seam are copied a world's width either side (`seamCopies`); the meet, clearings
  and stamps measure the short way round. A map that doesn't wrap takes the old code paths, so the nine's golden
  (0x7c681170) holds; nine's map.json says generator 6 only because the version is in its input hash.
- **KnownRing and FarLandmarks** measure across the wrap (`ChunkGen.ChunkDistance`, chunk ids modulo the width,
  `FarLandmark.DxTo`).
- **Measured** (this VM, 2 threads): every one of the 9720 chunks known in ~2.0 s, 0.12-0.22 ms a chunk on one thread;
  the ring from one character knowing the whole world 1.2-1.7 s, 9111 chunks shown (881 open sea away from any coast).
- **The seam**: on land the biggest step across it is 65 land units, under the 97 inside the chunks beside it; onto
  the valley 1 more than the valley's own next step. Under the sea it isn't smooth, and that's not the wrap: where a
  coast's sea ramp meets an open-sea world square the floor drops straight to the deep, about 170-180 land units in one
  cell, at every such border (squares (6, 6) and (7, 6) too). The nine has no open-sea square, so it never showed. The
  open seas also read as square-edged in the heights picture. Both are a ChunkGen sea-floor follow-up.
- **Wall rows** are the plan's `ice-wall` and `dune-sea`, 900 m bands shaped by the massif, coloured ice and sand in
  the picture; nothing else yet (the ragged fringe, ice cliff, beach and sandstorm are later, paper first).


## A test map for scale: the scale island (scale-12x10, u2, 2026-11-10)

Andrew (2026-11-09): "get a version of the 12 x 10 out so we can look for more things that don't scale with a large
world ... just the nine plus a bunch of rolling hill grass surrounded by water." A test map, not the real 12 x 10
(`world/maps/world-12x10/` is unchanged). The plan is `docs/design/scale-12x10/plan.png` (a pixel row of 24 per world
square; the plan's own sea column west and sea row south are outside the map).

- **`world/maps/scale-12x10/map.json`**: seed `valley-1`, `squares [12, 10]`, no wrap, bakeable, generator 8: 6912 x
  5760 cells, 108 x 90 = 9720 chunks. "The Scale Island".
- **The nine is pinned whole** (`"pinned": [{ "map": "nine", "square": [4, 3] }]`), a **pinned block** (new in the
  GLOSSARY): its nine world squares are held as the nine's own plan has them (templates, turns, bands; `PlanInputs.Held`)
  and its chunks, known chunks, spawns and far landmarks are the nine's own generator's, moved
  (`PinnedSquare.Inner`). So **every one of the middle 729 chunks is the nine's, bit for bit** (land, cells, things and
  biome; the nine's golden 0x69abb343 holds over them, `TheMiddleChunksAreTheNines`), the valley lies at square [4, 5],
  and the spawn is the nine's moved (2596, 3208). The land round the block meets its whole rim as round the valley
  (`MeetPinned` over a rectangle; the rim is read from the nine's 104 edge chunks once, at load). Only the hash of a
  chunk differs, since it has the chunk's coordinates in it.
- **Every other square is a forced square** (new in the GLOSSARY; `"forced"`: a picture of the map, a character a world
  square): open water round it all and in the nine's own bay west and south of it (the nine's west and south face the
  sea in its own plan), the band's filler elsewhere (grassland from a one-shape sketch over everything), and ten
  landmark squares laid out so no template stands within 3 squares of a copy, the nine's included (J-080 holds with no
  exemption; `NoTemplateStandsWithinThreeSquaresOfACopy`): five great trees (downs, meadow-cross), a volcano
  (ash-fields), three lake villages (lakeland, riverbend) and two sea spires (open-sea), with `"towns": 100` keeping every
  village. With Old Grumbler and the nine's own that's the far ring's landmarks; the plan's roads add outcrops (ruins,
  lone great trees, rock spires) and inns and shops on top. The envelope lowers the grass next to the sea to the low
  band, so most of the island is low rolling land with hills inland.
- **Plan golden 0xacc38348**, no problems and no forced misses; it solves in under a second. The nine's plan, the
  nine's chunks and the valley are unchanged (a world file without `forced` or a pinned block takes the old paths).

### Measured on the VM (2 threads, 2.8 GB)

The whole map's cells are more than this VM holds, so the costs come from a 6 x 6 copy of the same layout (the nine,
a ring of grass with a village, the sea round it; 2916 chunks), baked in a world folder of its own in
`ASmallCopyBakesAndMeetsTheSpawnsChecks`, and from making every chunk of the full map one at a time.

| What | The 6 x 6 copy (2916 chunks) | The full map, carried at the same rate (9720 chunks) |
|---|---|---|
| Making its chunks (the bake's work) | 28.7 s, 9.8 ms a chunk | 96 s; **measured: 93 s** for every chunk made (`ItsExpectsAreWhatItsChunksMake`) |
| Allocated by the bake | 27.8 GB (9.5 MB a chunk) | **90 GB** of garbage: the collector's work, not memory held |
| The process's peak working set | 966 MB | not carried (the bake keeps every chunk's arrays until it writes) |
| The baked files | 13 MB | 43 MB |
| **The World a chunk** (cells, a `Cell` an object, and land) | **288 KB**, over the nine's 729 chunks | **2.7 GB** for the World alone, before the view |
| Load (plan, the nine's plan, the block's rim) | 1.9 s | about the same: the plan solves in under a second |

These are rates carried from a smaller map, not the full map measured; the PC bakes and loads the full map next. What
looks not to scale, for the PC to confirm: the World's 288 KB a chunk (2.7 GB for 9720 chunks, all held at once
today), the bake's allocation (9.5 MB a chunk, 90 GB a bake), and the bake holding every chunk before it writes.
The far ring's landmarks repeat names across the map (two great trees are both "the Elder Oak"): its names come from
a small list.

### The bake that doesn't grow with the map (bake-lean, u2, 2026-11-10)

The bake now makes a row of chunks on every thread while the row before is written on one, and drops each row once
it's written (`GeneratedMap.Bake.cs`): `cells.txt`, `land.json` and `things.json` stream out a row at a time
(`LandFile.Writer`), and the water bodies for `places.json` are found from the land rows as they go (`WaterRuns`,
runs of water joined row to row) instead of reading `land.json` back and flood-filling a set of every cell. The files
are the same bytes (`BakeTests`: one thread, four, and the old whole-map writer kept as the reference). The garbage
was closures and small arrays on every land point: `BiomeTable.IndexOf`'s `FindIndex` lambda (3.7 GB on the nine),
`WorldBiomes.Between`'s delegate and its closure (2.2 GB), `Shore`'s two `int[4]` (340 KB a chunk), then the chunk's
working arrays (256 KB, now one set a thread).

| On the VM (2 threads) | Before | After |
|---|---|---|
| The nine (729 chunks): allocated a chunk | 8.9 MB | 48 KB |
| The 6 x 6 copy (2916 chunks): allocated a chunk | 9.5 MB (27.8 GB) | 41 KB (117 MB) |
| The 6 x 6 copy: the process's peak working set | 704 MB | 102 MB |
| The 6 x 6 copy: the bake's time | 25.7 to 27.4 s | 22.0 to 23.7 s |
| **The full map (9720 chunks)** | not bakeable here | **76 s, 31 KB a chunk, peak 120 MB** |

The full map's bake still stops at `places.json`: two villages are both "Dunmere", and a place's id may be listed
once (the names above, from a small list). It did before too, as the last step; its cells, land and things are
written by then.

## The sim across the seam (sim-across-the-seam, u2, 2026-11-11)

The World now wraps east to west when it's made so (`new World(rules, w, h, seed, fill, wrapX: true)`; `MapFile.WrapX` is
map.json's generated `"wrap": "ew"`, true for the 12 x 10). A world that wraps is a whole number of chunks wide (the 12 x
10 is 108). North and south stay walls. A world made without wrap reads, ticks, hashes and saves as it did: every
existing golden holds (the nine 0x7c681170, the valley, the cell hashes in `TerrainTests`/`ChemistryTests`), with all
1576 tests passing unchanged.

**One place folds.** `World.FoldX` (a cell x into [0, W)), `FoldChunkX` (a chunk x into [0, W / 64)) and `DeltaX` /
`DeltaChunkX` (the signed short way round, east positive, exactly half way counting east) are the seam; on a world
without wrap they return what they're given. Everything else goes through them:

- `InBounds` takes any x on a world that wraps (only y is checked), `Idx` folds x, `Locate` / `LocateActive` (so `At`,
  `IsActiveAt` and every reach for a cell) fold x, and `FindChunk` folds the chunk x, so `HasChunk`, `IsActive`,
  `SetActive`, `IsSettled`, `IsLiveAtEdge` and `SetDrawn` take a chunk past the seam as the one it folds to.
- **Chunk links** wrap: `AddChunk` links through `FindChunk`, so the far east column's `East` is the west column and
  back. The tick's neighbour reads (`Chunk.Next`: fire, water, lava, gas, wind, snow, liquids), still water's
  neighbour snapshot, catching up (`World.CatchUp`), unpacking round an activated chunk and `BesideActive` (packing,
  D17) all go by the links, so they cross with no change of their own.
- `ChunkCells` (the stamps' and the map's moved land, the bare cells) folds its chunk index on a world that wraps.
- `Neighbours` yields folded cells; a spell's shape cells (`ShapeCells`, `LineCells`, `VisitSlant`, `Jag`), a
  wave's front, a seek's pick and a step trigger's cell are folded; `Release` and `Land` fold where a cast lands, so a
  star, a whirlwind or a wave starts on the world's own cells.
- Anything that moves folds its new x (raiders shoved, hurled, whirled or walking a hold or defense path; whirlwinds;
  waves and boulders, whose line runs on unfolded from its origin while the cell it's on folds; the golf ball; a
  wizard's stand and a punch's knock-back), and distances between two things go the short way (`DeltaX`: a punch's
  target, a stand's step for triggers, a whirlwind's ring and eye, a blast's nearest-first order, a gas flash's shove,
  a wave's power falling off, golf's cup, the hold's reach).
- The planner and streaming measure round the seam: `ChunkStreaming.Plan` / `StartPlan` (`wrapChunks`, which the World
  overloads pass), `ChunkPlanner` (reach, keep, linger, and a live edge knocking the chunk over the seam active), and
  `ChunkStreaming.Within` for the view (`wrapCells`).
- `StandKeeper` takes the World's `DeltaX` (optional, x1 - x0 without), so a step over the seam is one step.

**The tick's order** doesn't move with the seam. Active chunks are visited as row segments in world row-major order by
their folded coordinates: chunk rows north to south, and in each row chunk x from 0 east to W / 64 - 1, whatever the
characters' places. A character standing on the seam has chunks 106, 107, 0, 1, 2 active, visited 0, 1, 2, 106, 107.
That's the order every peer uses (it's the World's, not the planner's), so lockstep holds; it isn't symmetric (a fire
running east over the seam is met in another order from one running west), which only matters to rules whose outcome
depends on the order inside a tick, as it always has away from the seam.

**Hashes, saves, late join.** A world without wrap hashes, saves and replays as before. A world that wraps hashes its
cells the same way (row-major over [0, W) x [0, H), the things cracked, then FitText) and ends with `~ew`, so it never
equals the same cells on a world that doesn't wrap; the rolling hash (`HashSlice` + `CloseHash`) agrees with it. Its
save has `"wrap": "ew"` and `World.Load` makes it wrap again. Tests (`SeamTests`, and
`LateJoinTests.PeersAndJoinersAgreeWithCharactersCrossingTheSeam`):

- A fire lit at x = W - 2 on a strip of grass round the seam burns east over it to x = 1; the same strip lit at x = 1
  burns west, and at every tick every cell matches its mirror (x to W - 1 - x). The catches are made certain by a mod
  (spread 1000, no smoke, heat or boiling) since the draw count follows the tick's row order, not the fire. The wrapped
  world's golden after 80 ticks: `2ae46b89`.
- Water poured east of the seam runs west over it in a channel, keeping as much as the same channel in the middle of a
  world without wrap; smoke along x = 0 drifts into the far east column; a Fireball on the seam burns both sides; a
  raider spawned at x = -1 is at W - 1 and a gust shoves it east over the seam.
- A character at x = 10 on a world 12 chunks round keeps chunks 10 and 11 active (reach 2), the same world without wrap
  doesn't; walking west the keep holds and the far side lets go; a fire on chunk 0's west edge knocks chunk 7 of 8
  active; the chunk over the seam from an active one stays unpacked, two away packs.
- Two players walking back and forth over the seam on a world 12 x 2 chunks round, stands sometimes given unfolded,
  step triggers casting bolts and meteors, a Fireball on the seam and the host's planner stamping chunk switches: a
  peer from the first tick and joiners at nine later ticks match the host part by part at every tick.

### The bounds checks, and what each became

Found with `>= W`, `< W`, `W - 1`, `% W`, `/ W`, `W * H`, `InBounds(`, `x < 0`, `Math.Max(0, x...)` and the like over
`Chemistry/` (the generator, plan and baked-map files are left out: generator 6 already wraps). By file:

| File | Checks | What it became |
|---|---|---|
| World.cs | `InBounds`, `Idx`, `Neighbours`, `LandUnitsAt`, `LiftAt`, `SetLand`'s rows and chunk loop (`W * H`, `ChunkOf(W - 1)`) | `InBounds` checks y only when wrapping; `Idx` and `Neighbours` fold; new `FoldX`, `FoldChunkX`, `DeltaX`, `DeltaChunkX`, `ChunkDelta`, `XSpan`, `WrapChunks`. The rest read through `InBounds` and the chunks, unchanged |
| World.Chunks.cs | `BuildChunks` (`ChunkOf(W - 1)`), `Locate`, `LocateActive`, `BuildSpans`' clip (`W - x0`), `CellAt(index)` (`W * H`, `% W`), `RowCells`, the hashes' `W * H` | `FindChunk` folds (so do the links); `Locate*` fold x; `SetActive` raises the chunk's own coordinates; the hash ends `~ew` when wrapping. The spans, `CellAt`, `RowCells` and the hash walk stay: they walk [0, W) |
| ChunkCells.cs | the chunk index | folds on a map that wraps |
| ChunkStreaming.cs | `Nearest`'s `Math.Abs(cx - ...)`, `Knocked`'s sides, `Within`'s distance | short way round (`ChunkDelta`, `FoldChunkX`, `wrapCells`) |
| World.Pack.cs | `UnpackChunk`'s and `ChunkReader.At`'s `InBounds`, `SetDrawn`'s keys | unchanged (they test the chunk's own cells, inside [0, W)); `SetDrawn` folds |
| World.Spells.cs | `ShapeCells`' `Add`, `Jag`, `LineCells`, `VisitSlant`, `Cast`'s and `MarkStar`'s `InBounds`, `Seek`, a whirlwind's moves and rings | shapes and seeks fold; `Release` and `Land` fold; whirlwind moves fold, rings and eye by `DeltaX`. `Cast` and `MarkStar` stay |
| World.Raiders.cs | `RaiderAt`, `SpawnRaider`'s `InBounds`, the shoves' and hurl's moves, `BlastRaiders`' nearest order | `RaiderAt` and spawn fold; moves fold; order by `DeltaX` |
| World.Strike.cs | `ApplyStrike`'s `InBounds`, the stand's x, the knock-back's way and steps, `Target`'s offsets | the stand folds, knock-back steps fold, ways and offsets by `DeltaX`; the `InBounds` stays |
| World.Triggers.cs | `Stepped`'s way, `ActionCell`'s `InBounds`, the around pick's `InBounds` | way by `DeltaX`; the action's cell folds; the pick stays |
| StandKeeper.cs | `Step`'s `s.X - r.X`, same-cell test | the World's `DeltaX`, given (optional) |
| World.Waves.cs, World.Boulders.cs | the mover's cell after a step; the wave front | fold |
| World.Golf.cs | `ApplyGolf`'s and the roll's `InBounds`, the ball's moves, the stroke's offset, the cup's reach | moves fold, offset and reach by `DeltaX`; the `InBounds` stay |
| World.Land.cs | stamps' boxes (`Math.Max(0, ..)` to `Math.Min(W - 1, ..)`, three), the land-face, drop, bank and stamped reads' `InBounds` | boxes walk `XSpan` (unclipped but at most W columns when wrapping) and yield folded cells; the reads stay (`ChunkCells` folds) |
| MapStamps.cs | `Bare`'s box; the trail's `InBounds` | `XSpan`; the trail stays |
| World.Place.cs | `PlaceOpen`'s `InBounds` and cell sets, `SummonAnimal` | fold (the area folds before the pick when wrapping) |
| Effects.cs | `ApplyEffect`'s `InBounds` | stays, and the effect's x folds |
| World.Machines.cs | `LeavesFrom`'s flood, the gate link's `InBounds`, `% W` decodes | the flood folds (a ring of pipe round the world ends); the rest stay (`Idx` folds) |
| World.Flash.cs, World.React.cs, World.Terraform.cs | flood fills by `Idx`, `% W` decodes, a wave's power by distance, `React`'s fire beside | the floods stay (`Idx` and `Neighbours` fold); a gas flash's shove and a wave's power by `DeltaX` |
| World.Dragon.cs | `StartCell(index)`'s `% W`, `InChunk`, the pass lengths (`W`, `H`), `BreathBox`' clip | `CaughtByBreath` folds its x; the rest stay: a pass still sweeps [0, W) once, which covers a world that wraps |
| World.Hold.cs | the site's box (`Math.Max(0, ..)`, `Math.Min(W - 1, ..)`), road `InBounds`, `Fly`, `InReach`, the path's moves | `Fly`, `InReach` and moves go round; the box stays (a site is laid inside [0, W)) |
| World.Hands.cs | `TowerGround`'s nearest order | folded cells, `DeltaX` order |
| World.Defense.cs | the field's box (`Math.Min(W - 1, ..)`), `InBounds`, a raider's move | the move folds; the box stays (a field is laid inside [0, W)) |
| World.Buildings.cs, World.Lay.cs, WorldBuildings.cs | `InBounds` and `Idx` round laid cells, `% W` decodes | unchanged: `Idx` folds, so a building laid over the seam lays on the world's cells |
| World.Weather.cs | random x in [0, W), the fog regions' last-column clip (`Math.Min(W, ..)`) | unchanged (deterministic; a fog patch spreads over the seam through the cells) |
| World.Night.cs | the light block | unchanged (reads through `Locate`, which folds) |
| World.Entities.cs, World.Acts.cs | `InBounds` | unchanged |
| World.Save.cs | the save's size | adds `"wrap": "ew"` only when wrapping |
| WorldPuzzles.cs, World.Course.cs | a puzzle's box, a course's zones | unchanged: they're laid inside [0, W) |

Left as they are on purpose: a defense field, a hold site, a course zone and a puzzle are rectangles inside [0, W) (the
12 x 10 lays none over the seam; one that wants to would need its box split); the dragon's passes run 0 to W along x.

### What the view needs (for the PC)

1. **Make the World wrap** (an hour): `ChemGrid.MapWorld` builds `new World(rules, source.W, source.H, seed)`; it wants
   `wrapX: MapFile.Load(Folders.World).WrapX`. The 12 x 10 is still `"bake": false`, so this lands with whatever loads
   it unbaked (the sparse World's generate switch).
2. **The stand keeper** (a line): `NetChem`'s `new StandKeeper<StrikeIntent>(..., HoldReads)` adds `grid.World.DeltaX`.
3. **The camera and the origin re-centre across the seam** (2-3 days, as scoped): the world is 6912 cells, 10 368 m,
   round. Keep the eye's and the wizard's positions unfolded and continuous in the view; re-centre the origin on the
   player (within about 5.2 km east-west, 4.3 km north-south on the 12 x 10's 6912 x 5760 cells); send the core any x
   (it folds) and read cells by any x (`At`, `IsActiveAt` fold). Chunks the view lays out go at the copy nearest the
   eye: `DeltaChunkX(eyeChunk, cx)` chunks from the eye's chunk, so chunk 107 draws just west of chunk 0.
4. **Terrains and dressing near the seam**: `ChemGrid.DetailChunks` passes `wrapCells: world.W` to
   `ChunkStreaming.Within` (grass and detail); `SetDrawn` folds, so the drawn hint can name chunks either side. Props and
   items crossing the seam: their cell is the folded one (`ThingSpawn` chunk and local cell), their transform the copy
   nearest the eye.
5. **The far ring and landmarks**: the core's `KnownRing` and `FarLandmark.DxTo` already measure round (world-12x10-plan);
   the view's `FarRing` must put each known mesh and landmark at the copy within half a world (± 10 368 m) of the eye,
   for up to the whole ring (9111 chunks shown when one character knows the whole 12 x 10), and move them when the eye
   crosses the seam or the origin re-centres.
6. **Anything the view measures between two places** (a wizard to a raider, the dragon's flight line drawn from
   `PassLine`, the breath box, a cast's aim) should take `DeltaX` near the seam; the dragon's breath box never spans it.
