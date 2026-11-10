# Follow-up: finish sparse-world-guard and push it

Lead, 2026-11-10 16:45 MST. Your session went idle at 13:42 waiting on a ScheduleWakeup that never fired (the runner
doesn't wake headless sessions), with the guard work uncommitted in /home/anweather/sparse-world-3 and the full core
suite logged to /tmp/guard-test.log.

1. Read /tmp/guard-test.log. If it passed, commit the guard work; if it didn't finish, run `dotnet test core` again in
   the foreground (no background wait, no ScheduleWakeup).
2. The work belongs on a branch `sparse-world-guard` off origin/dev (sparse-world-3 already landed at ba53272c; dev is
   at 100f4e37 or later): move your commits there (cherry-pick or rebase onto origin/dev), run the gates, push.
3. Part 2 of the brief, if not done: the Scale Island's World build in a player went 2.06 -> 3.62 GB between 7 and
   8 s and ~4 GB at 82 s (still in the World build, before GROUND HEIGHTFIELD), while your dotnet number for the World
   in play is 63 MB. Name what the build path allocates (MakeFrom, KeepStart, BakedSource reading cells.txt, the
   start state, the bind) with a dotnet run that builds the island's World from its bake the way ChemGrid.MapWorld
   does, and fix it or say what it is.

Report when pushed. Don't wait on timers; run long things in the foreground.
