Brief from the PM: Fireball's aim, and the Sleet Storm rules question (docs/BACKLOG.md priority 7).

You're a worker in a VM, running headless. This is your only task. Your final message is your report, and it goes straight back to the PM.

Setup: in ~/5e-compatible-game, run `git fetch`, then make your own worktree: `git worktree add ../aim origin/dev -b fireball-aim`, and work there. First read COORDINATION.md (lean) and playtest/sessions/0.1.8.md, "Observations" item 1 and the "Triaged as expected" paragraph.

1. **Fireball's aim.** In playtest run 811, a Fireball cast at cell 366,370 changed nothing there, yet the log said "Fire takes hold of the grass". Find out which it is:
   - the ball bursts somewhere other than the aimed cell (the projectile's path, its burst point, a scatter of debris landing elsewhere, rounding between world position and cell);
   - or the bot's check watches the wrong cell (the aimed cell, rather than the burst's area).
   Reproduce it in the core with a test: aim at a grass cell, cast, and check where the fire takes hold. Fix whichever is wrong, in both engines (C# core and TS sandbox) with a shared golden if the rules change, or in the bot's check (Scripts/Playtest) if that's what's wrong.
2. **Sleet Storm** on stone (the tower) and on the defense field did nothing in the bot runs. Since 0.1.5 the open question has been whether it should. Say what the rules do now: cold on stone, on dirt, on a covered cell. Then say whether that matches docs/SPELL_COMBOS.md and 5e's Sleet Storm, which makes the ground difficult terrain and puts out flames. If it's clearly a gap (sleet on bare ground should leave the slippery frost cover), make it so through the material verbs, in both engines with a golden. If it's a design question, don't change anything; describe the choice in two lines.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, and a Unity batch compile that's clean, with no new warnings.

Deliver: commit and push the branch `fireball-aim`, not merged. Your final message is the report, 5 lines or fewer: the commit, what was wrong, what changed, the tests, the time.
