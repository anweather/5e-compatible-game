Needs: core
Area: unity/Assets/Prototype/Chemistry/ (World*, Chunk*, World.Pack.cs, the baked source, the hash and saves), its tests, docs/design/sparse-world.md
Branch: sparse-world-2 (off `origin/sparse-world-bench`, which isn't on dev yet; the PC compiles both)
After: sparse-world-bench

# The sparse World, step 2: packed chunks in the core (#204, D17 accepted)

D17 is accepted (J-089): your bench fit the tick budget (five chunks unpacked at a chunk edge in 0.32 ms, a Misty
Step's 5 x 5 in 1.3 ms, a packed cell hashing 12-25% dearer, option A not needed). Step 2 as the design says
(`docs/design/sparse-world.md`, `docs/adr/0017-the-sparse-world.md`):

- Wire the prototype in: inactive chunks pack to their source plus their changes on the hold you proposed (300
  ticks, 2 packs a tick, never a chunk next to an active one), and unpack from a block pool on activation or a read.
- `BakedSource` in the core (the bake's rows by chunk; the legend that `ChemGrid.PaintCell` holds in the view moves
  into the core with it), and the per-cell arrays (`FireGeneration`, `Marks`, the `Snap*` arrays) per block.
- The hash and saves read packed chunks; the save golden (153e8400) and the CellHash goldens stay; late join replays
  to the same state whatever each peer packed (the test you wrote, now against the real path).
- `World.Cells` goes (or turns internal): nothing in the core or the view reads it (sparse-world-1b).
- The view's drawn hint (a command-free note of which chunks it draws, so they aren't packed under it), as the
  design says, with the view side left to the PC: name the API it calls.

Keep `AllCells()` callers working (probes count over it) but say in the doc it unpacks.

## Check

The core gates pass; PackTests and the late-join replays with packing on; the nine's World memory in a .NET bench
before and after (254 MB to the design's ~80 MB), and the step's time on a walk-shaped test (chunks activating and
packing as a character moves) within the step budget. Every fixture needs its `[Category]`.

## Done

Push the branch; the PC compiles it, checks the nine and the valley, and tries the Scale Island in a player. Report
the memory, the timings and the API the view's drawn hint needs.
