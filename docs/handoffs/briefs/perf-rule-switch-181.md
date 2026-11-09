Needs: unity
Area: unity/Assets/Prototype/Chemistry/ (World.ApplyRules and what it rebuilds), unity/Assets/Prototype/Scripts/ (ChemGrid's ApplyQueuedRules, the mod switch), docs/perf/2026-11-09-profile.md (a new heading only)
Branch: perf-rule-switch-181

# 60 fps pass: the rule-switch hitch (#181, #155)

The goal is 16.7 ms a frame, mean and p95 (`docs/perf/2026-11-09-profile.md`, on dev). A rule switch costs one
frame of 126 to 150 ms on the nine with 4.7 MB of garbage (`ApplyQueuedRules` -> `World.ApplyRules`, inside
`ChemGrid.Step`), and #155 measured the same thing as a mod switched on: 123 to 136 ms, 57 to 59 MB. Both issues
are the same hitch; fix both.

## What to do

1. Name the inner work first: profile one apply (deep profile or markers) on the nine and on the valley, and write
   down what the 126 to 150 ms is made of (parsing, building tables, re-tagging cells, the view's rebuild, garbage).
2. Take the frame under 16.7 ms if it can be done, or as far down as it goes. The likely shape: prepare the new
   rules off the tick (before it, or on a worker) and swap them in on the agreed tick; rebuild only what the
   switch changed; no per-cell garbage.
3. Lockstep (D4) is the risk: every peer must switch on the same tick and hold the same state after it. What is
   prepared early must not be read by the sim before that tick. Keep the goldens (`CellHash`, FNV) unchanged, and
   run `online` with a rule switch on both peers (checksums agree after it).

The PC is on #179 and #180 now (`perf-fix-1`, `ChemGrid.Visuals.cs` and the blocker colliders) and takes #178
(`ChemGrid.RenderGas`) next: stay out of those, and say so if the fix needs them.

## Done

Before and after for the switch frame (ms and garbage) on the nine and the valley, in a development player build
if the Mac can make one, else the Editor, under a "Fix: rule switch" heading in the profile doc. EditMode passes,
`dotnet test core` passes, smoke passes on a fresh valley yard, `online` with a switch agrees. Self-land with
`land.sh auto perf-rule-switch-181 --compiled`; close nothing (the lead closes #181 and #155 from the report).
