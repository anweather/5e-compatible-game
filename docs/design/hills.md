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
  (`ChunkGen.Hills.cs`): `Plan`, `PinnedMap` and `Field` are null, there are no landmarks, villages, roads, rivers,
  spawns or things. Every cell is grass (`,`) on the hills' land; every known chunk is dry grassland.
- **The land** (`HillsLand.At`) is the base plus octaves of integer value noise: each octave a lattice of `period`
  cells whose points get a height in 0..amplitude land units (0.125 m) from the seed (`WorldHash.Hash4`), blended with
  smoothstep in Q16. Every period divides both 6912 and 5760, and the lattice is taken modulo its count, so the land at
  x and x + W (y and y + H) is the same and there is no seam in it. Integers only: every peer makes the same chunk.
  The land runs 41 to 305 units (5.1 to 38.1 m); the steepest step between neighbouring cells is 1 unit, so nowhere is
  steep enough to be bare dirt. Golden: chunk (54, 45) hashes `4ceac909` (`HillsTests`).
- **Spawn** is cell (6880, 5728), 32 cells (48 m) from the east and the south seams, so a short walk crosses both.
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

## Left before roads

- The first east crossing's 42 ms frame (once a run, 0.35 s after it).
- The managed heap's slow rise over a long walk: walk 30 minutes and see whether it levels.
- The fire's GPU cost (the bot's p95), shared with every map.
- From 60 m the land is mostly haze past the known ring (672 m); the hills are gentle (5-38 m), so they read as nearly
  flat from the ground. Stronger octaves are a map.json change.
- `ChemGrid.Maps()` still hides `"bake": false` maps from the menu; `-map hills-12x10` plays it.
