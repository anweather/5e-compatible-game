Brief from the PM: build `triggers-core` from `docs/design/events-and-placing.md` (#76). Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only; the PM lands it with `land.sh`, which runs the Unity compile check you can't. Last time `place-core` and `world-lay` were merged into dev by the worker: don't repeat that.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../triggers-core origin/dev -b triggers-core`.

Build the design's `triggers-core` row: `triggers.json`, the `step` event, `cast` and `place` actions, cooldowns, `add_trigger`, a walk in `simulate`, and `StepTriggerGolden`, so "a meteor at every step I take" works. Andrew's answers (J-067): the mod decides whose steps count, the asker's by default; triggers wait at least 3 ticks between firings, at most 4 fire a tick in a room.

Constraints: the core stays deterministic, steps come in as the existing `Stand` commands, and existing goldens don't move. Don't touch `ChemGrid.cs`, `Workbench.cs`, `RuleSwitcher.cs`, `GameFolders`, `PlayLog`, `GameRoomDesk.cs`, `McpRpc.cs`, `World.Room.cs`, `Workbench.Fauna.cs`, or anything under `Scripts/`. No real prompts. Gates in CLAUDE.md, and `git diff origin/dev...HEAD --stat` before the push. Say exactly what `triggers-view` will need. Final message, 12 lines or fewer: the commit, what works, what the view needs, what's left out, gates, time.
