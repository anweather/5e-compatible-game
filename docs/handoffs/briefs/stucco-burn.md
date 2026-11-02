Brief from the PM: stucco burns, slower than wood. Andrew, 2026-11-02: "Stucco burns, but slower than wood." (J-063)

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it. This is this VM's first job: say at the start of your report which Claude account you're signed in to (`claude auth status`, plan only, no email or token) and anything about the VM that got in the way.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../stucco-burn origin/dev -b stucco-burn`.

There's no stucco terrain in `rules/` yet: only a test fixture has one (see `docs/design/builders.md` and `docs/RULES_MCP.md` "Buildings"). Add a general `stucco-wall` terrain row by the rules MCP route (D13, `docs/RULES_MCP.md`), with a `burn` that catches and spreads clearly slower than the log wall's, and a sensible look. The finish stays in the building row. Pin the difference with a test: the same fire on log and on stucco, and stucco takes clearly longer to go. A new golden only if it needs one; the fingerprint moves, as a new row does, and no existing golden should. Note it in `docs/RULES_MCP.md`.

Checks: the gates in CLAUDE.md (core build with -warnaserror, tests, format, jscpd). `git diff origin/dev...HEAD --stat` before pushing. Deliver: commit and push `stucco-burn`, not merged. Final message, 8 lines or fewer: account, commit, the numbers chosen and how much slower, gates, time.
