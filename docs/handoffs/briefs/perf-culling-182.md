Needs: unity
Area: unity/Assets/Prototype/Chemistry/Tests/ (WorldTwelveTests only), unity/Assets/Prototype/Scripts/ (MapTerrains, FarRing, the terrain roots; not ChemGrid.Visuals.cs), docs/perf/2026-11-09-profile.md (a new heading only), docs/TESTING_LAYERS.md
Branch: perf-culling-182

# 60 fps pass: the slow 12 x 10 test (#187), then culling on the nine (#182)

Two jobs, each its own commit, in this order.

## 1. #187: `WorldTwelveTests.TheKnownRingOverEveryChunk` under Mono

184 to 195 s on the Mac against a 180 s limit (it fails), 1 s under .NET. It costs minutes of every EditMode run on
the PC too. Keep what it proves in the .NET suite (`dotnet test core`, where the landing gates run it) and take it
out of Unity's EditMode run, or make it cheap under Mono, whichever fits `docs/TESTING_LAYERS.md`. Say in that doc
which. Check the other WorldTwelve tests' times under Mono while there and treat any over ~10 s the same way.

## 2. #182: main-thread culling, 2.2 ms on the nine

The profile (`docs/perf/2026-11-09-profile.md`) put main-thread rendering at 4.1 ms on the nine standing still,
2.2 ms of it culling, from too many Terrains. `perf-fix-1` (a2285404) since made the nine draw ahead only the
chunks within the far ring's known radius, so measure again first: the number may have moved.

Then cut what's left: Terrains (and other renderers) that the haze or the drawn tier hide shouldn't reach the
culler at all (disabled, or not made), and what's on screen should cull cheaply. The look must not change: a
before and after still from the same spot, time stopped. Don't change what's active (lockstep state); this is
view work.

Measure in the Editor until #183 lands (dev's player can't load the nine; the PC is fixing it on `perf-fix-2`),
then in a development player build if the Mac can make one. Stay out of `ChemGrid.Visuals.cs` (the PC's gas pool,
#178, and later #185 and #186).

## Done

EditMode passes in full on the Mac with no timeout; `dotnet test core` passes; smoke passes on a fresh valley yard
and on the nine; the stills match; a "Fix: culling" heading in the profile doc with before and after. Self-land
with `land.sh auto perf-culling-182 --compiled`; the lead closes the issues from the report.
