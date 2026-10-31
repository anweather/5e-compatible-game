Brief from the PM: carry out five of Andrew's design calls (COORDINATION.md, 2026-10-30 21:00).

You're a worker in a VM, running headless. This is your only task. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task or wait loop you started, and don't send anything after it.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../calls origin/dev -b andrew-calls` and work there. Copy in the asset packs that a fresh worktree lacks (Starter Assets and the others under unity/Assets that the main checkout has and git ignores). Read COORDINATION.md (lean). `gh issue view <n>` has each issue.

The host is doing UX pass 4 at the same time, on the cast bar's look, the HUD and the targeting markers. Keep your Unity changes to the logic, input and rules data, and leave how the meter and HUD are drawn alone.

1. **Charged casts cost time (#7).** Each charge level adds cast time, so a full charge takes about 2 s and a tap stays the fastest. The charge only grows while the meter runs, so holding past it no longer comes for free. Make the time per level data, not a constant, and say what you chose.
2. **Raise Earth held is a strong raise.** Remove Lower Earth as Raise Earth's held form, so full charge raises higher (charge 3: a tall pillar or mound). Digging stays with the pick, and with Raise Earth cast while aiming at your own feet if that already digs; say what digging is left.
3. **Zero health: downed, then respawn (#14).** At 0 HP the wizard falls and drops what it carries. About 3 s later (data, not a constant) it comes back at the yard with full health, with a log line. It works online for every player. It's not the world reset.
4. **Sleet Storm makes a slippery field, as in 5e.** A wide frost glaze over its area (the same slippery frost cover Frost leaves) that makes creatures slide and puts out fires. Do it through the material verbs: Sleet's cold strong enough over its area, not a rule for one spell. Both engines, with a shared golden.
5. **The punch goes on Circle (pad) (#18).** Punch with Circle when not casting; while casting, Circle (B) still cancels. F stays the key. Square goes back to picking up and sitting only.

Tests: plain C# tests for 1, 3 and 5's logic; both engines with shared goldens for 2 and 4 (and 1 if the rules' timing changes).

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, and a Unity batch compile that's clean, with no new warnings.

Deliver: commit and push the branch `andrew-calls`, not merged. Final message, 8 lines or fewer: the commit; one line per item; the tests; the time; what the host should feel-test.
