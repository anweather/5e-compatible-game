Done: landed on dev (see git log); don't redo.
Needs: unity
Area: unity/Assets/Prototype/Scripts/ (Contour*, CellSlickMesh, ChemGrid.Effects.cs, the spell effect prefabs' set-up; not ChemGrid.Visuals.cs, not CellDressing), unity/Assets/Prototype/Chemistry/Tests/DragonTests.cs (the one timing test), docs/perf/2026-11-09-profile.md (a new heading only)
Branch: perf-fireball-frames

# 60 fps pass: the boulder check (#195), the dragon timing test (#192), then a Fireball's two bad frames (#188, #189)

The goal is 16.7 ms a frame, mean and p95 (`docs/perf/2026-11-09-profile.md`). The rule (J-086): cost follows the
cells and chunks near the action, never the map's size. Each part its own commit, in this order.

## 1. #195: did turning hidden Terrains off move a boulder?

In `perf-culling-182`, 1 of 3 smoke runs on the nine had boulder 13,11#0 settle a cell off its spot. Run smoke on
the nine about 8 times at 586765f4's parent and 8 times at dev. If it only happens with the Terrain component off,
fix it (or turn the component back on for chunks holding things, and say what culling that gives back). If it
happens before too, say so on the issue with the counts: a thing's cell is shared state and physics must not
decide it (D4), so write down how the cell changed.

## 2. #192: `DragonTests.AFullSweepStaysWithinTheTickBudget`

Fails in full EditMode runs on both machines (250-263 ms against 166.7), passes alone. Give it #177's treatment
(best of three rounds after a settled heap, `[Category("Timing")]`; TESTING_LAYERS, "Timing tests on a busy
Editor").

## 3. #188: the frame a Fireball lands, 58-92 ms on the nine

24-37 ms of it is `Contour.Film.Outline`: `CellSlickMesh` contours 48,107 cells to outline 7 film cells, with
9 MB of garbage (it showed at 189 ms on the nine's walk too). Contour only the cells near the film (the chunks
that hold film, plus a border), reuse the buffers, and name what the rest of the frame is.

## 4. #189: the frame it's cast, 37-43 ms

10-12 ms is `Instantiate` in the spell's own effects. `perf-fix-2` found the pack's smoke prefab holding an unused
Sphere mesh on its billboard renderer and trims it (`ChemGrid.TrimFx`); check the other pack prefabs the spells
use for the same, and pool or pre-warm the cast effects. The look must not change (a frozen still, before and
after).

Dev's player build loads the nine again (#183 fixed, 647f169b): measure parts 3 and 4 in a development player
build if the Mac can make one (Fireball on the nine and on the valley: worst frame, mean, p95 over the cast and the
5 s after), else the Editor. The PC is in `ChemGrid.Visuals.cs` (`perf-fix-3`: #185, #186, #184) and the dressing
is next there (#194): stay out of both.

## Done

EditMode passes in full; `dotnet test core` passes; smoke passes on a fresh valley yard and on the nine; a "Fix:
Fireball frames" heading in the profile doc. Self-land with `land.sh auto perf-fireball-frames --compiled`; the
lead closes the issues from the report.
