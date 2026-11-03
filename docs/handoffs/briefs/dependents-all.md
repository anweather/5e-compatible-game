Brief from the PM: #88, a mod depends on anything another mod adds. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `dependents-all` off the latest `origin/dev` (`mod-stacking` #86 landed: read `World.WithDependents`, `TempMaterials.Named` and `ModStackingTests` first; `liquids` isn't landed, keep clear of its files where you can).

Read `gh issue view 88`. One rule: anything a mod adds (material, style, building row, species, part, trigger id, liquid) makes a mod naming it a dependent, going with its owner the same tick on every peer, through the same `WithDependents`. Then the never-on refusal should name the owner when it can (keep a record of who added what, if cheap and deterministic), and batch `ChemGrid.Reverted`'s redraw so a sweep of N mods redraws once. That last one is view (`Scripts/`): just list exactly what to change, don't edit it.

Tests on host and peer: a wing whose house adds only a style follows the house on revert and lapse; a species variant follows its base species' mod; existing goldens unchanged. Constraints: deterministic core, nothing under `Scripts/`, new `.cs` under `unity/` gets its `.meta`, new methods under S3776 15 by your `/tmp` measure. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, what now counts, gates, time.
