# Brief: performance pass (overnight on the MacBook; a short Windows comparison on the PC after)

Andrew, 2026-10-30: run a profiling pass now, and afterwards write a project
skill (`perf-pass`) from how it actually went. So **keep notes as you go**:
the exact commands, flags, scenario names, where results land, what worked and
what didn't. The skill is written from those notes.

**Where (Andrew, 2026-10-30):** the MacBook, overnight, after Andrew's five calls. It's the weaker
hardware, so it shows the spikes first and gives a minimum-spec baseline. Build a macOS player
locally for profiling only (no Mac release). Check that the new fire's StructuredBuffer works on
Metal (UX pass 4 part 2 wasn't seen on a Mac). Afterwards, the PC repeats step 1 on its
Windows player for comparison: players get Windows. Pull `dev`. Read COORDINATION.md (lean), docs/FAST_LOOP.md (the
probes, and the `-fps-probe` player flag in `FpsProbe.cs`) and docs/BACKLOG.md
item 19 (the last pass's numbers, 2026-10-26). Run it in short fresh
sub-agents per step, not one long session.

## 1. Measure, in a player build (not the Editor)
Build a macOS player from dev (UX pass 4 merged), with development build and profiler connection on.
With `-fps-probe`, measure each scenario for a fixed time, and record the
mean, p50, p95 and p99 frame times, the worst 5 frames, and memory:
- the meadow (the `perf` camera);
- a wildfire (about 650 burning cells, like `wildfire`);
- a storm with rain;
- snow, then fog weather (new today; `w1` flagged fog gas on every active cell);
- the village;
- the bot's route, seed 811 (where the 185 ms walking hitch, #5, was found).

Note Play-mode entry time in the Editor too (last time about 60 s).

## 2. Attribute the spikes
For the worst frames and the steady cost, capture with the Unity Profiler
(the player with the profiler connected, or ProfilerRecorder markers already
in ChemGrid, RobeCloth and the bot). Name what each spike is: the chemistry
tick, grid drawing, effects, terrain writes, GC, cloth. Check:
- #5, the walking hitch (forest at night);
- the 858 ms frame from the 0.1.8 RC bot run;
- fog weather's cost;
- the new targeting marker mesh, the fire variety (UX pass 4) and snow.

## 3. Compare and file
- A table against 2026-10-26's baseline (Editor numbers then, so say which
  are comparable).
- Each real regression or spike becomes a GitHub issue: send the text to the
  PM, who files it. Use the label `perf`, with the numbers, the scenario,
  the profiler's culprit, and a Discovery-Cost line (agent-cost-telemetry's
  template: the session's cost ÷ the issues found).
- Fix only what's small and clear (under about 30 minutes each) and say so;
  the rest is queued.

## Deliver
- `docs/perf/2026-10-30.md`: the table, the culprits and the issues.
- The notes for the skill: the exact steps as they really ran, the pitfalls,
  and what to measure every time.
- Report to the PM in 10 lines or fewer, then close the Editor.
