Brief from the PM: the Gauntlet's rules (docs/BACKLOG.md item 25; docs/handoffs/obstacle-course-brief.md).

You're a worker in a VM, running headless, with no Unity: core and sandbox only. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../gauntlet origin/dev -b gauntlet-rules` and work there. Read COORDINATION.md (lean), the obstacle-course brief and docs/SPELL_COMBOS.md ("Through the material system").

Build the rules half of the slice, in both engines (C# core and TS sandbox), with shared goldens. Andrew wants it creative but small: prove the idea.
1. **Spell zones.** World data names an area and its allowed elements or spells. A cast whose spell isn't allowed fizzles, with a log line ("The booth's charm smothers your Fireball"). It's reusable anywhere in the valley, works online, and is in the checksum.
2. **Checkpoints.** Areas with a checkpoint: downed inside the course means back at the last checkpoint reached, not the yard (the downed system, sim.json `downed`). Per player.
3. **Timer and best times.** Start at the gate, stop at the bell, a best time per player, saved, and a log line like the golf's.
4. **Booth rules, as material verbs, not rules for one spell:**
   - **Crust that re-melts:** lava hit by cold crusts to walkable rock that turns back to lava after N ticks, as data.
   - **The ice wall:** fire melts it to meltwater that floods the cells below and re-freezes into a slippery slide.
   - **An updraft cell kind:** air over it lifts props and wizards.
   Say which already exist and only need data.
5. **The course in the world data:** a gate, 3 or 4 booths in a row, and a bell, on the lake's far shore, clear of the island, the golf course and the gate puzzle. Pick the spot from the world files. Booths, in order:
   - the lava pit (Water and Air only);
   - Misty Step gaps;
   - the ice wall (Fire only);
   - the geyser launch.

   The side camera and the looks are for the host.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`.

Deliver: commit and push `gauntlet-rules`, not merged. Final message, 8 lines or fewer: the commit; one line per item; the tests; the time.
