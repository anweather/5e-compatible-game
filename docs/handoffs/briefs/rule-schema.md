Brief from the PM: cleanup item 7, one `RuleSchema` per table, first table only. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `rule-schema` off the latest `origin/dev` (not on `react-split` or `split-2`, which wait on a Unity compile).

Item 7 of `docs/design/cleanup-review-2026-11-02.md`: field and verb lists are copied in `RuleFields.cs`, `ModValidator`, `Rules.Check*` and `Workbench.*`. Build a small `RuleSchema` (each field's name, type, range, whether a mod may set it) that the validator, the checks and the MCP all read, and move **one** table onto it: the smallest that still shows the pattern; say which and why. Leave the rest as they are; list what each remaining table will need. Messages a player or Claude sees stay word for word unless they were wrong (say which).

Constraints: deterministic core; goldens and the fingerprint unchanged; nothing under `Scripts/`; a new `.cs` under `unity/` gets its `.meta` (new GUID); new methods under S3776 15 (your `/tmp` measure). Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, the table, lines of copied lists removed, gates, time.
