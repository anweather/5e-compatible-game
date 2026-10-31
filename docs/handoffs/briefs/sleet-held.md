Brief from the PM: Sleet Storm as Frost's held form (Andrew, 2026-10-30).

You're a worker in a VM, running headless. This is your only task. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task or wait loop you started, and don't send anything after it.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../sleet origin/dev -b sleet-held` and work there. Read COORDINATION.md (lean) and rules/combos.json.

1. Water then Air (Frost) gets `"held": "sleet-storm"` in rules/combos.json, the way Wall of Stone has `"held": "stone-ramp"`. So holding Frost to full charge casts Sleet Storm's slippery field (power 4, from bd488d5). A tap or a part charge stays Frost.
2. Check that both engines pick the held spell the same way, and add a shared golden: Frost held to full charge glazes the area the way Sleet Storm does.
3. Update docs/SPELL_COMBOS.md's Frost entry: "held: Sleet Storm, a wide slippery field".

This VM has no Unity: core and sandbox only, and say so.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`.

Deliver: commit and push `sleet-held`, not merged. Final message, 4 lines or fewer: the commit, what changed, the tests, the time.
