# D5. No grid tactics; turn-based only in physical form (2026-10-13, amended same day)

**Status:** Accepted

- **Decision:**
  - P1–P4 are real-time: action, platforming, exploration.
  - Turn-based is in **only** as P5, physical tactics
    (`PHYSICAL_TACTICS.md`): continuous 3D space, intents resolved
    physically, reactions, flat HP. That includes P6, which uses P5 as
    its combat mode (`EXPLORE_AND_FIGHT.md`).
  - Grid or FFT-style tactics is out.
- **Amended because** the physical, reaction-driven, low-HP version is
  a different game from Pocket Tactics. The original "real-time only"
  reason holds only for the grid version.
- **Why:** Pocket Tactics already covers turn-based tactics on the
  platform, and that genre is crowded. The interesting, less-trodden
  space is real-time 5E spell chemistry.
- **Consequences:**
  - Single-player PhysX is authoritative everywhere (D4's
    "nobody has to agree" case). P5 adds preview-then-replay:
    simulate in a sandbox `PhysicsScene`, then replay the recording on
    commit. That would also make P5 async-multiplayer-able later.
  - Platform integration drops to option 0 (standalone) or option 1
    for accounts and saves only (`PLATFORM_FIT.md`).
  - D3's core library is still worth it, for a different reason: one
    chemistry rule set shared by the 2D (`Rigidbody2D`) and 3D (PhysX)
    prototypes, which are separate physics systems in Unity.
- **Reopen if:** a prototype shows that the chemistry shines most when
  you can plan it out.
