Brief from the PM: fog weather in patches, not a blanket (GitHub #28; Andrew, 2026-10-31).

You're a worker in a VM, running headless, with no Unity: core and sandbox only. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../fog origin/dev -b fog-patches` and work there. Read COORDINATION.md (lean), `gh issue view 28` (the numbers), docs/perf/2026-10-31-mac.md (fog), and docs/GLOSSARY.md's fog weather entry.

Today fog weather's `Veil` lays a unit on open cells at 0.05 a tick, and fog decays at 0.015 a tick, so about 77% of open cells hold fog (about 220,000 cells at peak). That costs ticks: storm and fog run the world at 59% speed. Andrew picked patches: fog here and there, as the glossary says.

1. **Patches.** Fog weather lays fog in patches: low ground, and near water first. It's seeded and deterministic (the same patches on every machine), as data in sim.json: how many patches, their size, and a preference for low and wet cells. Aim for under about 10% of open cells holding fog at peak, and say the number you reach.
2. **Patches drift a little with the wind** and thin when the stretch ends, as now.
3. **Line of sight** through a patch is still blocked (`World.LineOfSight`).
4. **Measure the tick:** the fog tick's cost before and after, on the same map, in the core. Say the numbers.

Both engines (C# core and TS sandbox), with shared goldens; update the goldens that move, and say which.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`.

Deliver: commit and push `fog-patches`, not merged. Final message, 5 lines or fewer: the commit, the patch rule, fog cells at peak before and after, the tick cost before and after, the tests.
