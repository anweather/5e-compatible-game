Brief from the PM: short designs for the bigger asks from Andrew's play test of 2026-11-02 (#73, #74, #75, #76, #60). Max account. Design only: no code.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../playtest-designs origin/dev -b playtest-designs`. Read the issues (`gh issue view <n> --json body,comments`), `docs/ARCHITECTURE.md`, `docs/RULES_MCP.md`, `docs/design/primitives.md`, `builders.md` and `world-tools.md`, and decisions J-055, J-057 and J-058 in `JUDGMENT_CALLS.md`: the world is built from general primitives as data, and examples are tests of the tools, never features.

Write two documents, each under 200 lines, each walking Andrew's real asks through the design to show they'd work:

1. **`docs/design/events-and-placing.md`** for #76, #74 and #75 together, since they share a mechanism. Rules that react to events, starting with a player's step ("a meteor at every step I take"). A spell effect that places creatures and things ("the dock spell, but laying a human child"). Scattering many of a thing over an area ("50 trees near the lake"), seeded so every peer agrees. How each gets into the core as a command (lockstep, D4, D9), what goes in `rules/`, which MCP tools Claude gets, the budgets that keep a mod from flooding the world, and which golden tests would be new.
2. **`docs/design/bodies-and-surfaces.md`** for #73 and #60. Creatures built from parts, beyond today's species rows (a centaur, a half-man half-deer), from `rules/parts.json` and the creature mesher, and how Claude asks for one. Placing things on the water's surface rather than the lake bed (a dock, lily pads, a raft), as a general primitive, and what it changes in cell state and the checksum.

For each: the smallest general design, then the order to build it in branches, each with effort (hours, days, week+) and whether it needs Unity. End each with at most 3 questions for Andrew, each with options and your recommendation. Read the code to ground claims, and cite `file:line`.

Push the branch, don't merge. Final message, 12 lines or fewer: the commit, each design in one sentence, the questions for Andrew, time.
