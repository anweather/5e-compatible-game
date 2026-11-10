Needs: unity
Area: unity/Assets/Prototype/Scripts/ (PerfStage, PerfScenario, probes), tools/probe.sh, docs/perf/ (a new file)
Branch: scale-probe
After: scale-12x10 (u2's map) lands or is pushed

# What doesn't scale: the nine against the 12 x 10 scale map

Andrew, 2026-11-09: run a 12 x 10 world to find what doesn't scale with a large world. The rule (J-086): cost
follows the active region (the chunks within reach of characters) and the drawn tier, never the map's size. The
scale map (`world/maps/scale-12x10/`, u2's `scale-12x10`) is the nine in the middle of 9,720 chunks of rolling grass
and sea, with landmarks, baked whole.

## What to do

1. Bake the full map (time it; disk size of what it writes; peak memory while baking).
2. Load it in a development player build with the Editor idle: load time, memory after load (private, working set,
   managed, native, Unity objects and meshes by type), and the same for the nine.
3. Run the same scenarios on both maps (same spawn, seed and length): standing, the bot's walk, a Fireball, a
   sim-only rule switch, a look-changing switch, and a long walk out of the nine onto the grass. Mean, p95, worst
   frame, frames over 30 ms.
4. **The list, the main output:** every cost that grows from the nine to the scale map (a frame cost, a hitch, a
   load step, memory), with its code path, how it scales (with chunks, cells, things, landmarks) and a size guess
   for the fix. Grep for whole-map loops (`World.Cells`, every chunk, every Terrain, every thing) to explain each.
5. If the full map doesn't fit in memory, that is the first finding: say how far it got and the numbers.

## Done

`docs/perf/2026-11-10-scale.md` with the tables and the list; file one issue per real cost (`Discovery-Cost:` line);
EditMode passes; smoke passes on the valley and the nine. Any probe or flag added goes on the `scale-probe` branch,
pushed; the lead lands it.
