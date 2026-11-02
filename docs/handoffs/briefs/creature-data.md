Brief from the PM: creatures as data (J-058, J-062). Lane: Gameplay, in the Editor, after `q-e`. Design: `docs/design/builders.md`, sections 1 and 2.

1. **`creature-data`, one branch.** Species into `rules/fauna.json`, limb shapes into `rules/parts.json`, outside the fingerprint, with the spawners' table there too. An Editor menu exports Punchy's assets once with the same numbers. An Editor test proves each body built from its row matches the one built from its asset; then the assets stop being read. No art changes: a before and after sheet of a dozen species must match. A live change to a row rebuilds only that species. Report its cost.
2. Then the port that was waiting, as rows: `punchy-people` (children, the humanoid peoples, hair, beards), `life-variety`, `punchy-water`, per `docs/handoffs/briefs/punchy-life.md`, plus Ptarmigan and Roadrunner into the spawners. MCP tools to patch a row are u1's, later: don't build them.

Report each branch with commit, gates, media and your counter. Write your handoff first if you're past 400k.
