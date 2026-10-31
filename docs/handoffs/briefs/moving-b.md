Brief from the PM: casting and moving, from Andrew's playtest (GitHub #6, #12, #18), plus two loose ends from the half-built merge.

You're a worker in a VM, running headless. This is your only task. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task or wait loop you started, and don't send anything after it.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../moving origin/dev -b moving-b` and work there. Copy in the asset packs that a fresh worktree lacks (Starter Assets and the others under unity/Assets that the main checkout has and git ignores). Read COORDINATION.md (lean). `gh issue view <n>` has each issue. Another worker is doing charged casts (#7) in SpellCaster at the same time, so keep your changes there small.

1. **#6 End Shapeshift whenever you like.** The old key (2) no longer works, and nothing else does; only a world reset ended it. Give it a way out that fits the element-casting controls: casting anything, or a dedicated button, whichever fits best (say which), on both keys and pad. It works online too.
2. **#12 Ice keeps your momentum.** Running onto ice slows the wizard down. On ice the wizard should keep its speed and slide: low grip, slow to turn and stop, and no speed loss on entry. Frost's glaze (the frost cover) feels the same, and so does snow glazed by Frost.
3. **#18 A punch.** A quick punch on Square (pad) and on F (keys) for now; keep the binding in one place, since Andrew may move it. It hits what's just in front through the same force a thrown prop uses, so it can crack and then break a window pane, knock a crate and nudge a ball. It doesn't fire while carrying something, or mid-cast.
4. **Snow slows the wizard.** The rules slow walkers on snow (0.85, a bank 0.7), but `SurfaceEffects` only reads thorns and terrain speed. Make the wizard slow on snow too.
5. **One coil opens every gate.** Any powered machine opens every gate in the world. Link a coil to its own gate, and the world/puzzles.json gate to its coil, in both engines, with a test where two puzzles don't open each other.

Tests: plain C# tests for the logic you can test without Play mode, and core and sandbox tests with shared goldens for 5.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, and a Unity batch compile that's clean, with no new warnings.

Deliver: commit and push the branch `moving-b`, not merged. Final message, 7 lines or fewer: the commit; one line per item; the tests; the time; what the host should feel-test.
