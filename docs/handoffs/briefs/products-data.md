Brief from the PM: primitives step 1, products by data (P3). Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `products-data` off the latest `origin/dev` (`rule-schema` waits on a Unity compile; don't base on it, and keep clear of `RuleSchema.cs`/`Rules.CheckSchema.cs`).

Read `docs/design/primitives.md` §1, §3 and §5, and J-062 in `JUDGMENT_CALLS.md` (Andrew said go to every recommendation: products and temporary materials before liquids). Build step 1 of §5: **P3, products by data** in the core, so what a material leaves behind (ash, char, steam, rubble and so on) comes from `rules/` rows instead of code naming a material, plus the slick covers' `needs` in `looks.json` as data if that's core-side. Every existing golden must hold unchanged: this is a move from code to data, not a change in behaviour. Rules change through the rules MCP's tools or by hand in `rules/*.json` with the checks updated; mods must be able to set the new field, within the checks and budgets.

Tests: the goldens, plus a test that a mod's material leaves a product of its own choosing with no code naming it. If any piece needs the view (Unity), stop at the core and list what the view needs. Constraints: deterministic core, nothing under `Scripts/`, a new `.cs` under `unity/` gets its `.meta` (new GUID), new methods under S3776 15 by your `/tmp` measure. Update `docs/RULES_MCP.md` for what a mod can now say. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, what moved to data, how many code names of materials went, goldens, gates, time.
