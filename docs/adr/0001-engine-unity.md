# D1. Engine: Unity (2026-10-13)

**Status:** Accepted

- **Decision:** Unity. Start on **6.3 LTS** (supported to Dec 2027) with
  **URP**.
- **Why:** learning Unity is a goal in itself, not a means. URP suits a
  stylised, whimsical look, runs on every target, and is where Unity 7
  is consolidating.
- **What this supersedes:** the earlier TS/Three.js/Rapier recommendation
  in `RESEARCH.md` §8. That was optimised for the web platform, not for
  learning Unity.
- **Upgrade path:** 6.6 if WebGPU on the web matters. Plan a move to
  6.7 LTS or Unity 7 in 2027 (CoreCLR, .NET 10).
- **Reopen if:** never, for the engine itself. Revisit the version
  yearly.
