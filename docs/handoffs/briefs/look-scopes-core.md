Brief from the PM: looks that apply to a place or around a player, and looks of several colours, in the core and the MCP. Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../look-scopes-core origin/dev -b look-scopes-core`. Read `docs/design/world-tools.md` (G1 and "Scoped and mixed looks", which is this job), `docs/RULES_MCP.md` ("Looks"), `Workbench.Look.cs`, and how mods reach the room.

1. **Places (G1's first piece):** `world/places.json`: named landmarks with their cells, the villages and the lake and river among them, generated from what the world data already says wherever it can. A read-only MCP tool `places()`. Mods can't change them yet.
2. **`here()`:** a read-only tool for where the player is and what's near. The core can't know the player, so define the small interface the game fills in, with a stand-in for headless runs. The Unity side is the PC's.
3. **`where` and `mix`** on a look, as the design says, through `set_look`, `describe` and `validate`: on elements, covers, gases and terrains. Refuse a place that doesn't exist, and list the places. `around: "me"` names the asking player's slot.
4. **A pure function the view will call:** the look for a cell given the active scoped looks and player positions, with deterministic `mix` picks from a hash of the cell, no RNG draw, no allocation per call. Test it: scope edges, overlap (latest wins), a spell's own colour wins, revert, `mix` weights over many cells, identical on two runs.
5. Update `docs/RULES_MCP.md`, and the Instructions text Claude reads, so "make the fire near me green" and "fire round the village in three colours" are clear asks.

Looks stay out of `Rules.Fingerprint`: goldens must not move. No named content in code (J-055).

Checks: the gates in CLAUDE.md (core build with -warnaserror, tests, format, jscpd). Deliver: commit and push `look-scopes-core`, not merged. Final message, 10 lines or fewer: the commit; what each tool now does; the interface the view must fill in; anything left out; the gates; the time.
