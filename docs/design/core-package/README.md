# Chemistry core as a package (proposed)

Andrew's handoff (2026-11-12): `plan.md` (the full plan, Punchy integration included) and `kickoff.md` (Ticket 1).
Lead's amendments, agreed with Andrew:

- **Phase 1 only, to explore.** Ticket 1 (the audit) first; nothing moves until it's reviewed.
- **The package lives in this repo for now** (e.g. `packages/com.anweather.chemistry-core/`), used by our game by
  local path so core and view still change in one commit; another project pins it by git URL with `?path=` and a tag.
  A separate `chemistry-core` repo (subtree split) only if that proves necessary.
- Expect the audit to split "Chemistry/" into a cell-simulation core plus rules, world generation (WorldPlan,
  ChunkGen, LandChunks, SeaMask, CellHeightfield, Waystones, Fauna, Buildings, Places), and our-game features
  (PlayerSave, GameFolders paths, ...).
- Punchy's licence and Andrew's friend's permission come before any fork.

The audit report is `audit.md` (Ticket 1, 2026-11-12): lift and shift first, at a quiet point; carving a true sim core later, on a real need.
