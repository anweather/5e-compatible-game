Brief from the PM: cleanup item 8, split the worst reaction method. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `react-split` off the latest `origin/dev`.

Item 8 of `docs/design/cleanup-review-2026-11-02.md`: split `World.React.cs:324` (S3776 complexity 80) into named steps, each under 15, with no change in behaviour. If time allows, also `World.React.cs:172`; leave `World.Terraform.cs:151` and `World.Buildings.cs:231` for later branches. This is the hot path of the simulation: keep allocation and iteration order exactly as now (no LINQ, no new collections per tick), and say if any split costs measurable time (a quick before/after of the core's existing perf or tick tests is enough).

Constraints: deterministic core; every `CellHash`/FNV-1a golden and the fingerprint unchanged; nothing under `Scripts/`; a new `.cs` under `unity/` gets its `.meta` (new GUID). Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, complexity before and after per method (your count and the build's), gates, time.
