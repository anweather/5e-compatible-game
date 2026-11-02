# D14. Creatures are data (2026-11-02)

**Status:** Proposed

- **Context:** species and limbs were Punchy's Editor assets, with a spawn table baked into the scene, so a
  mod reached none of it (J-055, J-058). `docs/design/builders.md` sets out rows instead.
- **Decision:** species are rows in `rules/fauna.json` and shapes in `rules/parts.json`, with the spawn table
  as each row's `spawn`. The core loads and checks them and hands them on (`FaunaTables`); it never acts on them,
  and they stay out of `Rules.Fingerprint`. The view builds one `CreatureBody` per species at run time and,
  at a rule switch, only for the rows that changed (`CreatureBodies`), so animals keep their places and seeds.
- **Why:** a mod or the MCP can then add or change a creature with no code, asset or scene rebuild, and
  fauna is view only already (each peer rolls its own), so nothing about it can reach the checksum.
  `CreatureRowTests` proved every number equal before the assets stopped being read.
- **Reopen if:** a creature's numbers must be shared between peers (they would then need the fingerprint), or
  composing rules with the tables in them gets slow enough to hitch a switch (14 ms in the Editor, 6 of it
  without them, at this size): the tables could then sit outside the document mods clone.
