Brief from the PM: houses that burn. PC, in the Editor. First read `docs/handoffs/desktopunity-2026-11-01.md`, then this. Branch `houses-burn` off dev (the `host-ux` worktree is detached).

From issue #54:
1. **Walls that burn or break disappear.** Row houses are drawn from rows, so cells change and the mesh doesn't. A burnt row house must leave a ruin: wall segments follow their cells, openings and floors stay as they should, the roof falls or goes with what held it. Budget it: a redraw on a cell change must not cost a frame hitch in a burning street. Spread it if needed. Do the same for shattered panes and a broken door. Keep it view only.
2. **The roof patch:** find why ground detail shows on the garage roof at the first site, and fix it.
3. **Chimney smoke** of an edited village house.
4. **Look at it properly:** frames from a wizard's height, at the door, inside a hollow house. Walk through a door gap and against a new wall. If the real chat works for a stand-in, use the stand-in client. Also film the details never filmed: viga, canale, porch, parapet.
5. If it's cheap, walls meet cleanly at joins (the trim rule) and `overhang` is honoured.

Test: the stucco house through a stand-in MCP client, then a fireball into it, filmed from the wizard's height: before, burning, ruin, and on two peers. Frame numbers while it burns.

Rules: `git diff origin/dev...HEAD --stat` before pushing. Close only what's proven yours, by pid. Andrew may be on the PC: check `query user` idle time, and keep to hidden Editor work if he's active. Don't use his Claude session. Mac's lanes: radial, pause menu, spell list, Q and E. Gates plus `rowhouses`, `houses`, `roomopen`, `online`, `smoke`, rerun after your last edit. Push, not merged. Report.
