Brief from the PM: cleanup item 8, the next splits. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `split-2` off the latest `origin/dev` (`react-split` waits on a Unity compile; don't base on it and don't touch `World.React.cs`).

The same way as `react-split` (old order, no new allocation, the build's count from a `/tmp` copy):
1. `World.Terraform.cs:151` under 15.
2. `World.Buildings.cs:231` under 15.
3. `Rules.CheckFauna.cs:69` `Donor`: Unity counts S3776 16; under 15.

Constraints: deterministic core; every golden, bench hash, RNG state and the fingerprint unchanged; nothing under `Scripts/`. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, complexity before and after per method, gates, time.
