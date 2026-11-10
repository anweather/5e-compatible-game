Needs: core
Area: world/maps/scale-12x10/ (new), unity/Assets/Prototype/Chemistry/ (WorldPlan, GeneratedMap, ChunkGen only as far as a forced square needs), its tests, docs/design/world-12x8-scope.md (a new section only), docs/GLOSSARY.md (new entries only)
Branch: scale-12x10
After: seam-and-sea-steps (same generator files)

# A 12 x 10 map for finding what doesn't scale (Andrew, 2026-11-09)

Andrew: "get a version of the 12 x 10 out so we can look for more things that don't scale with a large world. It
doesn't need to be final terrain, but it should have landmarks and all the chunks and cells for performance. Maybe
just the nine plus a bunch of rolling hill grass surrounded by water." It's a test map, not the real 12 x 10
(`world/maps/world-12x10/` stays as it is).

## The map

`world/maps/scale-12x10/`, a world file like the nine's (`generated`):
- 12 x 10 world squares, 6912 x 5760 cells, 108 x 90 chunks (9,720), **no wrap** (the seam is its own job).
- The middle 3 x 3 world squares are the nine as it is (the valley pinned in its square, the same seed and the
  same eight squares round it, Old Grumbler in the middle square), so the spawn and the nine's checks carry over.
  If a plan can't hold the nine's squares at an offset, say how close you got.
- Every other square is rolling grass hills (the `downs` or `filler-hills` template, or a forced square kind if
  one is needed: add it to the GLOSSARY first), and the outer ring of world squares is open sea, so the map is an
  island.
- Landmarks across it for the far ring (`musts`, `world/landmarks/`): Old Grumbler plus 6-10 others spread over
  the grass, so the known tier has things to show far off.
- **Bakeable** (no `"bake": false`): the point is all the chunks and cells, held as today's World holds them. The
  memory that costs is one of the things to find.

## Check (under dotnet; the VM can't hold the whole map)

- The plan: a golden; the middle 3 x 3's chunks hash the same as the nine's (or say what differs and why); no
  template rule (J-080 spacing) breaks it, or the forced squares are exempt by design.
- A smaller copy of the same layout (say 5 x 4 squares) bakes in a test, and its land meets the spawn's checks.
- Measure on the VM: bake time and the World's memory per chunk on the small copy; extrapolate to 9,720 chunks and
  say so plainly. The PC bakes and loads the full map next.
- Every fixture needs its `[Category]`; no `Does.Contain(object)`.

## Done

`dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes` and the
duplicate gate pass. Push the branch; the PC compiles it, bakes the full map, and the lead lands it. Report the
plan picture (a small PNG under `docs/design/scale-12x10/`), the numbers and the extrapolation.
