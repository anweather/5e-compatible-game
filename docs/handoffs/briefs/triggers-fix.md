Brief from the PM: fix `triggers-core` before it lands (#76). Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Work on your branch `triggers-core` (b378165e): fetch, check it out, and merge `origin/dev` into it first (it's 13+ commits behind; `Workbench` now takes a `GameFolders`).

The Mac compiled it in Unity and checked it in the Editor: with `who: "each"` everything works (cooldown, marks, delay, revert). Fix these:
1. **Solo, a trigger with no `who` never fires.** `add_trigger` fills in `Here.Asking().Player` ("Player 1", `Workbench.Triggers.cs:24`), but solo stands come in as player "solo" (`SpellCaster.Combos.cs:29` `SoloPlayer`, `WizardStrike.cs:80`), and `Stepped` compares exactly (`World.Triggers.cs:90`). Make the default match the asker's steps solo and online, deterministically; a test for each.
2. Commit the missing `unity/Assets/Prototype/Chemistry/Mcp/Workbench.Triggers.cs.meta` (a new GUID, in the same format as its neighbours').
3. Bring the 4 new S3776 under 15: `Workbench.Triggers.cs:20` AddTrigger, `Rules.Triggers.cs:130` Triggers(), `World.Triggers.cs:83` Stepped, `ModValidator.cs:455` TriggerBudget. `ModPatch.cs:62` ShapeErrors rose from 17 to 19: back to 17.
4. `simulate` said a meteor-at-every-step mod ticks 4.2 times slower, past the budget of 2, yet `apply` took it. Find out whether the mod-cost budget is checked at apply for triggers, and make it so if not, the same as other mods.

Constraints as before: deterministic core, existing goldens unchanged, nothing under `Scripts/` except what fix 1 strictly needs (say so if it does). Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, each fix, gates, time.
