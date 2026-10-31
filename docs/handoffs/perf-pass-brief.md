# Brief: performance pass (host, on the PC with the GPU)

Andrew, 2026-10-30: run a profiling pass now, and afterwards write a project
skill (`perf-pass`) from how it actually went. So **keep notes as you go**:
the exact commands, flags, scenario names, where results land, what worked and
what didn't. The skill is written from those notes.

Start once UX pass 4 is done and merged, with no worker VMs running (the PM
makes sure). Pull `dev`. Read COORDINATION.md (lean), docs/FAST_LOOP.md (the
probes, and the `-fps-probe` player flag in `FpsProbe.cs`) and docs/BACKLOG.md
item 19 (the last pass's numbers, 2026-10-26). Run it in short fresh
sub-agents per step, not one long session.

## 1. Measure, in a player build (not the Editor)
Use the release-style player from UX pass 4's part 3, or build one from dev.
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
