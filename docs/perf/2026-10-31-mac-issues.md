# Perf issues from the Mac pass, 2026-10-31

Issue texts for the PM to file, one per real problem left after step 3, label
`perf`. None is filed. Numbers are from `perf-mac` (a24f4e6, a macOS development
player on an M2 Pro at 1600x900) unless a line says otherwise; the full story is
in `2026-10-31-mac.md`. **Proven** means a marker, count or controlled run shows
it; **likely** means it comes from reading the code or from arithmetic.

Scenarios run with `-fps-probe -perf-scenario <name>`, in order of what to fix first.

**Discovery cost, the same line for each issue.** The template (commit-cost.mjs,
playtest/sessions-0.1.8-brief.md) wants the session's tokens, time and ite, the
issues found, and the share in ite. Only tokens are known: step 1 266,191, step 2
380,264, step 3 272,970,
so about 919,000 in all. The harness gives sub-agents a total with no split into
input, cache and output, so **no ite figure can be worked out and none is given**,
and no time was recorded for steps 1 and 2. To get ite, run `commit-cost.mjs
--record` once the transcripts are on disk.

`Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.`

---

## 1. perf: Water.Recompute walks every wet cell for any change

- **Scenario:** storm, 30 s (also the bot, seed 811).
- **Numbers:** storm: 14.5 ms mean a frame, 30.7 ms peak, on 402 of 600 frames,
  over 5900 to 9300 wet cells. Bot: 2.7 ms mean, 23.4 ms peak, on 2436 frames.
  It's now the biggest part of `ChemGrid.WaterMesh` in storm (23.1 ms mean).
- **Culprit, proven:** `CellWaterMesh.Recompute`, and in it `ComputeTops` (19.3 of
  23.1 ms in storm's worst frames): every body of water's level is worked out
  again when one cell's water changes, at most a few hundred cells.
- **Suggested fix:** recompute only the bodies of water that changed, keeping each
  body's cells; or a Recompute at most every few frames. About half a day: bodies
  merge and split as water moves. `DryAround` and `WetNeighbours` are iterators,
  so they're garbage too.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 2. perf: one water tile takes far longer than the frame's budget

- **Scenario:** bot, seed 811, with a player by the river; storm.
- **Numbers:** the budget is 3 ms and is only checked between tiles. A tile takes
  about 18 ms on the river (3600 verts) and 5.5 ms in storm (900 verts). Bot: 2364
  frames over 50 ms of 13 992, nearly all one or two tiles; `Water.DrawTile` 7.1 ms
  mean, 66 ms peak. Storm: a backlog of 180 tiles on average, peak 225, never clears.
- **Culprit, proven:** the budget's check; `Contour.Tile.Points` (6.0 of 7.1 ms in
  the bot) now outweighs the outline. Likely inside it: `FlowAndFoam`, `BlendedFlow`
  and `BlendAround` per vertex through closures, with `RunoffOpacity` asking again.
- **Suggested fix:** work out flow per cell rather than per vertex, without closures
  (half a day), then a smaller tile, or draw a tile over several frames. Far tiles
  could wait longer than near ones.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 3. perf: the film is rebuilt whole for a few cells of mud

- **Scenario:** storm.
- **Numbers:** 24 rebuilds in 30 s, 130 ms peak (500 ms before this pass), each for
  a few cells, one mesh over a box of 98 604 cells for 290 film cells. Storm's worst
  frames are all this: 191 ms, with 20 MB of garbage. Bot: 34 rebuilds, 39 ms peak.
- **Culprit, proven:** `CellSlickMesh.Rebuild` and `Contour.Film.Outline`, 98 ms
  peak, `Contour.Film.Points` 31 ms. Likely: the rest of Outline is `ReadCells` asking
  `Has` and `BesideStep` for every cell in the box, and the garbage is per-vertex
  closures.
- **Suggested fix:** draw the film in tiles as water does (an hour or two; the tile
  machinery exists: `ContourSurface` with `tiled: true`).

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 4. perf: storm and fog run the world under speed, and the debt has no cap

- **Scenario:** storm, 30 s; fog, 60 s.
- **Numbers:** storm: 106 ticks of 180 (59%), every frame out of budget, 13.2 s owed
  at the end. Fog: 220 ticks of 360 (61%), 23.3 s owed. A tick costs 39 ms in storm,
  78 ms in fog, 18 ms in the meadow (`ChemGrid.Step` time ÷ ticks).
- **Culprit, proven:** `ChemGrid.StepWithinBudget`: 6 ms a frame can't pay for that,
  and `tickTimer` grows without limit. Storm's cost is flow (15 ms a tick). Fog's
  is likely gas over 220 000 cells: no tick stage was timed on the merged code.
- **Likely consequence:** after the weather clears the world runs fast until the
  debt is paid, up to 4 ticks a frame. Not measured.
- **Suggested fix:** cap `tickTimer` at a second or so (minutes, but lockstep play
  must keep its pace, so check online: a peer's ticks are the host's). Then flow.
  Storm reached 59% from 44% with no change to it, so faster frames help but don't
  get there.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 5. perf: fog weather lays a unit on most open cells (a question for Andrew)

- **Scenario:** fog, 60 s.
- **Numbers:** on dev's change the frame is fine: 23.9 ms mean, nothing over 50 ms,
  493 MB allocated, against about 2100 ms and single frames of 52 to 68 s in step 1.
  That's 303a42d (the veil draws nothing), not this pass. What's left: **220 668 fog
  cells** at peak, of about 266 000 in 65 active chunks; a frame 8 ms over the
  meadow's, GPU 15.9 ms against 11.6; issue 4's slow tick.
