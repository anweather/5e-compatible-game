Brief from the PM: build `place-core` from `docs/design/events-and-placing.md` (#74, #75). Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../place-core origin/dev -b place-core`.

Build the design's first branch, `place-core`, as section 7 lists it: `Pick`, the `place` field, `World.Placed`, `World.Summoned`, budgets, `PickGolden` and `PlaceGolden`. A spell can place things and creatures, picked by hash from the world's seed, so every peer agrees. Then, on a second branch `world-lay` off `place-core`, the next row: `lay` in the world section, the switch and revert, `scatter`, `LayGolden`, so "50 trees near the lake" scatters trees rather than building huts.

Defaults for the design's open questions, until Andrew says otherwise: a summoned creature is alive and wanders, built so a still thing is also possible later. Budgets as the design recommends.

Your `creature-scale`, `fauna-rows` and `dragon-tools` aren't on dev yet (the Mac is compiling them). Build off dev, and keep out of `Workbench.Fauna.cs`. If you need `budgets.json` or `RulesTools.cs`, keep each change small, so the merge is easy.

Constraints: the core stays deterministic. Existing goldens don't move; the new goldens are part of the work. Don't touch `ChemGrid.cs`, `Workbench.cs`, `RuleSwitcher.cs`, `GameFolders`, `PlayLog`, `GameRoomDesk.cs`, `McpRpc.cs` or `World.Room.cs`: other branches own them. No real prompts. Gates in CLAUDE.md, and `git diff origin/dev...HEAD --stat` before each push. Say exactly what the PC's `place-view` will need. Final message, 15 lines or fewer: each commit, what works, what `place-view` needs, what's left out, gates, time.
