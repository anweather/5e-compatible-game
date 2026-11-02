Brief from the PM: the golden squirrel test (J-055) and field-level patches. Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../mods-add-things origin/dev -b mods-add-things`. Read `docs/design/world-tools.md` section 2 (G4 to G6) and `docs/RULES_MCP.md`.

1. **Field-level patches.** Today a catalogue mod restates a whole entry, so stacking `big-fireball` with `blue-fireballs` loses the radius, and reverting can't undo values a later mod copied. Make a mod patch only the fields it names, for drafts, `apply` and `save`, with mods in `rules/mods/` converted or read compatibly. Stacked mods combine, and revert undoes only its own. Test those cases. Goldens stay.
2. **Things drawn from data (G4).** A `look` on entities and items rows with a `creature` family (species, coat colour, scale) beside today's shapes. Define what the view needs: a kind with no baked prefab is dressed from its look. `set_look` takes `entities` and `items`; `add_item` and `add_entity` drafts.
3. **Mods add things (G5).** The `world` section's `add` appends a thing to its chunk's list, never inserting, so ids stay. Effective things feed the fingerprint and spawner. A revert removes what the mod added. The first pick-up of a thing a mod added logs a line naming the finder and the row's name.
4. **`chance` (G6).** Decided once from the world's seed and the thing's id by `WorldHash`, with no draw from the world's RNG, so peers agree.
5. **The test.** "Put a rare golden squirrel somewhere in the woods for us to find", from a headless client, ends as one mod: an item with a creature look, added at a spot Claude picked with `find_site` or `places`. It survives save and reload, is identical on two runs, and is the same on two peers. No squirrel in code or base rules (J-055): the row lives in the test fixture.

The Unity side is the PC's: state exactly what it must read, and keep Chemistry compile-safe for Unity, avoiding signature changes where you can. Gates in CLAUDE.md. `git diff origin/dev...HEAD --stat` before pushing. Deliver: commit and push, not merged. Final message, 12 lines or fewer: the commit, what works, what the view must do, what's left out, gates, time.
