# D4. Physics is authoritative only where nobody has to agree (2026-10-13)

**Status:** Accepted

- **Decision:**
  - **Single-player:** Unity PhysX may decide outcomes (barrels roll,
    enemies slide off ledges).
  - **Shared or server-checked outcomes** (multiplayer, async turns on
    the platform): the truth is `Game.Core`. Physics either:
    - animates outcomes the core already decided, or
    - is run once by the acting client, and its result is **quantised
      into the move** ("barrel came to rest in cell (4,7)"). The server
      then validates plausibility rather than re-simulating.
- **Why:** no Unity physics engine is deterministic across machines
  (see `RESEARCH.md` §9). Chasing fixed-point physics is a research
  project, not a learning project.
- **Reopen if:** the game turns out to need competitive multiplayer
  physics. That would mean evaluating a fixed-point engine, or Netcode
  for Entities prediction on a dedicated server.
