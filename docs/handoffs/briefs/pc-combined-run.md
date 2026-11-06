Brief for the PC: one run of `pc-combined` (ea10e1b2), which is `named-acts` and `props-follow-land` on dev. Nothing here has been drawn. Rebuild the yard first: terrains have a new material. One Unity job at a time, and note the Editor's memory at the start and end of each part (#145); restart the Editor if it passes 12 GB.

**1. Props.** The 11 checks in `docs/handoffs/briefs/props-follow-land-pc-run.md`. Watch two things above all: frame cost at radius 32 with the per-area query, and the wizard's lift on rising ground (no jitter, no double lift; it stood fine without this).

**2. Named acts with a real Claude.** Ask in words for a named act, for example a campsite, then call it by name at two places. **Must show:** Claude finds `add_trigger(on: called)` and `call_act` unaided; one firing is one command; `undo_act` takes a firing back whole. Then one with a land row as canyon or cliff, which no test fires. Does a big stamp hit the 256-cell cap (`lay.maxCells`)? Report the words Claude used, any wrong turn, and the cost.

**3. Three peers.** A, B and late joiner C, same network, on the new shapes: canyon, cliff, a named act firing, undo on all three. **Must show:** no desync, equal hash, C gets the same land and props.

**4. Canyon then dragon.** Solo and peers. **Must show:** land back to the map's own on all, same hash. This was stopped by low memory last time.

**5. Edges.** Stamp beside the ball court, defense field, climbing tower, the bridge and one landmark. The places were copied by hand, the bridge's line worked out by hand. **Must show:** refused where the thing stands, allowed beside it, and the refusal doesn't reach far past what's drawn.

**6. PSO.** Refresh the trace for `TerrainRock` and say if warm-up covers it. Note whether the build content check gives a false failure again (#93).

**Report:** one message: gates, each part's result, frame numbers, desyncs, memory readings. Findings are for me: don't fix on this run, unless it's a one-line slip, and then say so. Push nothing to dev.
