# D7. Netcode: NGO, client-hosted, lockstep chemistry (2026-10-24, proposed)

**Status:** Proposed (from `MULTIPLAYER.md`, not yet accepted)

- **Decision:**
  - Netcode for GameObjects 2.x on Unity Transport, client-hosted
    (one player hosts). Unity Multiplayer Services sessions with
    Relay and a join code for connecting; anonymous sign-in.
  - The grid chemistry stays lockstep, as in the sandbox (D6): the
    host orders cast intents into one batch per tick, every machine
    steps on batches, `CellHash` checksums, snapshot on join or
    desync. Cells are never streamed.
  - Players are owner-authoritative (NetworkTransform/Animator,
    interpolated). PhysX props are host-authoritative; any result
    that matters to chemistry is quantised into an intent (D4).
- **Why:** first-party and the most learnable path (D1); co-op needs
  no prediction; the deterministic core already exists and is tested;
  $0 at friends scale.
- **Reopen if:** competitive play, more than ~8 players, shipping on
  Steam (swap the transport for Steam sockets), or a desync rate that
  integer rules can't fix.
