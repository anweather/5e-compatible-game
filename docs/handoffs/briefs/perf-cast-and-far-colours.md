Done: landed on dev (see git log); don't redo.
Needs: unity
Area: unity/Assets/Prototype/Scripts/ (ChemGrid.Effects.cs and the spell effects' set-up; FarRing*, KnownRing's colours in the view; not ChemGrid.Visuals.cs, ChemGrid.LookScopes.cs, ChemGrid.Rules.cs, CellDressing), docs/perf/2026-11-09-profile.md (a new heading only)
Branch: perf-cast-and-far-colours

# A Fireball's cast frame (#189), then the far ring's colours (#191)

Two parts, each its own commit.

## 1. #189: the cast frame, 37-43 ms in a player

Your finding on `perf-fireball-frames`: the cast frame's Instantiate is BigExplosion (8 prewarmed particle
systems), 5.5 ms a copy once warm; trimming its billboard mesh gave nothing. Turning prewarm off changes how the
blast starts, and that look is not taken. Instead keep a small pool of ready explosions (made once at start-up or on
the first cast, hidden, reused, reset with `Clear` + `Play`), so a cast makes nothing. Check the other effects a cast
makes the same way and pool those that cost over ~1 ms. The look must not change: a frozen still of the blast at
the same moment, before and after. Measure the cast frame and the 5 s after it (worst, mean, p95) on the nine and the
valley, in a development player build (#183 is fixed, so the nine loads in a player), and add a "Fix: cast frame"
heading to the profile doc.

## 2. #191: the far ring paints dry country white

On the nine since `biome-from-sketch` (066b1d44): dry country is brown ground flecked white up close, but the far
ring draws the same land flat white past the volcano (the haze hides the join; with the fog thinned it shows). The
far ring's colour for a biome should come from the same source as the near ground's, so a known chunk's colour
matches the drawn chunk next to it. A still from ~200 m up over the valley's north-east corner, fog thinned for the
shot only, before and after.

The PC is on `perf-fix-3` (`ChemGrid.Visuals.cs`, `LookScopes`, `Rules`) and then the dressing (#194): stay out of
those, and say so if a fix needs them.

## Done

EditMode passes in full (rebuild the yard first so no Untitled scene is open); `dotnet test core` passes; smoke
passes on a fresh valley yard and on the nine. Self-land with `land.sh auto perf-cast-and-far-colours --compiled`;
the lead closes the issues from the report.