- **Culprit:** `Veil` lays a unit on open cells at 0.05 a tick and fog decays at 0.015
  a tick, so about 77% of them hold fog. That's arithmetic, and the count now fits it.
- **The question:** the glossary says fog weather lays a unit "here and there", but
  the rules cover most of the land. Which is right: **change the rule** (lay much
  less), or **keep the rule and pay for it**? Now it's undrawn, the veil costs ticks
  and does nothing to look at, though it still blocks sight (`World.LineOfSight`).
- **Still true, not re-measured:** gas above one unit still goes to `FogBank`: 3
  GameObjects a cell, 0.33 ms each to make, 1 us a frame to turn, about 3 KB each.
  A large Fog Cloud pays that.
- **Suggested fix:** the rule, minutes if Andrew picks it (rules change both engines'
  runs, so goldens may need looking at). Fog drawn as `FlameBank` draws fire, half a
  day or more, if big fogs need to be cheap.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 6. perf: snow still hitches about every 4 s

- **Scenario:** snow, 30 s.
- **Numbers:** worst 207 ms (942 ms before this pass), 62 frames over 50 ms. In the
  worst: `ValleyGround.HeightUpdate` 85 ms, `ChemGrid.Step` 47 ms, `HeightWrite` 22 ms,
  `WaterMesh` 19 ms.
- **Culprit, proven:** the height work: 62 575 samples a snowfall (633 365 before),
  1.3 us each. Likely: `BankAt` looks at about 36 cells a sample. `Snow` also lands
  all its cells on one tick.
- **Suggested fix:** spread a snowfall's reshaping over frames (it's eased anyway),
  or a cheaper `BankAt` that looks only at the changed cells. Two hours.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 7. perf: #5 on the PC: PsoDirect3D12 has no night states (comment on #5, not a new issue)

- **Scenario:** `forest` at night on an empty pipeline cache.
- **Numbers, Mac:** with the shipped states, worst frames 510 and 226 ms and a 3887 ms
  stall; merged with a night trace, worst 44 ms, no stall. **Fixed on Metal.**
- **Culprit:** proven on Metal: states first made in mid-walk (`SpeedTree8_PBRLit` and
  Lit with `_MAIN_LIGHT_SHADOWS LOD_FADE_CROSSFADE`). On the PC, likely: same cause,
  but D3D12 wasn't run and `PsoDirect3D12` wasn't read; the Mac can't.
- **Suggested fix, on the PC, 30 minutes:** `-fps-probe -perf-scenario forest
  -perf-seconds 120 -pso-trace <file>`, then merge it with a copy of the shipped file
  named first (RELEASING.md). Then close #5 if the 185 ms is gone. An empty cache now
  warms 325 states in 15.7 s (11.6 s for 253): first launch only (likely), but no
  splash covers that.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 8. perf: the lit smoke is most of a close fire's GPU time

- **Scenario:** `flamewall`, step 2, on db686d6: 426 cells at level 3, 10 m from a low camera.
- **Numbers:** GPU 81 ms with the fire, 24 ms without. No smoke, 25 ms; no fire
  lights, 51 ms; no flames, 77 ms.
- **Culprit, proven by taking each away:** smoke (Particles/Lit, soft particles) under
  up to 16 fire lights. They multiply. Likely fill rate. Flames themselves cost at most 4 ms.
- **Suggested fix:** unlit or vertex-lit smoke, fewer or smaller particles close up.
  Changes the look, so it's Andrew's call. Half a day.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 9. perf: GPU time climbs late in a storm

- **Scenario:** storm, compared by length.
- **Numbers:** GPU mean 12.0 ms and p95 14 ms over 15 s (57 ticks); 12.1 and 17 over
  20 s (74 ticks); 18.3 and 37 over 30 s (106 ticks). Step 1 never saw it: its 30 s
  only reached 80 ticks.
- **Culprit, not found.** Proven only that it follows the storm's progress, not the
  fixes: the same build at 15 s reads 12 ms. Film cells go from 120 to 290 over that
  time, and water verts rise. A film or water surface filling the view is likely.
- **Suggested fix:** run a storm of 60 s with the camera's draw stats, then decide. An hour to find.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 10. perf: the forest is steadily twice the meadow's GPU time

- **Scenario:** forest, 120 s, at 21:30 or noon.
- **Numbers:** p50 24 ms, GPU 25 ms mean (meadow 14 and 11.6). No spikes: it's a
  steady 41 fps on this Mac, and the same by day and night (step 2).
- **Culprit, not attributed.** Likely the SpeedTree shadows and crossfades; nothing
  measured what part of the GPU time they are.
- **Suggested fix:** take shadows, LOD fade and tree distance away one at a time as
  step 2 did for fire, an hour or two. Then decide.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.

## 11. Water left with a stale runoff edge (a look, found by the perf check; `perf` or `bug`)

- **Scenario:** `-perf-water-check` on snow, wildfire or the bot: redraws each settled
  tile and compares.
- **Numbers:** 3 or 4 of 77 tiles differ (10 of 82 once in the bot), same verts, alpha
  off by 0.7. In snow and the wildfire the same tiles differ with and without fix 6,
  so it isn't new.
- **Culprit, likely:** `RunoffOpacity` reads `flowOf`, which `GroundChanged` clears
  everywhere, while only the tiles round the change are redrawn. The first draw's flow
  stays on the rest until something else redraws them. So a runoff's colour depends
  on when its tile last drew.
- **Suggested fix:** mark the tiles whose flow changed, or clear `flowOf` only near the
  change. An hour.

Discovery cost: session about 919,000 tokens (steps 1-3; ite and time not recorded), 11 issues found, share about 84,000 tokens.
