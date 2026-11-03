Brief from the PM: primitives step 2, temporary materials. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `temp-materials` off `origin/products-data` (9c3f65ea, yours, waiting on a Unity compile) merged with the latest `origin/dev`.

Build step 2 of `docs/design/primitives.md` §5, per §4: mod-made materials (covers first) that live while their mod is on and may `lapse` {after: ticks}; the sweep, the retired list so a lapse never leaves a dangling reference, ids by string on the wire, the limits in `budgets.json` (8 new materials a mod, 32 temporary rows live). J-062: a temporary material lasts while its mod is on, `after` is optional, and a revert always clears it. Every peer must agree tick for tick (lockstep, D4): sweeps in a fixed order, no wall-clock.

Then run **the first test** headless, as a test or a `simulate` call: §5's black ice. One mod with a cover `black-ice` (`slippery`, `lasts` 360, `melt`, a dark blue look, `lapse` {after: 3600}), one element, one reaction row and one spell; cast it, see slicks, step past the lapse, and every slick is gone and the rules are back to base, with the same checksum on two simulated peers. Also: the mod reverted early sweeps it at once.

Constraints: deterministic core, existing goldens unchanged (add new ones for the lapse), nothing under `Scripts/`; new `.cs` under `unity/` gets its `.meta` (new GUID). Run your `/tmp` S3776 measure on new methods this time. List what the view will need to draw a temporary cover. Update `docs/RULES_MCP.md`. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, what's built, the black-ice result, gates, time.
