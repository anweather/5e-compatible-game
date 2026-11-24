# The rolling hills (hills-12x10)

Andrew, 2026-11-24: "Get the sparse island with just rolling grassy hills working. Forget the valley in this one. Just
rolling hills with wraparound. No water. Just all rolling hills with grass. Get them working and stable. Then let's add
roads ... Then patches of forest ... then other biomes ... But make sure the first thing that we try to do is get the
rolling grass hills with nothing else in it stable at 60 FPS."

This is step 1: a generated map of nothing but grass on rolling hills, the Wide World's size, wrapping both ways (a
**torus**), played from its rows alone like the Wide World. Roads, forest and biomes come after it passes.

## The map

`world/maps/hills-12x10/map.json`: 12 x 10 world squares, 6912 x 5760 cells (108 x 90 chunks, about 10.4 x 8.6 km),
`"wrap": "torus"`, `"bake": false`, and generated `"hills"` in place of a plan:

```json
"hills": { "base": 8, "biome": "grassland", "octaves": [[1152, 200], [384, 96], [128, 24], [64, 6]] }
```

- **No plan, nothing pinned.** `GeneratedMap.Load` sees `"hills"` and makes a `ChunkGen` in hills mode
  (`ChunkGen.Hills.cs`): `Plan`, `PinnedMap` and `Field` are null, there are no landmarks, villages, rivers, spawns or
  things. Every cell is grass (`,`) on the hills' land but its roads' dirt (`.`); every known chunk is dry grassland.
- **The land** (`HillsLand.At`) is the base plus octaves of integer value noise: each octave a lattice of `period`
  cells whose points get a height in 0..amplitude land units (0.125 m) from the seed (`WorldHash.Hash4`), blended with
  smoothstep in Q16. Every period divides both 6912 and 5760, and the lattice is taken modulo its count, so the land at
  x and x + W (y and y + H) is the same and there is no seam in it. Integers only: every peer makes the same chunk.
  The land runs 41 to 305 units (5.1 to 38.1 m); the steepest step between neighbouring cells is 1 unit, so nowhere is
  steep enough to be bare dirt. Roads (below) level and bank it where they run. Golden: chunk (54, 45) hashes `4ceac909` (`HillsTests`).
- **Spawn** is cell (6880, 5601), on the main road 32 cells (48 m) from the east seam and 159 from the south one (it was
  (6880, 5728) before roads).
- Selectable with `-map hills-12x10`; the nine stays the default start map.

## The torus in the core

The World wrapped east-west already (`World.WrapX`, the Wide World; docs/design/world-12x8-scope.md, "The sim across the
seam"). A torus wraps north-south too: `new World(..., wrapX: true, wrapY: true)` (`MapFile.WrapY` is map.json's
`"wrap": "torus"`). Every place the seam work folded x now folds y the same way:

- `FoldY`, `FoldChunkY`, `DeltaY`, `DeltaChunkY`, `YSpan`, `WrapChunksY` beside their x twins. `InBounds` is true for
  any cell on a torus; `Idx`, `CellKey`, `Locate`, `FindChunk` (so the chunk links), `Neighbours` and `ChunkCells` fold y.
- Movers fold their new y where they folded x (raiders, golf ball, whirlwinds, waves, boulders, stands, knock-backs,
  steps, summons, effects, casts' shapes); distances that took `DeltaX` take `DeltaY` for y (strikes, flashes, waves'
  power, the hold, golf, the whirlwind's ring, a blast's order). `StandKeeper` takes `deltaY` too.
- Streaming and the planner measure round both seams (`ChunkStreaming.Plan/StartPlan/Within`, `ChunkPlanner`'s
  candidates and knocks), packing's distance (`AwayFrom`), the drawn hint and the source's make-ahead fold y.
- Stamps' boxes walk `YSpan`. The hash ends `~torus` (a torus never hashes as the same cells east-west only); a save
  says `"wrap": "torus"` and loads as one.
- `KnownRing` reaches over the south seam (`ChunkGen.WrapChunkY`, `ChunkDistance` on the torus).

