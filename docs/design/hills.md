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

## Results

(Filled in from the player runs below.)
