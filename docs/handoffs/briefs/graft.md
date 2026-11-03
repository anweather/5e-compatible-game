Brief from the PM: `graft`, made-up bodies from other species' parts (#73). Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `graft` off your `origin/fauna-read` (198f4f97, not landed yet: it waits on a Unity compile) and merge the latest `origin/dev` into it (`triggers-core` just landed).

Build `docs/design/bodies-and-surfaces.md` §2-§3, the `graft` row of §4:
1. **`from` on a species row**, flattened by the core's `Fauna.cs` when it's read, so the view, checks and `describe` see plain slots: `take`/`drop` per donor (a slot taken brings every slot hung `on` it), `at`, `dir`, `scale`, clashing ids prefixed by the donor's id, `coat: "own"` kept on each flattened slot as data (the view draws it in the next branch). `body`, `move`, `behave` may say `from` a species and override fields.
2. **Checks:** every donor and part exists, `on` resolves, no cycles. New `fauna` budgets in `budgets.json`: 4 species a mod, 3 donors a graft, 40 slots after flattening, `scale` 0.25 to 4.
3. **MCP:** `add_species` and `set_species` take `from`; `validate` flattens and checks; `describe fauna` prints the flattened row (yours from `fauna-read`). Update `outside_rules` and `docs/RULES_MCP.md`.
4. **`GraftTests`:** pin the centaur's flattened row from §2, the faun, and the six-legged bear (`fore` taken twice); refusals over each budget.

Andrew's call (2026-11-03): bodies ship before gaits by rule, so don't touch gaits; first bodies may move a little wrong. Constraints: deterministic core, existing goldens unchanged, nothing under `Scripts/`; a new `.cs` under `unity/` gets its `.meta` (new GUID). Keep new methods under S3776 15 by your count. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, each item, gates, time.
