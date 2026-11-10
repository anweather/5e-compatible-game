Needs: core
Area: unity/Assets/Prototype/Chemistry/ (WorldPlaces, WorldPlan's name picking, GeneratedMap.WriteRows), world/squares/*.json name lists only if needed, its tests
Branch: scale-12x10 (commit on it; it's u2's branch, not landed yet)
After: bake-lean

# A map never repeats a place id (#203)

The scale probe (`docs/perf/2026-11-10-scale.md` on `scale-12x10`) found the full bake of the Scale Island throws:
`WorldPlaces.Generate` -> `FromJson` refuses `place dunmere is listed twice` (and fogmere, the-elder-oak). Names come
from a template's `villageNames` / `landmarkNames` (6-8 each) with nothing stopping a repeat across the map; a
player's first launch on that map throws the same way. It blocks landing `scale-12x10`.

Give each place a unique id across the map, deterministically from the plan (same seed, same names): pick an unused
name first, and when a list runs out, a numbered or qualified one ("Dunmere Reach", "the Elder Oak of the Downs";
the id stays a slug). The nine's and the valley's names and ids must not change (their goldens and saves). A test:
the Scale Island's plan and the 12 x 10's have no repeated place id; the far ring's landmark labels follow the names.

The core gates pass. Push `scale-12x10`; the PC compiles it, bakes the island in full and the lead lands it.
