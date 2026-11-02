Brief from the PM: draw houses from their parts, so asking Claude for a house shows in the game (J-059, J-062). PC, in the Editor.

Start from `origin/houses-core` (35ab489) merged with latest dev. u1 built it in the core and MCP with no Unity, so Chemistry hasn't compiled there yet: I land it with your work. Read `docs/design/builders.md` section 4, `docs/RULES_MCP.md` "Buildings", and u1's list:

- The view reads `World.BuildingsNow` and each `BuildingRow.Json` (joins, walls, roofs, parts, finishes), plus `Rules.Buildings.Row(table, id)["look"]`.
- Handle `BuildingsChanged`: redraw `Ids`, and use `Covers` with `NearestFree` to push players, since they aren't core state.
- Set `Workbench.WorldDesk` to an `IWorldDesk`: `find` and `find_site` must read the real valley.
- Draw the new `door` terrain. Move `ValleyShape.Cabins` to read `site`.
- `BuildingMesher` draws wall segments, joins, openings, roofs (gable, hip, shed, flat with parapet, pitch, overhang) and the detail families from rows. Stucco finish and a tile cover are needed for the test house; barrel-tile pattern if it's cheap.
- An Editor test that the villages' base rows match `ValleyShape.Villages`: u1 tested that on synthetic land only. Village looks and cells must not change: before and after shots.

The test, through the real MCP, with a stand-in client in a real player, and on two peers: the stucco house on the lake's north shore, "add a wing", "make it a shack", revert, then a save and reload. Clips of each for Andrew. Frame time near a dozen new houses.

Known: a wing on a cabin is a closed solid box, because a cabin has no walls to join. Say how it looks. u1 notes the rules fingerprint moved, so existing saves are refused: tell me how a player sees that, and whether a plain message is needed.

Gates, `roomopen`, `online`, `smoke`, new `houses` probe. `git diff origin/dev...HEAD --stat` before pushing. Close only windows proven yours. Push, not merged. Report.
