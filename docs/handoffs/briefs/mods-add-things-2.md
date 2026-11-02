Brief from the PM: finish `mods-add-things`, started by u1, which ran out of Pro at the weekly limit. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../mods-add-things origin/mods-add-things -b mods-add-things`, and merge `origin/dev` if it has moved. The whole job is `docs/handoffs/briefs/mods-add-things.md`: read it and follow its rules, deliverables and report, with u2 in place of u1.

Where u1 stopped (commit 29f0cb3, a WIP, all on the branch): field-level merging, things in rules, effective things, `chance`, the found log, validator checks, looks data and converted mods are written. u1 said converted mods give byte-identical rule documents and the same fingerprints. Its new tests had analyser errors it was fixing, so treat them and the gates as unproven: start by running `dotnet build core -c Release -warnaserror` and `dotnet test core`.

Then step 5, the test, which is what's left: u1's first headless client run in /tmp (not saved) failed with exit 1, "no stdin data received in 3s", which looks like the way it invoked `claude`, not the game. Read every step of the brief against the branch rather than trusting this summary.

Don't rewrite what works. Clean the WIP subject into proper commits only if it's quick.
