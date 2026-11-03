Brief from the PM: #85, three quiet gaps in mods' building tables. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `building-gaps` off the latest `origin/dev`. DesktopUnity's `room-tables` (#79) touches `Rules.Buildings.cs` and `World.Room.cs` and isn't landed yet: merge `origin/room-tables` in first and keep its changes.

Read issue #85 (`gh issue view 85`). Fix all three, each with a test on host and peer worlds:
1. A table-only change that alters base building cells (a drawn-door that `becomes` "open") must lay those cells again, so cells and the raster agree; revert lays them back.
2. Opening `cells` near 2e9 must not overflow `SpanOf` into a negative span: refuse it at the checks.
3. Unknown tables in a mod's `buildings` (`"windows"`) and `"ground"` are refused with a named message instead of silently ignored.

Constraints: deterministic core, goldens unchanged (say if 1 needs a new one), nothing under `Scripts/`, new methods under S3776 15 by your `/tmp` measure. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, each fix, gates, time.
