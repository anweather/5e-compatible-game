Needs: core
Area: docs/design/sparse-world.md (new), docs/adr/ (a proposed ADR), unity/Assets/Prototype/Chemistry/ (World*, Chunk*, the callers of World.Cells), its tests
Branch: sparse-world-1
After: place-ids-203

# The sparse World, step 1: the plan and the accessor move (#204)

The scale probe (`docs/perf/2026-11-10-scale.md`, #204): the World holds every cell of the map as a `Cell` object,
`Cells = new Cell[w * h]` (World.cs:29, World.Chunks.cs:41): 254 MB on the nine, 3.4 GB on the 12 x 10's 9,720 chunks
(356 KB a chunk). The 12 x 10 can't load on the PC until this changes. J-086: cost follows the active region.

## Step 1 (this brief)

1. **A design note**, `docs/design/sparse-world.md`: how an inactive chunk is held (packed arrays a chunk, or its
   bake's rows read back on activation, or regenerated from the seed when untouched plus a delta when changed), how a
   chunk becomes full `Cell` objects on activation and packs on deactivation, what the hash, saves, late join and the
   fit numbers (#190's `Chunk.Fit`) do, what the view reads for drawn and known chunks that aren't active, threading,
   and the memory budget at 9,720 chunks. A proposed ADR (template `docs/adr/README.md`) for the choice; it lands as
   proposed, Andrew accepts it.
2. **The accessor move**, the part that changes no behaviour: list every caller of `World.Cells` and `Chunk.Cells`
   (core and view; `grep`), and move the core's callers to accessors that work by chunk (D9: cells through accessors),
   so step 2 can change the storage under them. List the view's callers in the note with what each needs; don't
   change the view (`Scripts/`), the PC does that.

Goldens unchanged (`CellHash`, FNV); no new allocation in the tick. Every fixture needs its `[Category]`.

## Done

The core gates pass. Push `sparse-world-1`; the PC compiles it and the lead lands it (the ADR stays proposed). Report
the caller counts, the design's choice and its memory estimate, and the steps after this one with sizes.
