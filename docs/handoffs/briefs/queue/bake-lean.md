Needs: core
Area: unity/Assets/Prototype/Chemistry/ (GeneratedMap.Bake and what it calls in ChunkGen*, the bake's writers), its tests
Branch: bake-lean

# The bake that doesn't grow with the map (from u2's scale-12x10 numbers)

Your scale island measurements (`docs/design/world-12x8-scope.md`, "A test map for scale", on `scale-12x10`): the
bake allocates about 9.5 MB a chunk (90 GB of garbage for the 9,720-chunk island) and keeps every chunk's arrays
until it writes, so its peak memory grows with the map. J-086: costs follow what's near players, never the map's size;
a bake runs once, but it has to fit beside an Editor in 32 GB.

## What to do

- Write each chunk (or each row of chunks) as it's made, then drop its arrays, so the bake's peak memory is a few
  chunks' worth, whatever the map's size.
- Cut the allocation a chunk: reuse buffers across chunks (pooled arrays, spans), no per-cell objects or LINQ in the
  hot path. Name the top allocators first.
- The output files must be byte-identical to today's (the nine's and the scale island's bake hashes, `MapCheck`),
  and a bake must stay deterministic and the same with any thread count.

## Check

A test that bakes a small map twice (before and after shapes) and compares the files byte for byte; the measured
allocation a chunk and peak working set before and after on the 6 x 6 copy, and the time. Every fixture needs its
`[Category]`; no `Does.Contain(object)`.

## Done

The core gates pass (`build -warnaserror`, `test`, `format --verify-no-changes`, duplicates). Push the branch (off
`origin/scale-12x10` if it hasn't landed yet, so the scale island's tests come with it; say which); the PC compiles
it and the lead lands it. Report the numbers.
