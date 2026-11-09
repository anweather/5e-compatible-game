Needs: core
Area: unity/Assets/Prototype/Chemistry/ (GeneratedMap, PlanInputs, WorldPlan*, ChunkGen*, KnownRing, FarLandmarks) and their tests, world/maps/world-12x10/, world/biomes.json
Branch: world-12x10-plan

# A 12 x 10 world, plan and known ring only (the first proof)

Read `docs/design/world-12x8-scope.md` first: it has the paths and numbers. Andrew's call (J-083, J-084): 12 world
squares wide, 8 rows of land plus an ice row north and a desert row south (12 x 10 in all), wrapping east-west, laid
out like the paper prototype (`tools/world-paper/`). This brief proves the far field scales. No cells for the whole
world, no sparse World yet.

## What to do

- `PlanInputs` takes a width and a height; `GeneratedMap.Load` stops refusing W != H and reads the wrap from map.json
  (`"wrap": "ew"`; absent means none). The nine, its goldens and the valley stay byte-identical.
- A new generated map `world/maps/world-12x10/` (map.json only, like the nine; seed `valley-1` unless the plan needs
  another). It can't be baked in full (about 9-10 GB of cells): nothing should try to open it in the Editor yet, and
  MapCheck shouldn't bake it.
- ChunkGen wraps east-west: every land point takes x modulo the map's width in cells, the noise lattices are periodic
  across the seam (`climateCells` must divide the width; world-squares.md section 8), as generator v6. If that moves
  the nine's golden, change it in the same commit and say so.
- KnownRing and FarLandmarks measure across the wrap (`PlanGrid.ChunkDistance`), with chunk ids modulo the width.
- Tests: a 12 x 10 EW plan golden; the land is continuous at the seam (heights at x = W-1 and x = 0 agree within the
  usual neighbour step); the north row is ice and the south row desert; KnownRing over all chunks, timed and reported
  (expect ~0.26 ms a chunk); a known ring standing at the seam sees both sides.
- A picture: the plan and a coarse heights map of the whole world (a PNG or the world-paper's format), committed
  under `docs/design/` or attached to your report, showing ice north, desert south and an unbroken seam.

Ice and desert edge looks (ragged fringe, ice cliff, beach and sandstorm) are later, paper first. Leave the wall
rows plain if they need more than the existing `ice-wall` and `dune-sea` templates.

## Done

`dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes` pass. Push
the branch; land it with `land.sh` if only core and data changed, else stop for the PC to compile. Mind
`Chemistry/CLAUDE.md`'s notes on Unity's NUnit and fixture categories.

After: the sparse World and the generate switch (scope note, step 3).
