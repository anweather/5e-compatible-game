# D17. The sparse World: inactive chunks as their source plus their changes (2026-11-10)

**Status:** Proposed

- **Context:** The World holds a `Cell` object for every cell of the map: 356 KB a chunk. That's 254 MB on the
  nine and 3.4 GB on the scale island's 9,720 chunks, so the scale island runs out of RAM on the PC before its
  World is built (`docs/perf/2026-11-10-scale.md`, #204). J-086 says cost follows the active region and the drawn
  tier, never the map's size. The save already stores a chunk as the cells that differ from its `IChunkSource`.
- **Decision:**
  - An inactive chunk may be **packed**: only its source (for a baked map, the bake's rows and land, held by chunk)
    and its **changes**, the cells that differ from the source.
  - An active chunk, and an inactive one the view is drawing, is **unpacked**, with `Cell` objects from a pool of
    blocks.
  - How a chunk is held never shows: every accessor, the hash, saves and the tick see the same cells either way.
  - The cell hash keeps its bits, reading packed chunks without unpacking them.
  - Fit numbers still follow activation, not packing.
  - Every reach for a cell goes through its chunk (`World.At`, `CellAt`, `AllCells`, `Chunk.CellAt`;
    `Chunk.ActiveCells` for the tick's hot passes), and `World.Cells` goes.
  - The design and the steps are in `docs/design/sparse-world.md`.
  - **Not decided here:**
    - a hash cached per chunk (it would move every golden);
    - regenerating chunks from the seed in play (the bake stays the source for generated maps).
- **Why:**
  - **Memory stops following the map.** About 100-210 MB at 9,720 chunks against 3.4 GB. Only the source, a few KB a
    chunk, grows with the map.
  - **It reuses what's proven.** Unpacking is `World.Load`'s chunk half, and packing is `ChangedCells`.
  - **It scales better than packed arrays for every cell** (about 480 MB at 9,720 chunks, still growing with the
    map), and it keeps play's changes, which reading the bake back on activation alone would lose.
  - **It keeps the goldens and the save format.**
- **Reopen if:**
  - reading a packed cell from its source makes the rolling hash or a save too slow (then packed arrays, option A in
    the note);
  - a world's changes grow past the source (a whole map burnt over);
  - the view needs to read cells off the main thread.
- **Sources:** `docs/perf/2026-11-10-scale.md` (the PC's scale probe), `docs/design/sparse-world.md`,
  `World.Save.cs`.
