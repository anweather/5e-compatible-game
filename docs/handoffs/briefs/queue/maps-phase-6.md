Needs: unity
Area: ChemYardBuilder, ValleyBuilder, ValleyTerrain, the scene's terrain and place roots (unity/Assets/Prototype/Scripts
and Scripts/Editor); world/maps/valley read only
Branch: maps-phase-6

# Maps as data, phase 6: the scene lets go (critical path for the world squares; the lead, 2026-11-08)

`docs/design/maps-as-data.md` row 6: terrains out of the scene, place roots, `ValleyTerrain` retired, so nothing in
the scene assumes the valley. Read that doc's phase 6 and "what the scene still holds" before starting.

Why now: the world squares (J-080, `docs/design/world-squares.md`) need phases 6 and 7 before a second world square can
be drawn, and the landmark work just hit it. On the Mac's phase 1b, a cove or stamped lake can't show, because the
valley's sea and beach come from `ValleyCoast`, not from `land` (that's phase 7, which follows this one).

Keep the valley looking and playing exactly as it does now: the same probes pass (`smoke`, `online`, `hold`, `hitch`,
`landshapes`, `stamplooks`), with 0 console errors; before/after shots from the town and the castle at 1280 px, checked
at 480 px. Start-up time measured before and after. Before landing: `dotnet test core`, EditMode N/N in full. Push the
branch only, remove this claim on the branch in the last commit. The lead lands it. Phase 7 (the sea from `land`, the
shore map, the level from `map.json`) is the next brief.
