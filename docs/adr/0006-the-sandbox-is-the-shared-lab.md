# D6. The sandbox is the shared lab (2026-10-16)

**Status:** Accepted

- **Decision:** friends play the web sandbox together and change its
  rules live.
  - **Multiplayer:** lockstep over the artifact's `room`. Snapshots,
    experiments and votes go in its `db`.
  - **Mods:** data only, validated, switched on for everyone at the same
    tick.
  - **Promotion:** kept mods are baked into `rules/` by PR.
  - **Code:** code changes go through the repo, never live.
- **Why:**
  - It is the cheapest place to find out what's fun with other people.
  - The rule format is the same one the Unity `Game.Core` will load, so
    what's proven here carries over.
  - Data-only mods keep friends from running each other's code.
- **Reopen if:** an experiment that matters can't be expressed without
  new verbs often enough that the PR loop becomes the bottleneck.
