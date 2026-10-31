Brief from the PM: finish two tasks that Pro workers started before Pro hit its usage limit.

You're a worker in a VM, running headless. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task or wait loop you started, and don't send anything after it. Read COORDINATION.md (lean) first.

1. **Moving batch, from `wip/w1-moving-b`.** Set up the branch:
   `git fetch; git worktree add ../moving origin/wip/w1-moving-b -b moving-b`, then rebase onto origin/dev.
   Copy in the asset packs that a fresh worktree lacks (Starter Assets and the others under unity/Assets that the main checkout has and git ignores).
   Read the brief it was working from, docs/handoffs/briefs/moving-b.md, and the WIP commit's diff to see what's done. Finish the rest:
   - #6: end Shapeshift whenever you like.
   - #12: ice keeps your momentum.
   - #18: a punch on Square and F.
   - Snow slows the wizard.
   - One coil per gate, with a test.
   Checks as that brief says. Push `moving-b`, not merged.
2. **Then the boulder (#21), from `wip/w2-playtest-rules-a`.** It's on top of the wave fix, which is already on dev.
   `git worktree add ../boulder origin/wip/w2-playtest-rules-a -b boulder-dir`, then rebase onto origin/dev.
   Rolling Boulder rolls only north, south, east or west. Make it roll along the aimed direction (and downhill from there) the way the wave now does, deterministically and the same in both engines, with a shared golden. If they share a mover, fix it there.
   Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, and a Unity batch compile that's clean. Push `boulder-dir`, not merged.

Final message, 8 lines or fewer: both commits; one line per item; the tests; the time; what the host should feel-test.
