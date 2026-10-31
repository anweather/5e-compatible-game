Brief from the PM: live rule changes, the core foundation (Andrew's next flagship; docs/RULES_MCP.md "Change modes" and phase 3; D13 amended).

You're a worker in a VM, running headless, with no Unity: core only (the sandbox is frozen; don't touch it). Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../live-rules origin/dev -b live-rules` and work there. Read docs/RULES_MCP.md (all), docs/MULTIPLAYER.md (how rule switches and commands travel today; sections only), `ModValidator.cs`, and how the core applies a mod today (grep `WithMods`, rule switch, `EffectCommand`).

The idea: each player's Claude talks to the MCP of their own game; a change crosses the network only as a mod patch in the existing rule-switch path; every peer checks it itself before switching it on at the tick the host scheduled. Build the core half, deterministic and in the checksum:
1. **Change mode** in the world state: Open (anyone's change goes in), HostOnly, Locked. Set when the world is made; saved with it. Name the words in docs/GLOSSARY.md first.
2. **Commands:** apply a mod and revert a mod, each naming the peer who asked. The host side decides by the mode (HostOnly refuses other peers, Locked refuses all) with a one-sentence reason; refusals are deterministic too.
3. **Every peer validates:** before switching on at the scheduled tick, each peer runs `ModValidator` on the patch against its own current rules. Same inputs give the same verdict on every peer; a patch that fails is refused everywhere with the sentence. Prove it with a test that runs two worlds side by side (a hostile patch, a good one) and compares hashes.
4. **Active mods:** the world keeps the ordered list of mods on (id, title, who added it, the tick), so `revert` takes one off and rules are recomposed in order. Reverting the middle one of three gives the same rules as applying the other two.
5. **Log line** per change, in the world log, in the game's words: "Ana switched on Big fireball (Open room)", "Revert refused: only the host can change this room".
6. **The MCP:** add `apply` and `revert` and `room` to `core/Rules.Mcp` against an in-memory world (the real game connection is the Unity half, later). `room` reports the mode and the active mods.
7. **Docs:** update RULES_MCP.md's phase 3 with what's built and what the Unity half still needs (the room-creation choice in the menu, the game serving the MCP on localhost, the HUD line). Short.

Budgets and checks reuse `rules/budgets.json`. Keep to D9's seams (data apart from systems, commands as the only way in).

Checks: `dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes`, jscpd as in CLAUDE.md.

Deliver: commit and push `live-rules`, not merged. Final message, 10 lines or fewer: the commit; one line per item; anything that needs Unity; the tests; the time.
