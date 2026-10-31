Brief from the PM: three rules bugs from Andrew's playtest (GitHub #11, #15, #13; docs/PLAYTESTS.md, 2026-10-30).

You're a worker in a VM, running headless. This is your only task. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task or wait loop you started, and don't send anything after it.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../rules-a origin/dev -b playtest-rules-a` and work there. Copy in the asset packs that a fresh worktree lacks (Starter Assets and the others under unity/Assets that the main checkout has and git ignores). Read COORDINATION.md (lean) and docs/SPELL_COMBOS.md ("Through the material system"); `gh issue view <n>` has each issue.

1. **#11 Wave.**
   - It travels only north, south, east or west. Make it travel along the aimed direction at any angle, deterministically: integer or fixed-step cells, the same in both engines.
   - Leave water behind where it passes and where it breaks (shallow water that soaks in or runs off as water does now), not only mud.
   - Keep it carrying props, putting out fires and knocking creatures back.
2. **#15 Brick on water.** Water then fire on a pool left a brick texture on the water. Find why; likely fire or heat baking mud under standing water. Water on a cell should take the heat first (it boils), so nothing under the water bakes while the water is there. Fix it through the material verbs (an order or condition in materials.json or reactions.json), not a rule for one spell. If the brick look is only the view drawing the ground under the water, say so and fix the view's condition instead.
3. **#13 "rotten turkey".** A live, running turkey is named "rotten turkey" in the log. Find where creature names pick up "rotten" (a status, a material, or the corpse's name used while alive) and fix it, so only a dead, rotted one is called rotten.

Both engines (C# core and TS sandbox), with shared goldens for 1 and 2; say which existing goldens moved and why.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, and a Unity batch compile that's clean, with no new warnings.

Deliver: commit and push the branch `playtest-rules-a`, not merged. Final message, 6 lines or fewer: the commit; each issue's cause and fix; the tests; the time.
