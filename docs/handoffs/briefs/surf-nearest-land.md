Brief from the PM: the surf pushes toward the nearest land (GitHub #23).

You're a worker in a VM, running headless. This is your only task. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task or wait loop you started, and don't send anything after it.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../surf origin/dev -b surf-nearest-land` and work there. Copy in the asset packs that a fresh worktree lacks (Starter Assets and the others under unity/Assets that the main checkout has and git ignores). Read COORDINATION.md (lean, and the screenshot-budget rules from 2026-10-31 00:05), `gh issue view 23`, and `WaterCurrents.cs`, `Ocean.cs`, `CurrentRider.cs` and how `ValleyBuilder` bakes the coast.

Today `WaterCurrents` has one `seaward` direction for the whole sea, so the surf always pushes toward the mainland. Andrew wants it toward whichever land is nearest from where you are: near the island, toward the island.

1. **The bake.** ValleyBuilder bakes a coarse "toward nearest land" map over the sea: for each sea cell, the direction to the nearest land cell, from the terrain at sea level. Use a distance transform on the coarse grid, not a search per cell at runtime. Smooth it so it doesn't snap where two shores are equally near.
2. **The push.** `WaterCurrents.At` uses the map's direction in place of `seaward` for the breakers' shove and the drift. The river and the still lake are unchanged.
3. **The waves.** The Ocean shader's swell and breakers run along the same map (a flow or direction texture sampled in the shader), so the waves visibly come in toward the island near it. GPU work, not per-frame CPU.
4. Each machine still pushes its own wizard (physics only, never the chemistry, D4).
5. **Tests:** the map's direction near the island points to the island, near the mainland to the mainland, and the probe `current` (ProbeCurrent) still passes. Add a case off the island if the probe can stage it.

Checks: a Unity batch compile that's clean, with no new warnings, and the EditMode tests if you can run them. The host looks at the waves later.

Deliver: commit and push `surf-nearest-land`, not merged. Final message, 6 lines or fewer: the commit, how the map is baked, push and waves, the tests, the time, what the host should look at.
