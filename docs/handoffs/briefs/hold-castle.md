Brief for the Mac: the Hold, phase 1, the castle. Read `docs/design/the-hold.md` (Where, Rules for all of it, phase 1), then `docs/design/builders.md` section 4. Branch `hold-castle` off `dev` (60d79fbe or later). `maps-builder` isn't landed yet; it shouldn't touch your files, but say if it does.

**Goal:** a grand castle on the plateau west of Hollowmere, built from data, standing on its own. No fight, no raiders, no hands yet. This is the set piece Andrew wants to walk around first.

**First, one short message before building (a site note, not a report):**
- The exact spot and footprint, within about x 88 to 128, y 350 to 390. Use `find_site` and the land.
- What the `r` cells at x 45 to 86 are. Andrew remembers a short river behind the hilltop: is that it, and can the castle sit with it at its back?
- What the building tables can already do, and the new row kinds you'd add. Then carry on unless the answer changes the plan.

**Build:**
- Curtain walls, towers, a gatehouse with a timber gate that can open, a keep you can walk into with a room for attuning (empty for now), and a way up to the wall tops so wizards can stand and fight there.
- Rows in the valley's `buildings.json`, growing `rules/buildings.json` and the view's drawing of rows where they fall short (round towers, battlements, gatehouse). Nothing named "castle" in code: a mod must be able to build another one from the same rows (J-055, J-057, J-058).
- Real materials: stone that takes battering, a gate that burns.
- A road from the gate down the hill to the foot of the slope.

**Keep:**
- `land.json` untouched: use foundations for the 3 to 5 m. If the land must change, stop and say why first, because it moves the map hash.
- The valley otherwise as it is, no golden moved, the defense field and the sandbox's controls untouched. If a golden must move, stop and say why.
- Add any new word to `docs/GLOSSARY.md` first. The view decides nothing shared (D4).

**Tests:** the rows pass `MapCheck` and the building checks, the castle's cells are what the rows say, a mod can add a second one, the gate burns, and a wizard can reach the keep and the wall tops on foot. Add a probe that walks them.

**Report:** one short message: the commit, gates (with exit codes), and a numbered list for the PC: the look from the valley floor, the slope and the walls, and the frame cost. Include what you couldn't judge without a real GPU. Don't merge it. Ask before anything much larger than this brief.
