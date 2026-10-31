Brief from the PM: swimming with breath and drowning (docs/BACKLOG.md item 13; priority 4).

You're a worker in a VM, running headless. This is your only task. Your final message is your report, and it goes straight back to the PM.

Setup: in ~/5e-compatible-game, run `git fetch`, then make your own worktree: `git worktree add ../swim origin/dev -b swim-breath`, and work there. First read COORDINATION.md (lean, GPU-first), docs/BACKLOG.md item 13, and the swimming code (`SurfaceEffects`, probe `swim`).

1. **Diving:** while swimming, a dive input (crouch, or the jump button held, whichever fits the controls best; say which you picked) takes the wizard under the surface, and releasing it brings them back up. Underwater movement is slow, and the wizard sinks toward the bottom unless swimming up.
2. **Breath:** a breath meter that runs down only while the head is under water. Start at about 30 s of breath; it's a game-feel number, so make it data, not a constant. It refills quickly at the surface. Put it beside the existing vitals (`Vitals`; stamina shows only while used), and have it appear only while it's draining or refilling.
3. **Drowning:** at zero breath, damage comes in ticks (1d6 every few seconds is fine), with a log line. It stops as soon as the head is out. A frozen surface over the wizard blocks surfacing, so Frost over a swimmer is a trap, and that's on purpose.
4. **Online:** each player's breath is their own wizard's state; use the same path the other vitals use online.
5. **Tests:** the breath and drowning logic goes in a plain C# class, so it can be tested without Play mode, with tests next to the existing ones.

The swim animation is out of scope: the host does the looks later.

Checks: `dotnet test core/Game.Core.sln` (if you touch the core) and a Unity batch compile that's clean, with no new warnings. The Unity EditMode tests and Play mode run on the host later.

Deliver: commit and push the branch `swim-breath`, not merged. Your final message is the report, 5 lines or fewer: the commit, what changed, the tests, the time, and anything the host must check in Play mode.
