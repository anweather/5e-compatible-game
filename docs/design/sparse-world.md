# The sparse World (#204)

2026-11-10, u2. The plan for the World to hold `Cell` objects only for the chunks near the players, and the
census of who reads cells today. The ADR is D17 (`docs/adr/0017-the-sparse-world.md`, proposed). The rule it serves
is J-086: cost follows the active region and the drawn tier, never the map's size.

## What's true now

The World builds a `Cell` object for every cell of the map (`World` ctor `Cells = new Cell[w * h]`, then
`BuildChunks` fills each chunk's array with the same objects). The scale probe (`docs/perf/2026-11-10-scale.md`,
on `scale-12x10`) measured **356 KB a chunk** at every size: 254 MB on the nine (729 chunks), **3.4 GB** on the
scale island (9,720 chunks). The scale island can't load on the PC until this changes.

Where the 356 KB goes, a cell at a time (87 bytes):

| | Bytes |
|---|---|
| The `Cell` object: header, `T`, `Rare`, `Def` (references), `Water`, `Liquid`, `Fire`, `Height`, `DefRules`, `Frozen` | ~64 |
| Its slot in `Chunk.cells` and again in `World.Cells` | 16 |
| `Chunk.FireGeneration` (an int) and `Chunk.Marks` (a byte) | 5 |
| `World.land` (an int a cell, `SetLand`) | 4 |
| `Rare`, for the few cells with a cover, gas, charge, timer... | ~80 each, shared out |

The still-water snapshots (`Chunk.Snap*`, about 18 bytes a cell) come on top once a chunk's water pass has run.

## The rule this keeps

**How a chunk holds its cells never shows.** Every accessor returns the same cell state whether the chunk is
packed or unpacked (below). That state goes into the hash, saves, ticks and the view. Packing a chunk and unpacking
it again gives back exactly what was there. So when a chunk packs or unpacks changes memory and time only, never
shared state, and peers needn't agree on it. To make it easy to check anyway, the core decides packing at tick
boundaries from tick-driven facts (which chunks are active, how many ticks since a chunk was last unpacked), so two
peers pack the same chunks at the same ticks.

## Words

- An **unpacked chunk** holds its cells as `Cell` objects, as every chunk does today.
- A **packed chunk** holds only its **source** (where its cells come from before play) and its **changes** (the
  cells that differ from the source).
- **Unpacking** makes the `Cell` objects from the source and the changes. **Packing** takes the changes out again
  and drops the objects.
- An active chunk is always unpacked. An inactive chunk is either.

These go in `docs/GLOSSARY.md` with this note.

## The choice: the source plus the changes

Three ways to hold an inactive chunk were on the table:

| | Memory at 9,720 chunks | Unpack | What it needs |
|---|---|---|---|
| **A. Packed arrays a chunk**: every cell's fields in a struct of arrays (terrain and cover as small ids, water, height, fire, the rare fields to one side) | 9,720 x 4,096 x ~12 B = **~480 MB**, growing with the map | copy arrays into objects | a packed layout for every field, kept in step with `Cell` |
| **B. The bake's rows, read back on activation** | only the bake, but a chunk that play changed has nowhere to go | parse the rows | B alone loses every change when a chunk deactivates |
| **C. The source plus the changes** (chosen): untouched cells come from the chunk's source (for a baked map that's B's rows), changed ones from a short list | source ~20-120 MB, changes a few MB, flat in the map's size apart from the source | make each cell from the source, then lay the changes over | a core source for real maps (below) |