Tests: `TorusTests` (folding, a fire burning south over the seam against its mirror, a Fireball on the corner landing on
all four sides, streaming round the corner, the hash and the save; golden `2bc9fa40`),
`LateJoinTests.PeersAndJoinersAgreeWithCharactersCrossingBothSeamsOfATorus` (two players walking over the corner and
the south seam with triggers and a Fireball, the host's planner switching chunks: a peer and joiners at ten ticks match
the host part by part), `HillsTests` (size and wrap, tiling, chunks and known chunks meeting over both seams, all grass,
expects, the golden, rows-only load and a walk over the east seam with nothing made on a read).

## Roads (step 2)

Andrew, 2026-11-24: roads "using the road layout strategy we talked about before", made in the generator: a chunk's
cells come out of `ChunkGen` with its roads already in, worked out from map data alone, so every peer, save, late joiner
and the changes hash agree with no shared state, and the cost follows the player.

**The strategy** is the world squares' (`world-squares.md` sections 2 and 5): the network is coarse and decided per
world square and per side when the generator is made; a chunk then draws only the roads that reach it. The hills have no
plan, landmarks or bands, so the "roads lead somewhere" part (routes between destinations, waystations, outcrops) waits
for villages; what's built is the layout itself: edge slots, hubs, main roads and lesser ones.

`map.json`'s `"hills"` takes `"roads": { "mainRows": [4, 9], "mainColumns": [5, 11], "loops": 12 }` (`HillsRoads`):

- **Main roads** run along whole rows and columns of world squares, so each goes once round the torus and over its seam
  (rows 4 and 9 over the east seam, columns 5 and 11 over the south seam). Where a main row meets a main column the hub
  is a four-way junction (four of them; the spawn's world square (11, 9) is one).
- **Lesser roads** join every other world square: a spanning tree over the squares' sides in a seeded order (Kruskal,
  the main roads joined first), then each side the tree didn't need is a road 12 times in 100 (loops, so not every
  lesser road is a dead end). 135 of the 240 sides carry a road, 44 of them main.
- **A side's road slot** is one of its 9 chunks, never a corner one (seeded per side, so both squares read the same
  one); the road crosses at the slot's middle. **A hub** is a seeded point at least 160 cells in from every side.
- **An arm** (one per road side of a square) is a cubic from the slot to the hub: it leaves the side square on (its
  first control point 160 cells straight in, so the road runs straight over the side from one square into the next),
  wanders up to 40 cells across by seeded noise that is nothing and level at both ends, and where only two arms meet
  comes into the hub along the line between their slots (140 cells), so a road through a square has no bend at its hub.
  32 pieces an arm, Q16 cells, integers only.
