Brief from the PM to the Mac: design maps as data, with terrain through the MCP (Andrew, 2026-11-04). Design only, no game code, no Unity job unless a question can't be answered by reading. Start after the Haiku run and the runner commit. Frugal: no sub-agents, no clips, a report of a few lines.

Andrew wants a second map really working, and shaping the land through the MCP as part of the same work. Start from `docs/design/second-map-2026-11-02.md`, issue #69, branch `second-map` (afff4c9, The Drowned Chain), cleanup item 6 (`MapPlan`, `docs/design/cleanup-review-2026-11-02.md`), and `docs/design/world-acts.md` ("The water level", "Real mountains"). Check #69's findings against dev: two days of landings have passed.

Write `docs/design/maps-as-data.md` on branch `maps-design` off dev. It must answer:

1. **The land as data.** One format for a map's land, so a map is a folder under `world/maps/` and the valley is just the first one. #69 says a shipped height file avoids the unrepeatable bake: is that right, and what becomes of the 81 baked terrains and the cell map in the scene? How does a player load a map at run time, and what does it do to start-up time and build size?
2. **Identity.** Map id and hash in approval, the checksum and late join; what a mismatched peer sees. Lockstep stays: physics decides nothing, integer state, no golden moves without saying so.
3. **Terrain through the MCP.** The same data, two ways in: changing the land live for everyone (through `act`, with `undo_act`) and shaping a map to keep. How the drawn land and the cells stay one thing when the ground moves, what it costs per edit, and what the -6 to +8 height step range needs to change for real hills and mountains. The water level too, if it falls out of the same work. Primitives, not features (J-055): a mountain is a shape applied to height, never a mountain tool.
4. **Places.** Court, bridge, tower, Defense field, falls, coast are code: which must become data for a second map to work, and which can a map just leave out? `find_site` must stop offering a lake that isn't there.
5. **Failing loudly.** #69's silent failures (the 38 m drop, the 4.4 km fall, swapped data with no bake): what checks catch them, so a probe can't go green on the wrong world.

Then the order of work: phases of a few days each, each landing on its own with dev still playable, the size of each, which need the PC's GPU and which the Mac can do, and what collides with the PC's MCP work (backlog 30). The Drowned Chain is the test of the tools, not a feature. A map inside a mod is out of scope: note only what not to close off.

End with questions for Andrew, each with your recommendation. Keep the doc under 200 lines, in the words of `docs/GLOSSARY.md`; add new words there.

Push `maps-design` only. Report: the commit, the phases with sizes, and the questions.
