Needs: core
Area: unity/Assets/Prototype/Chemistry/MapCheck*.cs, MapFile*.cs, MapIdentity.cs and their tests; a flat test map under
world/maps/ (new, e.g. flat-1728); tools to measure; TerrainGrid and the view only after maps-phase-6 lands
Branch: map-size

# Any map size from map.json, and the big-map risk check (world squares; given to the Mac)

Read `docs/design/world-squares.md` and `docs/design/world-nine-options.md` (phase 1). Today the valley's 576 x 576 is
fixed in code (`MapCheck.Size`, `TerrainGrid.Valley()`).

1. **Core:** `map.json` gains `size`; `MapCheck` and the map loader read it (any multiple of 64), and every place that
   assumes 576 takes the map's size. The valley keeps 576 and stays byte-identical (its hash unchanged).
2. **A flat test map,** 1728 x 1728 (3 x 3 valley squares), empty land at one height, valid under `MapCheck`.
3. **Measure, in core first:** memory for its cells (World "cells everywhere", `Cell` as a class), the time to load
   it, and a rebuild. Then, once the PC's `maps-phase-6` lands (it changes how the scene holds terrain; don't touch
   TerrainGrid or the scene before that), in the Editor: start-up, memory, and a two-peer run on it. Report the numbers
   against the valley's (start-up 7.4 s, rebuild ~0.9 s) and say whether `Cell` must become arrays (D9) before nine
   squares.
Before landing: core build -warnaserror -m:1, `dotnet test core` twice, format; the Unity part with EditMode and
`smoke`. Push the branch only, remove this claim on the branch in the last commit. The lead lands it.
