Needs: core
Area: unity/Assets/Prototype/Chemistry/ (World*, Chunk*, ChunkStreaming, ChunkPlanner, the tick's spans and neighbour reads, the cells' bounds checks), its tests and goldens, docs/design/world-12x8-scope.md (a new section only)
Branch: sim-across-the-seam

# The simulation across the east-west seam (the 12 x 10's next core step)

The 12 x 10 wraps east to west (`world/maps/world-12x10/map.json` `"wrap": "ew"`, J-083). The plan and the generator
already wrap (generator 6's `ChunkGen.WrapX`: land at x and x + 6912 is the same, bit for bit). The World doesn't: the
scope note (`docs/design/world-12x8-scope.md`, the table) counts 52 bounds checks in 23 files, chunk links, the
planner and the tick's spans, and calls a missed check a desync. The sparse World is done in the core (D17, steps
1-3, the guard), so the World now holds the 12 x 10's chunks cheaply; this is the step that lets a character walk
over the seam.

## What to build

- A World made with east-west wrap (from `map.json`'s `wrap`): a cell or chunk x outside [0, W) folds into it, in
  one place (an accessor the rest use), so neighbour reads, fire, water, gas, pushes, the dragon's sweep, casts and
  shapes reach across the seam; north and south stay walls.
- Chunk streaming and the planner across the seam: a character at x = 10 keeps chunks at the far east active; reach,
  keep, linger and spread wrap; packing (D17) the same.
- The tick's order stays deterministic: active chunks visited in a fixed order that doesn't depend on where the seam
  is relative to the characters (state the order in the doc).
- Hashes, saves, late join: unchanged for a world without wrap (goldens stay); a wrapped world's hash defined and
  tested.
- Find the 52 checks (grep `>= W`, `< 0`, `W - 1`, `% W` and the like) and list what each became.

## Check

A golden world with wrap: a fire lit at x = W - 2 spreads to x = 1 and the hash equals the same fire lit at x = 1
spreading west (mirror test); water flows across; a character's reach across the seam; two peers and a late joiner
agree with a character crossing back and forth; the valley and the nine (no wrap) byte-identical. Every fixture
needs its `[Category]`; no `Does.Contain(object)`.

## Done

The core gates pass. Push the branch; the PC compiles it. Say what the view will need (the camera and the origin
re-centre across the seam, the far ring and landmarks) as a list for the PC, with sizes.