C is what the save already does. `World.Save` writes, for each chunk, the cells that differ from an
`IChunkSource` (`ChangedCells`, `SaveCell`). `World.Load` makes every cell from the source and lays the saved
cells over it. A packed chunk is a save record kept in memory. A is a fallback if a source turns out too slow to
read cell by cell (it isn't: a baked source is an array lookup and a legend table).

Regenerating from the seed (`ChunkGen.Make`) as the source isn't an option on its own. It takes milliseconds and
9.5 MB of scratch a chunk (the scale doc's #6), far too slow for a per-cell read. So the generated maps' source is
their bake. ChunkGen stays the bake's job.

## A chunk, unpacked and packed

**Unpacked** (as today, plus a pool): its `Cell[4096]` and the per-cell arrays that only matter while it's live
(`FireGeneration`, `Marks`, the `Snap*` arrays) form one **block** taken from a pool on the World. The pool holds
the most blocks ever in use at once (the active set plus the drawn tier, ~100-200), so unpacking reuses objects
and doesn't make garbage.

**Packed**:
- The chunk's header stays: coordinates, neighbours, `Active`, `LastLive`, `EdgeLive`, `StoppedAt`, `CaughtUp`,
  `Changed`, `Fit`.
- Its changes are a list sorted by local index. Each entry holds every field `SameCell` compares, with strings kept
  as the rules' interned ids. That's about 40 bytes a changed cell, or 160 KB for a fully worked-over chunk.
- Its fire generations that aren't 0 go in a short list (the save's `fireGen`).
- Its `Marks` are dropped (they're cleared every tick) and its water snapshot is invalidated (`SnapValid = false`,
  as activation already does).

**Unpacking** happens between ticks, or on a read from the view:
- Take a block, then make each of its cells from the source with `Cell.Reset` and `source.Make` (as `MakeFrom`).
- Lay the changes over (as `LoadCell`), and the fire generations.
- Then, only if the chunk is activating, `MakeFit` and `Resume`, as `SetActive` does today. Unpacking for any other
  reason never makes a chunk fit (see "Fit numbers").

**Packing** happens between ticks, at the end of `StepSome`:
- Only inactive chunks that have been unpacked longer than a hold time (a few hundred ticks) are packed, at most a
  few a tick, oldest first, by row then column.
- Packing diffs each cell against the source (as `ChangedCells`), keeps the changes list and returns the block to
  the pool.
- A chunk the view is drawing stays unpacked: the view tells the core which chunks it draws with a command-free
  hint. That changes memory only, so peers needn't agree on it.

**What unpacks a packed chunk:**
- Activating it.
- Any write to its cells: map building (`Paint`, `SetLand`, the lays), a rule switch's `LayBuildings` / `LayCells`,
  the dragon's `Freshen` and `SnuffFires`, the lapse's `Sweep`.
- A read through `World.At` / `CellAt` / `AllCells`, which return `Cell` objects.

The hash and the save read a packed chunk without unpacking it (below).

## The source for the real maps

Today only the MCP stand-in valley (`StandInValley`) implements `IChunkSource`. The game builds its World in the
view, in `ChemGrid.MapWorld`:
1. It paints every cell from the map's rows (`PaintCell`, a legend character to a cell).
2. Then it lays the puzzles, the stamps, the rows, golf and the Hold.
3. A generated map's rows are its bake's `cells.txt`, and its heights are `land.json`.

So step 2 needs:
- **A core source for a baked map** (`BakedSource : IChunkSource`). It reads the bake's rows and land a chunk at a
  time, so the bake is held packed too: a chunk's legend bytes and heights, or a single value for a uniform chunk
  (sea, grass). The legend-to-cell table moves from `ChemGrid.PaintCell` into the core.
- **The built world as the baseline.** The puzzles, stamps, rows, golf and Hold write cells after the source.
  Those cells are simply changes against the source: they're packed with the chunk like any other.
  A village is a few hundred changed cells in the chunks it stands on.

## The hash

- `CellHash`, `HashSlice` and `CellText` keep their bits: the goldens don't move.
  - Step 1 already walks them through the chunks in row order (`RowCells`), finding each chunk once for each row of
    it crossed.
  - In step 2 the walk reads a packed chunk's cell straight from its source and changes, into one scratch `Cell`
    the walk owns: no unpacking and no allocation.
  - `LiquidsIn(chunk, cell)` already takes the chunk, so a packed chunk's fit number still picks its liquids.
- `RollingHash`'s cost a tick stays bounded: a window's length grows with the map, never a tick's slice.
  Reading a packed cell costs more than reading an object. On the island the slice per tick is the same
  `CellsPerWindow / Every` cells, so a tick's hash cost rises by the decode, not the map.
- `ChunkHash`, the cracked things and `FitText` don't change.
- **Not in this plan:** a hash cached per chunk (RollingHash's header, "After"). Every write would have to mark its
  chunk, and the hash would then be defined per chunk, which moves every golden. It's a separate decision.

## Saves

- A packed chunk's save record is its changes list, already made: saving no longer diffs every cell of the map
  against the source. An unpacked chunk is diffed as today.
- `SaveFormat` and the save golden (`153e8400`) don't change.
- Loading lays a save's chunks straight in as packed chunks: an inactive chunk isn't unpacked until something needs
  it.

## Late join

- Today a late joiner replays the whole history from tick 0 (`NetChem`). Replay runs the same commands, so it
  activates, deactivates and writes the same chunks: the joiner's World ends in the same state, whatever it packed
  along the way. Nothing changes.
- The planned island snapshots (WORLD_STREAMING.md, "Joining an island") are save records: a packed chunk's changes
  are what the host would send.

## Fit numbers (#190, `World.Fit.cs`)

- A packed chunk keeps its `Fit`.
- A rule switch that unfits cells raises the world's fit number. As today, an inactive chunk, packed or not, is
  made to fit only when it activates: `MakeFit` runs after unpacking, then `Resume`.
- Unpacking for a read or a write never makes a chunk fit. Fit is shared state (in `FitText` and saves), so it must
  follow the active set, not the storage.
- The hash reads a packed chunk's cells as they are, unfit, with `LiquidsOf(chunk)`, as it does for an unpacked
  unfit chunk today.

## The view

**The known tier** (the far ring) reads the bake through `KnownRing` / `ChunkGen.Know`, not the World. It doesn't
change.

**The drawn tier** reads cells through `World.At` and, today, `World.Cells`, which keeps its chunks unpacked:
- With `FarRing.SetView(drawn, known)` at 2/4/7, that's about 81-121 chunks, ~30-45 MB, the same on every map.
- The view tells the core which chunks it draws (the hint above), so they aren't packed under it.
- Drawn chunks are unpacked by those reads, not activated.

**`World.Cells` itself goes in step 2.** Step 1b moved the view's 57 reads of it (below): a read by key became
`CellAt(key)`, a pass over the active chunks reads `At(x, y)` chunk by chunk (`FireLightPool`), and a probe's count
became `AllCells()`. Nothing under `Scripts/` reads `World.Cells` now. Drawing whole-map passes would unpack the
whole map, so those are the ones that matter.

**The whole-map walks left after 1b**, each on the valley only (a generated map, `ValleyGround.Lazy`, already
works by chunk), and what each needs under step 2:
- `ValleyGround.Bind` (the valley's eager path): every cell through `At` into `cellBase` / `bakedPaint`. Under
  step 2 it unpacks every chunk once at bind, ~30 MB on the valley; it should take the lazy path's per-chunk
  `ReadCells` as each chunk's terrain is made (#4, #5).
- `ValleyChemistry.CountBuildingWalls` (not lazy): every cell's `T` through `At` for the scene's cabin walls.
  Under step 2: the chunks round the houses, as the lazy path does (`ChunksNearHouses`).
- `ValleyChemistry` start trees (not lazy): `StartTrees.Read` over every `ChunkCoords`. Under step 2: on the drawn
  chunks as they're made, as the lazy path does on `ChunkSwitched`.
- The probes, perf, showcase and `DragonViewTests` (17 files): counts and sweeps over `AllCells()`, by index with
  `CellAt`. Under step 2 they unpack the world they run on, fine on the yard and the valley; a probe on a big map
  should count over `ActiveChunks` instead.

## Threading

- The World is read and written on the main thread only. Unpacking and packing happen there too, between ticks or
  on a main-thread read.
- `FindChunk`'s last-chunk cache isn't safe across threads, and nothing reads the World from another thread today:
  - `CellHeightfield`'s `Parallel.For` reads its own cache, made before it runs.
  - `KnownRing`'s `Task.Run` reads generated chunks, not the World.
  - `GeneratedMap`'s `Parallel.For` makes the bake.
- A later change could move a pack's diff to a worker, because the source is read-only and the chunk's cells could
  be copied first. It isn't needed for the budget.

## The memory budget at 9,720 chunks

| | Today | Sparse (steps 2-3) |
|---|---|---|
| Cell objects | 3.4 GB (every chunk) | active 25-49 + drawn ~81-121 + packing slack ~32: **≤ 200 blocks x 356 KB ≈ 70 MB** |
| Chunk headers (packed) | (in the above) | ~1 KB x 9,720 ≈ **10 MB** |
| The source (the bake, packed by chunk) | (the view holds the map text while building) | **20-120 MB**: 4,096 legend bytes + 4,096 short heights is 12 KB a chunk, 117 MB raw; sea and grass chunks that are one value are a few bytes |
| Changes | none (in the objects) | ~40 B a changed cell: a village chunk ~15 KB, a burnt-out chunk ≤ 160 KB; **a few MB** for a long game |
| `World.land`, `startAt`, `landMoved` / `landLaid` / `landCrag`, `bareCells` (core arrays the size of the map) | 160 MB each for the int ones, 40 MB the bool | per chunk with the block, or the changes only (step 3): **~0** for packed chunks |
| **Total** | **3.4 GB + up to ~0.8 GB of map-sized arrays** | **~100-210 MB**, of which only the source (~2-12 KB a chunk) grows with the map |

The nine comes down from 254 MB to about 80 MB (the same ~70 MB of blocks, plus a 2-9 MB source).

The core's map-sized arrays besides `Cells` (step 3):
- `World.land` (`SetLand`, an int a cell).
- `World.startAt` (`KeepStart`, an int a cell).
- `landMoved` / `landLaid` / `landCrag` (made when land is first shaped).
- `bareCells` (a bool a cell).

The view's map-sized arrays are the PC's, and listed in the scale doc's #5 and #7: `ChemGrid` `views`,
`CellFxTexture`, `LookScopes.remarking`, `ValleyChemistry.startTrees`, and `ValleyGround`'s `cellBase` /
`bakedPaint` / `paintedMix`.

## Step 1: the accessor move (this branch)

No behaviour changes: the goldens are unchanged, and the tick allocates nothing new
(`ChunkTests.AHashSliceAllocatesNothing`).

**New accessors:**
- `World.CellAt(index)`: a cell by world index, through its chunk; null off the world.
- `World.AllCells()`: every cell in row order, through the chunks. It allocates its enumerator, so it isn't for the
  tick.
- `World.RowCells` (private struct): an allocation-free walk of an index range in row order, finding each chunk once
  for each row of it crossed. `CellHash`, `HashSlice`, `CellText` and `KeepStart` use it.
- `World.LiquidsIn(chunk, cell)`: replaces `LiquidsAt(index, cell)`, which looked the chunk up again for each cell.
- `Chunk.CellAt(i)` / `Chunk.Put(i, c)`: `Chunk.Cells` is now a private `cells`.
- `Chunk.ActiveCells`: the array, for the tick's hot passes over an active chunk only.

**The callers, as the compiler counts them** (`[Obsolete]` on each in turn, a full `dotnet build core`):

| | Callers | Moved to |
|---|---|---|
| `World.Cells`, core code | 32 | |
| ... the hash and the chunk store (`World.Chunks.cs`) | 11 | `RowCells`, `Chunk.Put` (2 left: the array's construction, for the view) |
| ... `Mcp/Simulator.cs` | 8 | `AllCells`, `CellAt` |
| ... `World.Dragon.cs` (`KeepStart`, `SnuffFires`) | 3 | `RowCells`, `AllCells` |
| ... `World.Acts`, `Lay`, `Buildings`, `Terraform` (by index) | 8 | `CellAt` |
| ... `World.Lapse.cs` (`Retire`'s sweep) | 1 | `AllCells` |
| ... `World.cs` (the construction) | 1 | stays until step 2 |
| `World.Cells`, core tests (`Chemistry/Tests`, `core/Rules.Mcp.Tests`), 23 files | 129 | `AllCells` (counts, sums, `Where`), `CellAt` (by index), `W * H` (`Length`) |
| `Chunk.Cells` | 29 | |
| ... the tick's passes over active chunks: `World.Sim` 10, `Weather` 5, `Machines` 2, `StillWater` 2 | 19 | `ActiveCells` |
| ... any chunk: `Chunk.Next`, `Locate`, `ChunkHash`, `MakeFit`, `Freshen`, `Recount`, the save's three | 10 | `Chunk.CellAt` / `Put` |

After this branch the only reads of `World.Cells` in the core are the two lines that build it.

**The view's callers (`Scripts/`, moved in step 1b; the line numbers are from before it).** 57 reads of `World.Cells` in 29 files, plus about
480 calls of `World.At`.

The game's own (12 files):

| Where | What it reads | What it needs |
|---|---|---|
| `ValleyChemistry.cs:260` `Bind` | every cell's `T`: `startTrees` (a bool a cell), building walls | the trees and walls of a chunk when its terrain is made: per chunk, drawn tier (scale doc #5) |
| `ValleyGround.cs:199` bind | every cell for `CellPaint.Mix` into `cellBase` / `bakedPaint` | per chunk as it's drawn (#4, #5) |
| `CellSlickMesh.cs:282` `RescanGlow` | every cell, for glow | the drawn chunks only (#8, #210 started this) |
| `ChemGrid.LookScopes.cs:163-165` `Remark` | a cell by index; `remarking` a bool a cell | `CellAt(index)`; `remarking` per drawn chunk (#7) |
| `ChemGrid.Visuals.cs:547, 687` redress, `RenderCell` | a cell by index (drawn cells) | `CellAt(index)` |
| `ChemGrid.LookFire.cs:133`, `ChemGrid.Acid.cs:40`, `ChemGrid.Ice.cs:244` | a cell by index (cells with a look) | `CellAt(index)` |
| `CellWaterMesh.cs:130, 151, 504, 558, 561, 838, 1004` | a cell by key (wet cells it draws) | `CellAt(key)` |
| `FireLightPool.cs:88` | the array, read over the active chunks only | `At` / `CellAt` (already active-only) |
| `WindowPanes.cs:195` | a cell by index (a window's cells) | `CellAt(index)` |
| `ValleyChemistry.Roofs.cs:81` | a building's cells by index | `CellAt(index)` |

Probes, perf and showcase (17 files: `ProbeProductsView` 9, `ProbeGlowMoss` 5, `ProbeFogCost` 3, `ProbeSnow`,
`ProbeSlant`, `ProbeFlames`, `PerfScenario` and `DragonViewTests` 2 each, `ProbeSoak`, `ProbeScenarios`,
`ProbeScenario`, `ProbeMapSwitch`, `ProbeFeel`, `ProbeDragon`, `ProbeAct`, `FpsProbe`, `ShowcaseScript` 1 each).
These count or change cells over the whole world: `AllCells()` and `CellAt(index)` do, mechanically. On a packed
world, `AllCells()` unpacks every chunk, which is fine on the yard and the valley. A probe on the island should count
over the active chunks instead.

## The steps

| Step | What | Who | Size |
|---|---|---|---|
| 1 | This note, D17 proposed, the core's callers through accessors | u2 (this branch) | S, done |
| 1b | The view's 57 `World.Cells` reads to `CellAt` / `AllCells` / per-chunk passes (the table above); the whole-map binds wait for #205 | the PC | S for the by-index reads, M with the binds |
| 2 | Packed chunks in the core: the block pool, pack and unpack, the hold time and the view's draw hint, the hash and save reading packed chunks, `BakedSource` with the legend table moved into the core, loading saves packed; `World.Cells` dropped once 1b lands. Goldens unchanged; tests that pack and unpack mid-fire, mid-flow and across a rule switch, and that a peer packing differently hashes the same | u2 | L (about a week) |
| 3 | The core's other map-sized arrays per chunk: `land`, `startAt`, `landMoved` / `landLaid` / `landCrag`, `bareCells` | u2 | M |
| 4 | The view's map-sized arrays and binds per drawn chunk (scale doc #3-#5, #7), with #205's terrains for the drawn tier | the PC | L (#205) + M |
| 5 | The scale island loads in a player: the probe's scenarios on it, `PERF MEMORY` against this budget | the PC | S |

Steps 2 and 3 can go in either order. The island needs 2, 3 and 4 before it can load.

## Measured (`sparse-world-bench`, 2026-11-10)

u2, before step 2, so the lead can read the numbers before D17 is decided. The questions were what the hash and the
save cost reading packed cells, what packing and unpacking cost (five chunks at once when a character crosses a chunk
edge), how big the changes get, whether option A is needed as a fallback, and what the hold time should be.

**What was measured.**
- **The prototype** is `World.Pack.cs`. It is not wired in and behind no switch: nothing in the game packs a chunk.
  - `Pack` diffs an inactive chunk against its source, keeps the changes (`PackedChunk`), gives its `Cell` objects to
    a pool and leaves its slots empty.
  - `Unpack` takes a block from the pool, makes each cell from the source and lays the changes over.
  - `PackedCellHash`, `PackedChunkHash` and `SavePacked` read a packed chunk without unpacking it.
  - A read through `Chunk.CellAt` unpacks a packed chunk and counts it (`ReadUnpacks`). That's the design's "a read
    unpacks", and what found the tick's reads below.
- **The bench** is `Tests/SparseWorldBench.cs` (Explicit). It runs on the nine's valley square, built as
  `ChemGrid.MapWorld` builds it. The source is `cells.txt`, a byte a cell through the map legend, which is what
  step 2's `BakedSource` will be. Over it go the puzzles, stamps, rows, golf and the Hold.
  - The chunks: a plain one (0, 0); the village one with the most changes (2, 5); one burnt out by a Fireball (3, 2),
    235 ticks; one flooded with eight Floods and eight Create Waters (6, 2), 380 ticks; one dug, walled and heaped by
    24 earth spells (6, 5).
  - It ran under `dotnet` in Release on the worker VM (2 cores), best of 15 runs after a warm-up pass. Two runs agree
    to about 10%. Unity's Mono and IL2CPP aren't measured.
  - Run it with `dotnet test core -c Release --filter "FullyQualifiedName~SparseWorldBench" --logger "console;verbosity=detailed"`.
- **The tests** are `Tests/PackTests.cs`, in CI:
  - Pack then unpack gives back every field of every cell, the fire generations, the hash and each chunk's save record.
  - The hash and the save read a packed chunk the same as unpacked.
  - A read unpacks.
  - **A peer packing differently hashes the same**: step 2's planned test, written now against the prototype.

**Each chunk, packed:**

| Chunk | Changed cells | With a rare record | Packed | Save record | Hash, objects | Hash, packed | Save today | Save, packed | Pack | Unpack |
|---|---|---|---|---|---|---|---|---|---|---|
| plain (0, 0) | 0 | 0 | 0.2 KB | 0 | 0.118 ms | 0.123 ms | 0.08-0.16 ms | 0.000 ms | 0.06-0.14 ms | 0.035 ms |
| village (2, 5) | 1,333 | 0 | 31 KB | 55 KB | 0.120 ms | 0.137 ms | 0.83 ms | 0.75 ms | 0.08-0.13 ms | 0.040 ms |
| Fireball (3, 2) | 3,974 | 25 | 95 KB | 166 KB | 0.137 ms | 0.166 ms | 2.2 ms | 2.2 ms | 0.12 ms | 0.082 ms |
| flood (6, 2) | 508 | 0 | 12 KB | 22 KB | 0.116 ms | 0.122 ms | 0.33 ms | 0.28 ms | 0.07-0.15 ms | 0.041 ms |
| earth (6, 5) | 391 | 0 | 9 KB | 16 KB | 0.123 ms | 0.117 ms | 0.26 ms | 0.21 ms | 0.07-0.17 ms | 0.036 ms |

**More at once:**
- **A chunk edge crossed** (five chunks in a row, two of them the burnt and the flooded, 7,361 changed cells):
  unpacking and activating them takes **0.32 ms**. Today `SetActive` alone takes under 0.001 ms. That's 5% of the
  step's 6 ms budget (`ChemGrid.StepBudgetMs`), and 0.2% of a tick at 6 ticks a second.
- **A Misty Step or a respawn** (the 5 x 5 at once, 17,762 changed cells): **1.3 ms**.
- **The whole world's hash** (81 chunks): 9.9 ms unpacked (29.7 ns a cell) against 12.3 ms with every chunk packed
  (37.1 ns a cell). Across four runs, a packed cell costs **12-25% more** to hash. `RollingHash`'s slice a tick costs
  that much more for its packed cells. At 9,720 chunks a whole `CellHash` would be about 1.5 s; today's would be
  1.2 s, if the island could load.
- **Memory:** packing all 81 chunks lets **264-272 KB a chunk** go, which is the 4,096 `Cell` objects. Step 2's block
  also takes the per-cell arrays (`FireGeneration`, `Marks`, the `Snap*` arrays) for the design's ~356 KB. The 81
  packed chunks hold 544 KB in all (6.7 KB a chunk), 738 KB after the 5 x 5 had been active.

**What the numbers say:**
- **Decode cost.** A packed cell is a `source.Make` (a byte and the legend) plus, for a changed cell, a binary search
  and a 20-byte record. That's 4-7 ns a cell over an object read. For one chunk's hash that's 0.12 ms on objects against
  0.12 to 0.19 ms packed.
- **Saves.** Writing the JSON costs most of a save, not the diff: the packed save is only 0.1 ms faster on a changed
  chunk. The win is the chunks nobody touched. Today each costs 0.08 to 0.16 ms of diffing; packed, they cost
  nothing. At 9,720 chunks that's about 1-1.5 s off a save.
- **Changes are smaller than the plan's estimate.** A changed cell is 24 bytes: a 20-byte `PackedCell` and a 4-byte
  index. The plan guessed 40.
  - A burnt-out chunk is 95 KB. The plan's bound was ≤160 KB, and the chunk unpacked is 356 KB.
  - A village chunk has 1,333 changed cells (31 KB), more than the plan's "a few hundred": its rows, pads and stamps.
  - The first layout kept `Cover` and `DampUntil` in the rare record. Every burnt cell has an ash cover, and every
    flooded cell is damp, so that came to 80 bytes a cell and 326 KB for the burnt chunk. Both fields now sit in
    `PackedCell`, and only 25 of the burnt chunk's cells (its smoke) still need a rare record.
- **The case to watch is weather.** Snow covers, and rain damps, every cell of the active chunks. A long trip through
  snow leaves fully changed chunks at ~95 KB each, so 1,000 of them is ~95 MB. It doesn't break the budget, but
  `PERF MEMORY` on the island should look for it.

**Option A isn't needed as a fallback.** Option A would hold every cell of every chunk (~48 KB a chunk dense, 480 MB on
the island) to make reads faster. The source plus the changes already reads within 12-25% of an object, and unpacks
in under 0.1 ms. Only a chunk changed nearly everywhere would be smaller dense: ~48-80 KB against 95 KB. That's a
storage form one chunk could switch to later, if weather makes it matter. It isn't a fallback for the design.

**The tick reads inactive chunks, one chunk out.** With 25 chunks active and the other 56 packed, a Fireball and a
Flood at the active edge, and 600 ticks run, reads unpacked 8 chunks. All 8 were next to an active chunk: 7 on the
first tick and 1 at tick 147. On ChunkTests' 4 x 3 valley, where every inactive chunk borders an active one, reads
unpacked most of what was packed (100-124 of 113-150 packs). The reads:
- **Reads that check activity only after reading the cell.**
  - `Spread` reads the cell `Chunk.Next` returns, then checks `!nch.Active`.
  - So do `Flow` (called from `FlowBackward`) and `IsActiveAt` → `LocateActive` (a spell's `Land` drops its cells in
    inactive chunks that way).
  - Step 2 should have these check the chunk first. That changes no behaviour.
- **Real reads of an inactive neighbour.**
  - `SoakIn` reads the water across a chunk edge with no active check.
  - `NearHeat` reads `At` over a radius.
  - A spell's `LineCells` reads `At` past the active edge.
  - These use inactive state, which is the same on every peer, so they're deterministic. They reach one chunk out.

So step 2 needs one more rule: **a chunk next to an active chunk never packs.** With that rule the tick never unpacks
anything. The read hook in `Chunk.CellAt` can stay as a counted safety net: a `PERF` counter that should read 0.

**A peer packing differently hashes the same.**
- `PackTests.APeerPackingDifferentlyHashesTheSame` runs two copies of ChunkTests' valley for 300 ticks: fires, frost,
  lightning, moved earth, and columns of chunks let go and taken back while still burning.
- One peer never packs. The other packs and unpacks its inactive chunks on beats of its own, in three patterns:
  113-150 packs and 13-26 unpacks between ticks, plus 100-124 by reads.
- The packed peer's `PackedCellHash` equals the plain peer's `CellHash` at every tick. At the end, every field of
  every cell and the RNG agree.
- That holds even with reads unpacking chunks mid-tick, so the rule above is for time and memory, not for agreement.

**The hold time and the drawn hint (proposed):**
- **Which chunks pack.** An inactive chunk packs when, for the last **300 ticks** (50 s at 6 ticks a second), it has
  been all of these:
  - not active;
  - not next to an active chunk;
  - not in the view's drawn hint.
- **How fast.** At most **2 packs a tick** (≤0.35 ms), oldest first, then by row and column. Every peer runs the core,
  so the same tick-driven facts pack the same chunks.
- **The hint.** The view's drawn hint is the drawn tier: radius 4 at `FarRing.SetView(2, 4, 7)`, 81 chunks. The core
  keeps a hinted chunk unpacked, and its hold counts from when it leaves the hint.
  - The hint changes memory only. The peer test shows packing at any tick changes no hash, so peers needn't agree on
    it, and a peer with no view has no hint.
- **Why 300.** Unpacking is cheap (0.04-0.09 ms a chunk, 1.3 ms for a 5 x 5), so the hold is about memory, not time.
  - Sprinting (7 m/s ≈ 4.7 cells a second) crosses a chunk every ~82 ticks. Each crossing leaves a row of 9 chunks
    of the drawn tier behind.
  - At 300 ticks, ~33 chunks stay unpacked behind a sprinter, about 12 MB. That's the plan's "packing slack ~32". At
    walking pace (4.5 m/s, a chunk every ~128 ticks), it's ~21.
  - A player who turns back within 50 s finds the chunks still unpacked. One who comes back later pays a 0.1 ms
    unpack.
  - Anything from 60 to 600 ticks would work: shorter saves memory with four players far apart, longer saves
    nothing that matters. It belongs in `sim.json`'s `streaming` (`packTicks: 300`, `packsPerTick: 2`) so it can be
    tuned.

**Not measured:**
- Mono and IL2CPP in a player.
- A real `BakedSource` over a packed bake (the bench's source is a flat byte array; a per-chunk one adds a chunk
  lookup a row).
- The island itself, which can't load until step 2.
- The view's reads that would unpack drawn chunks (step 1b).