- **A cell** takes the nearest arm line within reach (arms of its world square and the ones round it, placed at the copy
  nearest it, so over a seam it reads the squares across it): nearer than half the road's width (3 cells for a main
  road, 2 for a lesser one: about 9 and 6 m wide) it's road, dirt, at the land of the line's nearest point (level
  across, following the hills along); within 6 more cells its land leans back from that to the hills' own (a cut on the
  uphill side, fill on the downhill). The road is a pure function of the cell, so it's the same in either chunk at an
  edge and over each seam. Known chunks sample the same land (the far ring's heights have the roads' grading; they
  don't paint roads).
- Nothing is laid at load or stored in the World: the World's land is the source's land, so `World.Save` diffs against
  it, the hash covers it, and no chunk is made because a road crosses it.

The main roads are 9.3 to 12.2 km once round (they wander); along them the land steps at most 1 unit (0.125 m) a cell,
the hills being gentle, so no switchbacks or deep cuts are needed here (a question for steeper biomes later).

Tests (`HillsRoadsTests`): the network reaches every world square and the main roads run round both seams; a side is the
same road from both squares and their arms meet square on; every cell along each main road's line is road, at most 1
unit a cell along; where a main road's line steps from one chunk into the next (582 edges, 4 of them seams) both cells
are road in their own chunks and the land steps at most 1, and every edge cell of both chunks is what the cell alone
works out; round the spawn road-to-road steps are at most 1 unit and anywhere 2; chunks made in reverse on many threads
by a second load hash the same, and a known chunk's samples are the made chunk's land; goldens for the spawn's chunk
(107, 87) `dfcd0703`, (0, 87) across the east seam `a9567428` and (102, 89) `451900bd` (the old golden (54, 45)
`4ceac909` has no road and is unchanged); the spawn is on a road; and two peers walking the main road over the east
seam, with fire bolts onto the road, plus a joiner replaying and a joiner loading the save at tick 300, agree chunk by
chunk, with fewer than 60 chunks made at load.

### Roads: results (2026-11-24, M2 Pro, development player, 1920 x 1080, `-fps-probe -perf-offscreen -perf-memory`)

Scripts and logs: `~/5e-logs/roads/` (`all.sh`, `table-r1.txt`, `r1-*.log`, `*.rss.csv`). Frame times in ms. The seam walk's
legs are `e2,s4,w2,n4` (the spike moved 127 cells north of the south seam); the road walk (`-perf-scenario roadwalk`,
`PerfStage.RoadWalk.cs`) follows the main road along the spawn's row from the spawn, over the east seam and on, a
waypoint at each point of its line: 3.0 km in 480 s, every frame on a road cell.

| Run | mean | p95 | p99 | worst | made on a read in the run | peak RSS |
|---|---|---|---|---|---|---|
| stand at the spawn, on the road x3 (30 s) | 9.98-9.99 | 12.01-12.13 | 12.5-12.6 | 32 (the 20 s frame, below) | 0 | 1.56 GB |
| seam walk x3 (150 s, 4 crossings each) | 9.76-9.78 | 12.02-12.07 | 12.7-12.9 | 40-43 (the 20 s frame) | 0 | 1.64 GB |
| road walk x3 (480 s, 3.0 km, over the east seam) | 10.01-10.02 | 12.23-12.29 | 13.0-13.2 | 41-43 (the 20 s frame) | 0 | 1.90 GB |
| bot walk seed 1 (not in the bar) | 9.76 | 19.72 | 44.4 | | 0 | 1.84 GB |

- 0 exceptions and 0 errors of ours in all 10 runs (the 6 Metal pipeline errors for URP's Particles/Lit are every map's).
  PLAY READY in 4.2-4.5 s every run; no stall.
- Chunks made on a read: the 41 at load, before the first frame, in every run; none during any walk. Making a chunk
  takes about twice what it did (load: 231 chunks in 490-660 ms, against 290-350 without roads), all on the worker.
- Frames over 30 ms: 1 a stand, 2 a seam walk, 4-5 a road walk. One in each is the 20 s frame below; the rest (31-39
  ms, 3-4 in 8 minutes of road) are a chunk crossing's activation (`ChemGrid.ChunkApply` and `Step` ~10-12 ms each),
  which the hills had before roads too (its long walk: 41, 31, 26, 26). None is at a seam: the crossings' worst frames
  are 16-26 ms but the first east one, which is the 20 s frame.
- The bot's p95 misses as before (its fire's GPU cost and its own screenshots; tracked separately).

