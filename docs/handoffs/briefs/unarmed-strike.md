Brief from the PM: the unarmed strike in the core (Andrew, 2026-10-31: "Unarmed strike good"; JUDGMENT_CALLS J-045; GitHub #18).

You're a worker in a VM, running headless, with no Unity: core only (the sandbox is frozen; don't touch it). Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../unarmed-strike origin/dev -b unarmed-strike` and work there. Read the root CLAUDE.md, `unity/Assets/Prototype/Chemistry/CLAUDE.md`, and how damage and commands work today (`Defense.cs`, `Effects.cs`, `Breath.cs`, the command types, how a downed wizard works in sim.json `downed`). Note: u1 is working on live rule changes in the core at the same time (mods, rule switches, the world log), so keep away from those files and say if you had to touch one.

Today the punch is view-only physics: it shoves props and cracks panes on the puncher's machine (`unity/.../Scripts/ItemHolder.Punch.cs`). Andrew wants punching other players to count, as the SRD 5.2.1 unarmed strike. Build the core half, deterministic and in the checksum:
1. **A strike command** (attacker, the aim), resolved in the tick: the target is the nearest wizard or entity in reach in front, using the core's own positions, never Unity physics.
2. **The hit:** the SRD unarmed strike: an attack roll (d20 + Strength modifier + proficiency, against the target's AC) from the world's seeded RNG, then 1 + Strength modifier bludgeoning damage. Use the core's existing ability scores, AC and damage paths if they exist; if a wizard has no Strength yet, add it as data with a sensible default and say so. Downed rules apply as for any damage.
3. **Knock-back:** a hit pushes the target by data (start at about 1 cell); props hit by a strike get the same push in the core if props live there, otherwise leave props to the view.
4. **Numbers as data** in `rules/` (reach, knock-back, cooldown in ticks), not constants in code. Name any new words in docs/GLOSSARY.md first.
5. **A log line** in the game's words: "Ana punches Bo: 2 bludgeoning damage", "Ana's punch misses Bo".
6. **Tests:** hit and miss with a fixed seed; damage 1 + Strength modifier; knock-back; downed at 0 HP; two worlds side by side give the same hash.
7. Say exactly what the view needs to call (the command and when), so DesktopUnity can wire it into the new IK punch.

Checks: `dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes`, jscpd as in CLAUDE.md.

Deliver: commit and push `unarmed-strike`, not merged. Final message, 10 lines or fewer: the commit; one line per item; the defaults you picked; the tests; the time.
