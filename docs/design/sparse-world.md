# The sparse World (#204)

2026-11-10, u2. The plan for the World to hold `Cell` objects only for the chunks near the players, and the
census of who reads cells today. The ADR is D17 (`docs/adr/0017-the-sparse-world.md`, accepted). Step 2, packed chunks
in the core, is in (`sparse-world-2`): see "Step 2: packed chunks in the core" at the end for what landed, where it
differs from the plan below, and what it measured. Step 3, the core's other map-sized arrays per chunk, is in
(`sparse-world-3`): see "Step 3" at the end. The rule it serves
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
| 2 | Packed chunks in the core: the block pool, pack and unpack, the hold time and the view's draw hint, the hash and save reading packed chunks, `BakedSource` with the legend table moved into the core, loading saves packed; `World.Cells` dropped once 1b lands. Goldens unchanged; tests that pack and unpack mid-fire, mid-flow and across a rule switch, and that a peer packing differently hashes the same | u2 | L, done (`sparse-world-2`) |
| 2b | The view's drawn hint: `World.SetDrawn` from the drawn tier (below) | the PC | S |
| 3 | The core's other map-sized arrays per chunk: `land`, `startAt`, `landMoved` / `landLaid` / `landCrag`, `bareCells` | u2 | M, done (`sparse-world-3`) |
| 4 | The view's map-sized arrays and binds per drawn chunk (scale doc #3-#5, #7), with #205's terrains for the drawn tier | the PC | L (#205) + M |
| 5 | The scale island loads in a player: the probe's scenarios on it, `PERF MEMORY` against this budget | the PC | S |

Steps 2 and 3 can go in either order. The island needs 2, 3 and 4 before it can load.

## Measured (`sparse-world-bench`, 2026-11-10)

u2, before step 2, so the lead can read the numbers before D17 is decided. (The prototype's API, `Pack(cx, cy, source,
strings)`, `PackedCellHash`, `SavePacked` and the rest, was replaced in step 2: see the last section.) The questions were what the hash and the
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

## Step 2: packed chunks in the core (`sparse-world-2`, 2026-11-10)

u2. The prototype (`World.Pack.cs`) is wired in. Every golden stands: the CellHash and FNV-1a goldens, the save's
(`c1737215` today, with the rules' fingerprint left out), `ChunkTests.AHashSliceAllocatesNothing`.

**What a World is now.**
- **Every world has a source** (`World.Source`): a `FillSource` of the terrain it was made with, until `MakeFrom` gives
  it another (a `BakedSource` for a baked map, a test's own). Not in the plan above, which had only baked maps
  packing; with a source on every world, packing runs on every world the tests make, and the yard's too.
- **Its chunks start packed with no changes**, so `new World` and `MakeFrom` make no `Cell` objects: a 1920 x 1920
  world is 900 empty `PackedChunk`s until something reads it. A read unpacks a chunk; a tick unpacks what it runs.
- **An active chunk is unpacked, and so are the eight round it.** `SetActive` unpacks a chunk that activates and its
  ring; `BeginTick` does the same for any active chunk still packed (a world just made, `MakeFrom`, a load). That's
  stronger than the bench's "a chunk next to an active one never packs": a chunk becomes next to an active one when its
  neighbour activates, and it might be packed then. With it, the tick never unpacks a chunk by reading it.
- **An unpacked chunk's block** (`ChunkBlock`, `Chunk.cs`): its `Cell[4096]`, `FireGeneration`, `Marks` and the
  still-water snapshot (`Snap*`). The pool keeps at most `World.PoolKeep` (32) blocks; a pooled block keeps its `Cell`
  objects and snapshot arrays for the next unpack.
- **`World.Cells` is gone.** Nothing read it after 1b.

**Packing** (`World.PackDue`, at the end of `EndTick`, between whole ticks):
- A chunk is **needed** while it's active, beside an active chunk (any of eight) or in the view's drawn hint; its
  `Chunk.Needed` is the last tick it was, or was unpacked.
- Once it has gone `sim.json` `streaming.packTicks` (300) ticks unneeded it may pack: at most `streaming.packsPerTick`
  (2) a tick, the longest unneeded first, then by row and column. `packsPerTick: 0` never packs.
- Both are rules (`StreamingParams`) so a mod can tune them. They change memory only: the peer tests run peers with
  different values.

**Reading a chunk where it lies.** `RowCells` with a scratch cell, and `ChunkReader` for one chunk, read a packed chunk's
cells from its source and changes without unpacking it: `CellHash`, `HashSlice` (no allocation still), `CellText`,
`ChunkHash`, the save, `ChangedChunks`, `KeepStart`, the dragon's audit (`Recount`) and `SetLand`'s pass. A save
against the world's own source writes a packed chunk's record from its changes (`SavePacked`); against another source
it diffs the chunk read where it lies.

**Writes to a packed chunk unpack it,** as the plan says (map building, the lays, a rule switch's `LayBuildings` /
`LayCells`, the dragon's `Freshen`), and it packs again once its hold is up. The two passes over every cell,
`SnuffFires` and a lapse's `Sweep`, go a chunk at a time (`WriteEveryCell`), packing each packed inactive chunk again
straight after, so they never hold the whole map unpacked.

**Loading.** `Load` makes every chunk from the source (packed, no cells), lays each saved chunk's cells in (unpacking
that chunk), then packs it again at once if it's inactive. So a load unpacks only the chunks with changes, one at a
time, and an inactive chunk ends up packed. (The plan said "straight in as packed chunks": this goes through the cells,
which keeps `LoadCell` the one reader of a saved cell.)

**`AllCells()` unpacks** every packed chunk it passes, and leaves it to `PackDue`. It's for the probes, tests and the
MCP's simulator on small maps; on a big map, count over `ActiveChunks`, or read a chunk with `ChunkCells` (which also
unpacks, one chunk).

**`BakedSource`** (`BakedSource.cs`): a baked map's `cells.txt` by chunk, a chunk of one character held as that one
byte. The legend (`BakedSource.Legend`) and the painting (`World.PaintLegend`) moved from `ChemGrid.PaintCell` into the
core. A legend character's cell is made by painting it on a one-cell world under the map's rules, so it's exactly the
cell `PaintCell` made (a terrain the rules lack stays dirt, as before). It holds no heights: a map's land is
`World.land` (`SetLand`), step 3's. `BakedSource.Read(dir, rules)` reads `cells.txt` a row at a time, so a big map's
text is never held whole; `FromText` takes the text, as `ChemGrid.MapWorld` has it. Its `Version` is the cells' FNV-1a.

**The view (changed here, not compiled: the PC's to check).** `ChemGrid.MapWorld` now makes its world from
`BakedSource.FromText(map, rules)` with `MakeFrom` instead of painting every cell, and `ChemGrid.Legend` /
`PaintCell` are gone. The cells it makes are the same; a map's start is `source.Start` (the last `@`, as before).

**The drawn hint, for the PC (step 2b).** The view calls, from the main thread, whenever its drawn tier changes (or
every frame, it's cheap):

```csharp
world.SetDrawn(IEnumerable<(int cx, int cy)> chunks); // the chunks it draws; null or empty for none
```

Each call replaces the last. A chunk in the hint isn't packed, and its hold counts from when it leaves. It's not a
command and not shared: a peer with no view gives none. Until the view calls it, a drawn chunk outside the active ring
packs 300 ticks after it was last read and unpacks again at the view's next read (0.1 ms): correct, only slower, and
counted in `World.ReadUnpacks`. The view must not keep `Cell` objects across frames for chunks it doesn't hint: a
packed chunk's objects go back to the pool and come out as another chunk's cells. Numbers for `PERF MEMORY`:
`UnpackedCount`, `PackPoolCount`, `Packs`, `Unpacks`, `ReadUnpacks` (should stay 0: reads of unhinted inactive chunks),
`PackMs`, `UnpackMs`, `DrawnCount`, `MapArrayBytes` (what step 3 left of the map-sized arrays; `PackedLandBytes` of it).

**Tests** (`PackTests`, in CI): a new world makes no cells until read; pack then unpack gives back every field, the hash
and every save record (against its own source and another); the hash, `CellText`, `ChunkHash`, the save, the audit and
`KeepStart` read packed chunks without unpacking them; a chunk packs once its hold is up and not beside an active chunk
or in the hint, two a tick by row; a load lays its inactive chunks in packed and runs on the same; the dragon's dive
snuffs fires in packed chunks and leaves them packed; **a peer packing differently hashes the same** at every tick, now
against the real path (`Pack`, `Unpack` and `PackDue` with its own hold) and across a rule switch that unfits the
inactive chunks; and **a late joiner replays the host's history to the host's state** (the hash every 30 ticks, the RNG,
the active chunks, the save, every field) whether the host packs after 40 ticks, the joiner never, or at once.

**Measured** (`Tests/SparseWorldBench.cs`, Explicit; `dotnet` Release on the worker VM, 2 cores; the nine baked, 1728 x
1728, 729 chunks, built as `MapWorld` builds it; heap after a full collection, the rules loaded first):

| The nine's World | Before (`origin/dev`) | After |
|---|---|---|
| As built, then the start plan and 360 ticks | **317.6 MB** | **84.5 MB** |
| ... of which `Cell` objects and chunk arrays | ~258 MB (729 chunks) | ~20 MB (72 unpacked: 25 active, the ring, the drawn tier read) |
| ... the source | (the view's map text) | 2.8 MB (14 chunks all one character) |
| ... changes held by the 657 packed chunks | | 0.12 MB |
| ... the map-sized arrays (`land`, `startAt`, `landMoved` / `landLaid` / `landCrag`, `bareCells`) | 57 MB | 57 MB: step 3 |
| Every chunk unpacked | | 282 MB, 396 KB a chunk with the arrays (305 MB with `World.Cells` added back) |

The scale probe's 254 MB was Unity's; .NET measures more (317.6 MB) for the same World. After step 2 the World is the
design's ~80 MB, and two thirds of it is step 3's arrays. Built from scratch the nine takes 0.3-1.3 s (VM noise),
with 23 chunks unpacked by its lays.

**A walk across the nine** (`AWalkAcrossTheNine`): a character walks east 10 chunks at walking pace (half a cell a tick)
and back, 3,600 ticks, casting a Fire Bolt ahead every 60, its reach planned each tick (`ChunkPlanner`), the drawn tier
hinted. One world packs; the other is the World before step 2, every chunk unpacked and none packed. Two runs:

| | Packing on | Before (every chunk unpacked) |
|---|---|---|
| A tick (planning, switches, the step), mean / p99 | 3.7-5.2 ms / 5.8-8.3 ms | 3.9-5.4 ms / 6.1-8.7 ms |
| The 161 ticks chunks activated on: the switches, median | 0.15-0.19 ms | 0.14-0.16 ms |
| Worst tick | 166-190 ms | 165-191 ms: a lingering chunk catching up its missed ticks (`catchUpTicks`), the same either way |
| Packs / unpacks in the walk | 192 at 0.20 ms / 213 at 0.11 ms: 62 ms over 3,600 ticks | |
| Read unpacks | 0 | |
| Chunks unpacked at most | 139 | 729 |

Packing costs about 0.02 ms a tick on average, and at most 2 packs (~0.4 ms) on any tick. That's within the step
budget (`ChemGrid.StepBudgetMs`, 6 ms), and the VM's noise between runs is larger. The hashes of the two worlds agree
every 300 ticks.

**Not done here:**
- The view's call of `SetDrawn` (2b).
- Step 3's arrays: 57 MB on the nine. On the scale island they're `land`, `startAt`, `landMoved`, `landLaid` and
  `landCrag` at 160 MB each and `bareCells` at 40 MB: about 0.8 GB before a single cell, so the island may load in a
  player now (its cells are ~100 MB packed), but it isn't the design's budget until step 3.
- Mono and IL2CPP aren't measured.

## Step 3: the core's other map-sized arrays (`sparse-world-3`, 2026-11-10)

u2. Each array the World held the size of the map is per chunk now, and none had to stay whole. Every golden stands
(the CellHash and FNV-1a goldens, the dragon's, the save's), and the packing, late-join and peer tests pass unchanged.

**Where each went:**

| Was | Now | On the island |
|---|---|---|
| `land` (`SetLand`, an int a cell) | An unpacked chunk's in its block (`ChunkBlock.Land`); a packed chunk's as `LandCodec`'s bytes (`Chunk.PackedLand`): each cell's difference from left + up − up-left, zigzag varints, a run the prediction gets exactly as one varint. Exact for any ints, `NoLand` too | 160 MB → **7.5 MB** (~0.8 KB a chunk; the sea's chunks a few bytes) |
| `startAt` and the palette (`KeepStart`) | A chunk's starting cells as a `PackedChunk` against the starting state's source (`Chunk.Start`). A chunk packed when the start is kept shares its own changes (a `PackedChunk` never changes); an unpacked one's are diffed from its block. Read with `ChunkReader` over those changes (`StartOf`, `StartCell`) | 160 MB → **0.2 MB** (7,048 cells held apart from the source: the lays) |
| `landMoved` / `landLaid` / `landCrag` (int) and `bareCells` (bool) | `ChunkCells<T>`: an array a chunk, made at the first write to that chunk, the rest reading as 0 | 3 x 160 MB + 40 MB → **~0.9 MB** (the chunks the rows' pads, the roads' cuts and the map stamps reached) |

**The land, read and moved where it lies.** `LandOf` reads an unpacked chunk's block, or a packed chunk's bytes read out
into one chunk's peek (`PackedLand`), so neither a read nor a stamp's or a road's cut (`MoveLand`) unpacks a chunk. A
moved peek goes back into its chunk's bytes when another chunk's is read out, as it unpacks, or at the end of the
write (`DropLandPeek`). `SetLand(Action<int, int[]> row)` takes the land a row at a time, north to south, so a big map's
land needn't be held whole to set it; `SetLand(int[])` goes through it. `LandUnitsAt(x, y)` reads it (for tests and
the view).

**The start's one difference.** The palette gave every cell that hashed and saved the same one shared copy, so a
dry cell's `Liquid` and a waterless cell's `Sets` (which mean nothing and aren't hashed) came back from the first
such cell. Now each cell's own come back. Nothing that's hashed, saved or ticked reads them, and the dragon's goldens
agree. `StartShapes` now counts the cells the start holds apart from its source (it counted the palette), and
`StartBytes` is what they hold. The view's probe and perf lines (changed here, not compiled: the PC's to check) say
`start_cells` for `palette`, and `ProbeDragon`'s `start_state_kb` is `StartBytes` (it added an int a cell).

**Measured** (`SparseWorldBench`, Explicit; `dotnet` Release on the worker VM, 2 cores; heap after a full collection;
before and after run back to back from two worktrees, `origin/sparse-world-2` with this bench and this branch). The
island is the Scale Island baked in full by the lean bake (81 s, a row of chunks at a time), then built as `MapWorld`
builds it:

| The World | Before (step 2) | After (step 3) |
|---|---|---|
| The nine as built | 67.9 MB | 13.0 MB |
| The nine in play (25 active, 72 unpacked, 360 ticks) | **84.4 MB** | **30.2 MB** |
| ... the map-sized arrays | 57.0 MB | 1.7 MB |
| The Scale Island as built (9,720 chunks, 52 unpacked by its lays) | 802 MB | 52.3 MB |
| The Scale Island in play (25 active, 81 unpacked) | **812 MB** | **63.0 MB** |
| ... the blocks | ~22.5 MB | ~22.5 MB |
| ... the source (`BakedSource`, 4,251 chunks of one character) | 21.4 MB | 21.4 MB |
| ... the packed chunks' changes | 1.6 MB | 1.6 MB |
| ... the map-sized arrays (`MapArrayBytes`) | 759.4 MB | 8.6 MB, of which 7.5 MB land |
| ... the rest: the chunk headers and their empty changes | ~8 MB | ~9 MB |
| Built in | 9.0-9.6 s | 8.4-8.6 s |
| The walk across the nine: a tick mean / p99 | 4.79 / 8.21 ms | 4.15 / 6.39 ms (VM noise between runs is larger) |
| A pack / an unpack | 0.206 / 0.116 ms | 0.186 / 0.146 ms: an unpack reads the land out of its bytes |

**What still grows with the map** (core, the island's numbers):
- The source: 21.4 MB, ~2.3 KB a chunk (a chunk of one character is one byte).
- The packed chunks' land: 7.5 MB, ~0.8 KB a chunk.
- The chunk headers: ~9 MB, ~1 KB a chunk (`Chunk`, its edge arrays and an empty `PackedChunk`).
- The index arrays of `ChunkCells` and the chunk store: 8 bytes a chunk each.
- Each grows by its chunk count, as the design's budget allows (a source and headers of a few KB a chunk).

**Not done here (map-sized, transient, at build):**
- `WorldBuildings.LayInto` reads the map's `land.json` whole (an int a cell, 160 MB on the island, and the file's
  text) to cut the pads and roads on it, then lets it go. Moving it onto the World's own land, or reading the file a
  chunk at a time, changes what the cuts read beside water (the World's land is `NoLand` there), so it wants a golden
  check of its own.
- The view's `ValleyChemistry.landFromFile` holds the same array for the life of the map, and `LandFile` has no reader
  a row at a time yet. With `SetLand(row)` the view can stream it once there is one (step 4).
- `SetLand` walks every cell once (40 M on the island, in the build's time above), reading packed chunks where they lie.


## The guard and the build's peak (`sparse-world-guard`, 2026-11-10)

**An unpack off the World's thread throws.** A World remembers the thread that made it; `UnpackChunk`, `PackChunk`
and a read's unpack (`UnpackOnRead`) throw `InvalidOperationException` on any other, before anything changes, so a
view worker that reads a packed chunk's cells fails loudly instead of putting a chunk in the unpacked list twice. The
check is on the unpack and pack only: a read of an unpacked chunk (the tick's every read) costs nothing more, and reads
fine from a worker. `World.OnOwnThread` says which side a caller is on. (`PackTests`: a read and an `Unpack` off the
thread throw and leave the chunk packed; an unpacked chunk reads off the thread; a pack off it throws.)

**What the island's World build allocated** (`WorldBuildBench`, under dotnet on the VM: each phase's bytes allocated,
garbage included, since a player's Mono heap keeps what it grows to). The PC saw private memory go 2.06 -> 3.62 GB in
one second of the build: the rows and the Hold were 3.2 GB of garbage between them, and step 3 didn't touch either.

| Phase (the island, 39.8 M cells) | Before | After |
|---|---|---|
| cells.txt read whole (`ChemGrid.CellMap`, view) | 152 MB | 152 MB |
| `BakedSource.FromText` | 115 MB | 39 MB: lines read where they lie, not `Split` |
| MakeFrom, puzzles, stamps | 8 MB | 8 MB |
| the rows (`WorldBuildings.LayInto`) | **1,951 MB** | 243 MB: land.json read once, not once a graded row |
| the Hold (`WorldHold.Load` reads places.json) | **1,262 MB** | 20 MB: a place of rows read as spans, not a set of its cells |
| KeepStart (step 3) | 1 MB | 1 MB |
| land.json read (`ValleyChemistry.LandOf`, view) | 385 MB | 220 MB: inflated a buffer at a time, not whole |
| SetLand | 10 MB | 10 MB |
| **All** | **3,884 MB** | **694 MB** |

- `GradeRoads` read and inflated land.json (385 MB) for each graded row and added `LandMovedAt` over all 40 M cells.
  It reads the file once for all the rows and asks the land as it stands cell by cell (the file's, plus what moved,
  less the row's own road grades, as the copy it made held it): the same cuts, goldens unchanged.
- `Place.Parse` turned a place given as rows (the generated water bodies: the island's sea is millions of cells) into
  a `SortedSet` of every cell and back into spans. Rows alone go straight to sorted, joined spans now.
- What's left that grows with the map is the land itself (an int a cell, 160 MB, read twice: the rows' copy goes,
  the view's `landFromFile` stays for the map's life) and the view's cells.txt read whole (80 MB of text). Both are the
  view's or a row reader's job (step 4: `ChemGrid` can take `BakedSource.Read`, which reads cells.txt a row at a time,
  and `LandFile` a reader a row at a time for `SetLand(row)`).
- The PC's 4.47 GB at 41 s is past the core's build: the view's ground and terrains, which this VM can't measure.
