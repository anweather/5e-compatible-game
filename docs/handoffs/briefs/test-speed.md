Brief: make the core tests and the landing gates faster (#162). Branch `test-speed` off `dev`. Core, tests and `tools/` only: change no game behaviour, and no golden moves.

**Why:** the core suite went from 13 s on Oct 26 to 5 to 6 minutes now, and it runs one test at a time. On the PC at dev 2a5932a8, test times add up to 290 s and the run took 293 s, on 8 cores. The slowest 50 of 1,573 tests are half the time.

**1. Run tests in parallel.** `core/Game.Core.Tests` uses NUnit 3.14 with no `Parallelizable`. The tests are linked from `unity/Assets/Prototype/Chemistry/Tests`, and Unity's test runner reads the same files, so the setting must not break EditMode: prefer an assembly attribute in a file only the `core/` project compiles. Start at fixture level, then try children if it's safe. Find shared static state (rules, caches, statics in `World` and `Rules`) and fix what collides. A test that truly can't share goes `NonParallelizable`, with a one-line reason. Tests with a tick budget (`AFullSweepStaysWithinTheTickBudget`, #87) may flake under load: keep them apart from the rest.

Run the suite at least 5 times in a row, with a different order if you can. A flaky parallel suite is worse than a slow one: if you can't get it steady, stop and say so.

**2. Trim `tools/workers/land.sh`.** Skip the sandbox gates unless `sandbox/` or something it reads changed (it's frozen, CLAUDE.md). Run format and duplicates alongside the build. A gate's exit code must still stop the landing: see the memory note, a hidden failure reached dev once. Check CI's workflows stay consistent with whatever you change.

**Report:** one short message: the commit, gates with exit codes, the run time before and after on the same machine, how many tests ended up `NonParallelizable` and why, and the 5 runs' results. Say what the PC should expect with its 8 cores. Don't merge it.

Steps after this (tiers, slimming heavy tests) wait for the numbers. Splitting features off the core waits until the Hold is done (Andrew, 2026-11-05).
