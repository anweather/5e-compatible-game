Needs: unity
Area: unity/Assets/Prototype/Chemistry/Tests/HoldLockstepTests.cs
Branch: hold-lockstep-fast

# The slow Hold lockstep test fits Unity's timeout (Andrew, 2026-11-07; given to the Mac)

`HoldLockstepTests.TwoPeersWithDifferentPoints…` takes 3.5-4 min and fails Unity's 180 s EditMode timeout on every
run. It passes under `dotnet test core`. Because every EditMode run reports N-1 of N, a real failure can hide behind it.

- Measure where its time goes (ticks run, world size, checksum cadence) before changing it.
- Make it finish well under 180 s in EditMode, say 60 s, **without weakening what it proves**: two peers with
  different points stay checksum-equal over a Hold. Prefer a smaller world, fewer ticks to the same events, or
  splitting it into tests that each prove one part. Raising the timeout is the last resort, and only with a reason.
- No core or rules changes. If the test can't get faster without one, stop and report.

Before landing: EditMode fully green (N/N), `dotnet test core` green. Push the branch. The lead lands it with
`--compiled` from the Mac's report. Remove this claim file on the branch in the last commit.
