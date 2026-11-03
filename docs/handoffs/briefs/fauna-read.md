Brief from the PM: `fauna-read`, the first step of made-up bodies (#73), plus #80 parts 3-5. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `fauna-read` off the latest `origin/dev`. Don't base it on `triggers-core` (not landed yet).

Read `docs/design/bodies-and-surfaces.md` §1-§4 first. `fauna-rows` already gave `list fauna` and `add_species`. Build:
1. **Read tools in the rules MCP:** `list parts` (id, name, role, slots) and `describe parts <id>`; `describe fauna <id>` printing the row with its slots flattened (each slot's role, what it hangs `on`, where it sits in body units) and a one-line text shape, e.g. "4 legs in 2 pairs, 2 arms, 1 head; 2.1 m tall, 1.6 m long". Fix `outside_rules` for fauna and parts as §4 says. No `from`/graft yet: that's the next branch.
2. **#80 part 3:** `whats_on` must report a new fauna row a mod adds (today it says "nothing that differs from what was under it").
3. **#80 part 4:** `add_species` copies the base's whole `behave` (`wander`, `idle`, `flee`, `group`) though the tool refuses those from the caller. Either don't copy what the variant can't change, or show it as inherited; say which and why.
4. **#80 part 5:** `ModValidator.FaunaBudget` (`ModValidator.cs:488`, complexity 18): under 15.

Tests for each (the `Workbench` in tests needs `new GameFolders(...)`). Update `docs/RULES_MCP.md` for the new tools. Constraints: deterministic core, existing goldens unchanged, nothing under `Scripts/`; if a new `.cs` file goes under `unity/`, commit its `.meta` (new GUID, same format as its neighbours). Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, each item, gates, time.
