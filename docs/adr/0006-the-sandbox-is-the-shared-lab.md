# D6. The sandbox is the shared lab (2026-10-16)

**Status:** Accepted

- **Decision:** friends play the web sandbox together and change its
  rules live.
  - **Multiplayer:** lockstep over the artifact's `room`. Snapshots,
    experiments and votes go in its `db`.
  - **Mods:** data only, validated, switched on for everyone at the same
    tick.
  - **Promotion:** kept mods are baked into `rules/` (amended below).
  - **Code:** code changes go through the repo, never live.
- **Why:**
  - It is the cheapest place to find out what's fun with other people.
  - The rule format is the same one the Unity `Game.Core` will load, so
    what's proven here carries over.
  - Data-only mods keep friends from running each other's code.
- **Reopen if:** an experiment that matters can't be expressed without
  new verbs often enough that the code loop becomes the bottleneck.
- **Amended 2026-10-25:** a kept mod lands like code, not by PR: a
  branch, verified (tests plus the probe), merged to main. No PR, as for
  all work on this repo (`CLAUDE.md`).

**Amended 2026-10-31 (Andrew): the sandbox is frozen.** It keeps working on a snapshot of the rules
and isn't updated for new ones; the C# core is the only engine. Changing rules live, for players
and agents, moves to the rules MCP (D13), which runs the core.
