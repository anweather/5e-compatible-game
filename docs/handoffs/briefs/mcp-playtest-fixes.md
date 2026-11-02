Brief from the PM: fix what the first play with Claude in the game found (issue #52, `docs/playtests/2026-11-01-mcp.md`). Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../mcp-playtest-fixes origin/dev -b mcp-playtest-fixes`.

In the core and the MCP only:
1. **Live state first.** Drafts, `describe` and `apply`'s diff read the rules in force (base plus every mod on), not the base. `describe` says which mods touched a value. A tool lists what's on now in plain words.
2. **Refuse at once.** `set_material` and friends reject an unknown field when it's set, as `validate` does.
3. **Limits up front.** Radius caps and the hue-only rule appear in the tool descriptions and Instructions, not only on refusal.
4. **What's outside the rules.** A tool or Instructions section lists what Claude can't change yet (heat glow, player size, fauna size, slippery terrain, weather's cover, area fill, buildings), kept in one place, so Claude needn't read source.
5. **Clock and weather.** Read tools for time and weather, and `set_time` and `set_weather`, as commands through the room with lockstep safe. If the core or the room needs view work, define the interface and say so.
6. A new spell: `add_spell` says whether it's reachable by a player, and how.

Goldens must not move except where a new command needs one. Gates in CLAUDE.md. Check `git diff origin/dev...HEAD --stat` before pushing. Deliver: commit and push, not merged. Final message, 10 lines or fewer: the commit, what's fixed, what the view must fill in, what's left out, gates, time.
