Brief from the PM: the Dragon's rules (docs/BACKLOG.md item 26).

You're a worker in a VM, running headless, with no Unity: core and sandbox only. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../dragon origin/dev -b dragon-rules` and work there. Read COORDINATION.md (lean), BACKLOG item 26, and the existing reset code (the online reset, net-reset; `ResetYard`; World.Save).

Build the rules half, in both engines (C# core and TS sandbox), with shared goldens.
1. **The event as rules.** A `DragonCommand` (or a world event) starts a seeded, deterministic flight with these phases:
   - **warning:** about 30 s, so the view can darken the sky;
   - **passes:** long sweeps over the valley, the path from the seed;
   - **leaving.**

   Every player's world runs it the same, on the same ticks, and it's in the checksum and the save.
2. **The reset follows the breath.** Each chunk under the breath on a pass resets to the valley's starting state: terrain, covers, water, fire, gas and props as built. Chunk by chunk, sliced across ticks like the existing tick-spread, so no frame does the whole valley. By the last pass, the whole valley is fresh. Play areas built at startup (the golf course, the puzzles, the Gauntlet if it's there) come back as built.
3. **Knock-down.** A creature or wizard in the breath's cells is downed (the downed system), unless sheltered: under water, or behind a stone wall on the breath's side. Keep the shelter rule simple and say what it is.
4. **Triggers.** The Reset command summons it (host only online). It also comes on its own when the world has changed too much: a changed-cell budget in sim.json (a count of cells that differ from the starting state, kept cheaply as cells change). When the budget is passed, the warning starts.
5. **Players keep their progress:** the grimoire and player saves (item 21) aren't touched.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, a golden of a full event on a small map, and the time a full sweep takes per tick (no single tick over the tick budget).

Deliver: commit and push `dragon-rules`, not merged. Final message, 8 lines or fewer: the commit; one line per item; the tests; the time.