**The 42 ms frame after the first east crossing isn't the seam.** It comes about 19.7 s after launch in every run,
whatever the wizard does: standing still at the spawn (19.7 s), walking north first with no crossing (19.7 s), and on
the valley map (34.9 ms at 20.4 s). The seam walk only crossed the east seam at 19.4 s, so it fell 0.35 s after it.
What it is not: a garbage collection (with `-perf-gc-off`, no collection in the run, it's still 44 ms; the collections
the window lines count sit next to it by chance), a pipeline state made on first sight (a `-pso-trace` run makes none
near it), the terrains, grass, far ring, wizard, shadows or post-processing (each turned off: still 35-45 ms). In a
profile of the player the main thread spends it in the Mac player's `TimerTick` outside any script or render marker,
the render thread idle. That frame has no `TimeUpdate.WaitForLastPresentationAndUpdateTime` of its own, where every
other frame has one, and a `sample` of the process shows that wait (`GfxDeviceMetal::WaitForLastPresentationAndGetTimestamp`)
running under `TimerTick`: so it looks like one long wait for the display to present the last frame, from the OS or the
Metal driver, once per launch (inferred: the sample can't single out that frame). Not fixed: the next step is an Instruments Metal System Trace over 18-22
s after launch. It isn't the hills' or the roads': every map has it, once.

**The 30-minute walk** (`mem30`: the road walk for 1800 s, 11.4 km along the main road, 905 chunks held by the source
at the end, 139 made again after it let them go; mean 9.84, p95 12.16, p99 12.97, 0 exceptions, nothing made on a
read). RSS and the managed heap (Mono's used and held) by minute, MB:

| minute | 1 | 3 | 5 | 7 | 9 | 11 | 13 | 15 | 17 | 19 | 21 | 23 | 25 | 27 | 30 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| RSS | 1581 | 1674 | 1721 | 1877 | 1888 | 1949 | 1955 | 1970 | 1965 | 1951 | 1973 | 1976 | 1976 | 1975 | 2015 |
| managed used | 361 | 421 | 570 | 536 | 668 | 534 | 626 | 577 | 672 | 786 | 591 | 554 | 633 | 721 | 634 |
| managed held | 456 | 558 | 574 | 727 | 727 | 791 | 791 | 807 | 807 | 786 | 792 | 790 | 784 | 784 | 823 |

The managed heap levels off: what the collector holds grows to about 790 MB by minute 11 and stays at 740-823 MB to the
end, and what's live after each collection (the troughs of "used": 536, 534, 577, 569, 554, 573) is flat at about 550
MB from minute 7. RSS climbs about 40 MB over the last 15 minutes (1975 to 2015), with the held heap's last step; not a
leak worth chasing tonight, but the next long walk should go 60 minutes to see it stop. Its worst frame was 52.9 ms at
27 minutes (a render: `CullScriptable` 23.7 ms in one frame), its second the 20 s frame (48.7 ms).

Stills (800 px): `~/5e-logs/roads/shots/spawn.jpg` (on the main road at the spawn, looking east to the seam),
`junction.jpg` (the four-way hub of world square (11, 9) from 50 m west), `seam.jpg` (6 cells past the east seam on the
main road, looking back west over it) and `high.jpg` (60 m up over the junction, looking north). The road reads as a
winding dirt road, level across; past the known ring's edge (about 400 m) it stops, as the far ring doesn't paint roads.

New perf knobs on the way: `-perf-cell X,Y` and `-perf-look X,Y` (stand on a cell, look toward another),
`-perf-eye-up M` (the camera M metres up), `-perf-gc-off`, `-perf-gc-slice-ms N`; the window lines now say the managed
heap (`mono_used_mb`, `mono_heap_mb`).

## The view across the seams

`ViewWrap` (Scripts/ViewWrap.cs): the core folds every cell, and the view draws each thing at the copy of its place
nearest the camera. The wizard and the camera walk on in the view's own unfolded coordinates; data stays keyed by folded
cells and chunks, and only positions are moved by whole periods (W x 1.5 m, H x 1.5 m):

- `ChemGrid.CellCenter` returns the copy nearest the camera, and `ChemGrid.CellAt` folds, so everything the view puts at
  a cell (cell looks, fire, water, props) goes near the camera, and the cells sent to the core are the world's own.
- `MapTerrains` keeps its terrains by folded chunk (focus, wanted, distances on the torus) and stands each at the copy of
  its corner nearest the camera (`Corner`), moving any whose copy changed (`Replace`, only after half a world). The
  chunks it warms for a worker run over the seam to those they fold to.
- `ValleyGround.GroundAt` folds the cell under a point; `GroundFromLand` folds the land and paint it reads past the
  edge (it clamped), and `CellHeightfield` folds the cells its samples read, so a terrain's edge over the seam meets the
  next one's.
- The grass's detail chunks (`ChemGrid.Detail`) run over the seam and fold.
- `FarRing` measures to the characters on the torus, places each known mesh at its nearest copy (moving them as the
  characters move), and on a torus draws no sea to the horizon (there's no edge to see it past).

Float precision: positions grow as the wizard walks on round the world (10 km is 1 mm steps in a float), so a floating
origin isn't needed for a session; one re-centre would be the next step if long walks show jitter.

## Fixes the first runs found

- The hills didn't count as generated (`PinnedValley.InGenerated` read the pinned map), so there was no far ring and the
  cell drawing's radius was the whole map: 7,000 chunks unpacked standing still, 6 GB. A hills map is generated now.
- A batch of terrain heights was worked out in one box round them all (`MapTerrains.PrepareHeights`,
  `CellHeightfield.Prepare`): terrains either side of a seam are a map's width apart by their folded chunks, so the box
  ran the whole width (or height) and made 1,400 chunks on the main thread at the corner, 150 to 300 ms each time a step
  crossed a chunk near a seam. Boxes now hold only terrains near each other, and the core never boxes over a seam.
- A cell chunk the drawing came to before its source made it was made on the main thread (`ChemGrid.DrawSome`): now it
  and the next few are made ahead on the worker and drawn the frame after.
- `map.json`'s `"off": ["fauna"]` turns the animals off on this map only (`MapFile.Off`, `ValleyFauna`); 0.6 ms a frame.

## Results (2026-11-24, M2 Pro, development player, 1920 x 1080, `-fps-probe -perf-offscreen -perf-memory`)

Scripts and logs: `~/5e-logs/hills/` (`all.sh`, `table-r2.txt`, `r2-*.log`, `*.rss.csv`). Frame times in ms.

| Run | mean | p95 | p99 | worst | made on a read in the run | peak RSS |
|---|---|---|---|---|---|---|
| stand at the spawn x3 (30 s) | 9.46-9.50 | 11.41-11.49 | 12.0-12.2 | 38-40 (first frame) | 0 | 1.56 GB |
| seam walk x3 (150 s, legs of 2 chunks round the corner: 6 crossings each) | 9.71-9.72 | 11.90-12.03 | 12.6-12.7 | 42 | 0 | 1.63 GB |
| bot walk seed 1 / 2 / 3 | 9.44 / 8.25 / 8.72 | **18.88** / 14.48 / **16.10** | 39.7 / 21.7 / 36.5 | 64 | 0 | 1.86-1.93 GB |
| long walk (660 s, both seams twice) | 9.55 | 11.79 | 12.59 | 54 | 0 | 1.83 GB |

- 0 exceptions and 0 errors of ours in every run (6 Metal pipeline errors for URP's Particles/Lit are in every map's
  log, the nine's too). PLAY READY in 4.1-4.4 s in all 13 runs; no stall.
- Chunks made: 231 at load (41 of them on a read, before the first frame), ~411 by the time a stand starts, then only
  ahead on the worker: none on a read during any walk.
- Worst frames near a crossing (within 3 s, 3 seam walks + the long walk): the first east crossing 41-43 ms every run
  (+0.35 s after it); every other crossing 12-22 ms. The run means near crossings are 9.4-10.1 ms.
- Memory over the long walk (RSS a minute): 1569, 1593, 1653, 1681, 1681, 1735, 1773, 1771, 1709, 1746, 1797 MB. Native
  and graphics memory are flat; the managed heap rose 346 to 620 MB and drops back at times (a collection), so it isn't
  shown flat yet. The Wide World's stand peaked at ~2.2 GB. Not compared with a smaller hills map.
- The bot's p95 misses on seeds 1 and 3: its slow frames are where it burns (184 cells alight), waiting on the GPU
  (gpu p95 34 ms) and its own screenshots (`PlaytestBot.Shot`, the 60-110 ms spikes). The nine's bot on the same Mac is
  15.8 / 28.6 (mean / p95). It's the fire's look, not the hills.

Stills (800 px): `~/5e-logs/hills/shots/spawn.jpg`, `seam.jpg` (past both seams at the corner), `high.jpg` (60 m up).

## Left before forest

- The 20 s frame (above): every map's, once a launch; an Instruments trace to name it.
- Roads past the known ring: the far ring's meshes don't paint them, so a road stops at about 400 m. A road colour in
  the known chunks' samples would carry it to the haze.
- A road walk of 60 minutes to see RSS stop (it leveled for the managed heap).
- Chunk making costs about twice what it did with roads (2.4 ms a chunk, all on the worker): fine at walking pace;
  `HillsRoads.Near` could keep arms per chunk if a faster pace needs it.
- The fire's GPU cost (the bot's p95), shared with every map.
- `ChemGrid.Maps()` still hides `"bake": false` maps from the menu; `-map hills-12x10` plays it.
- For Andrew: roads are dirt, 9 m (main) and 6 m (lesser) wide, levelled across with 9 m banks; the hills are gentle
  enough (1 unit a cell at most along any road) that no cut, switchback or bridge is needed. Steeper land (the
  mountains, step 4) will need a grade limit along a road, a cut through the crest or switchbacks: a choice then.
