Brief from the PM: a design, no code, for the game's material primitives. Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../primitives-design origin/dev -b primitives-design`. Read J-055 and J-057, `docs/design/world-tools.md`, `docs/RULES_MCP.md`, D4, D9 and D13, `rules/CLAUDE.md`, `rules/materials.json`, `rules/reactions.json`, `rules/sim.json`, `Cell.cs`, and the parts of `World.Sim.cs` and `World.React.cs` that special-case a material by name.

Andrew, 2026-11-01: "I don't wanna hard code the world. I wanna find some primitives that can be modified to creat convincing effects. Maybe creating new temporary materials from the primitves of the game?"

Write `docs/design/primitives.md`, under 180 lines:
1. **What the primitives are today.** Every property a material, cover or gas can have in data (burn, float, soak, dissolve, conduct, speed, tags, look...) and what each does. Then the honest list of what's hard coded by name or by special case in the core: water above all, and fire, ice, lava.
2. **Effects as a test.** Walk at least paint, tar or honey, lava, quicksand, a slick of ice, glowing moss and acid rain through them: which are one combination of data today, and what stops the rest. No named content in code.
3. **The smallest set of new primitives** that unlocks whole families, each justified by several effects. Liquids are the main question: can "a cell holds water" become "a cell holds a liquid" with a thickness, colour and heat, at a cost that holds the lag fix's numbers, with integer state and D9's seams? Give memory and tick cost estimates, and what happens to saves, the cell hash and the goldens.
4. **Temporary materials.** A material made at run time from primitives through the MCP, in a mod through the room: how it gets an integer id that every peer agrees on, how it lapses (a lifetime, or when the mod goes), what a late join and a save see, what happens to cells still holding it, and limits so a stray mod can't fill the table or stall the tick. Include how it fits "mix": two paints meeting could make a third material instead of one reaction row per pair.
5. **Order of work,** smallest useful step first, and the first test: something asked of Claude that ends as one mod.

Questions for Andrew at the end, 5 at most, each with your recommendation. Propose ADRs by title only.

Checks: docs only. Deliver: commit and push `primitives-design`, not merged. Final message, 12 lines or fewer: the commit; the new primitives you propose; how temporary materials work; the liquid cost; the questions; the time.
