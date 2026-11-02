Brief from the PM: houses from parts, through the MCP, in the core. The first test of building part of the game by asking and keeping it (J-059, J-062). Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../houses-core origin/dev -b houses-core`. Merge `mcp-playtest-fixes` if it isn't on dev yet and you need it, and say so. The design is `docs/design/builders.md`, section 4, with its order of work; `docs/design/world-tools.md` G1, G3 and G5.

Build, in the core and the MCP:
1. **Rows:** join points, wall segments, openings, floors, roofs, styles, in rules and world data, read and checked. No named content in code (J-055).
2. **The rasteriser:** a building's parts to cells, outside only (option (a)), hollow by default, with villages' cells and goldens unchanged. A golden pins one building row.
3. **Mods reach the world:** a `world` section: edit one building by id, add a building at cells. Through the room: validated by every peer, laid at the switch tick, in the cell hash, reverted, replayed for a late joiner, kept in the world save. A site check refuses things, other buildings and water; a player or raider on a new wall is pushed to the nearest free cell, logged.
4. **MCP tools:** `find` for buildings and things, `find_site`, and draft tools so "add a wing to the house by the lake" and "build a house on the beach" are one mod. `save` keeps it. Update the Instructions and `docs/RULES_MCP.md`.
5. **Tests:** both asks from a headless client, ending as one mod that survives save and reload, and identical on two runs. The stucco house row from the design as a test fixture only.

The view (drawing from parts) is the PC's: state exactly what it must read. Goldens stay, other than new ones. Gates in CLAUDE.md. Check `git diff origin/dev...HEAD --stat` before pushing. Deliver: commit and push, not merged. Final message, 12 lines or fewer: the commit, what works, what the view needs, what's left out, gates, time.
