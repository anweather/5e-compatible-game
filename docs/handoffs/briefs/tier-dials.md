Needs: unity
Area: unity/Assets/Prototype/Scripts/ (FarRing*, MapTerrains, the perf probe scenarios), tools/probe.sh, docs/FAST_LOOP.md's probe list only
Branch: tier-dials

# Three distance dials and a sweep probe (the 12 x 10 world's first proof, the PC half)

Read `docs/design/world-12x8-scope.md`, "The three tiers today". Andrew wants each tier to be a dial for
performance testing (J-084):

1. **Active:** the chunks whose cells simulate: `rules/sim.json` `streaming` `reach` / `keep` (2 / 3 chunks),
   `ChunkPlanner`. This is lockstep state: change it only through the rules as the host's, never per peer. For the
   probe, set it before the world starts (or by a rule switch) so every peer agrees.
2. **Drawn:** the chunks that get their full Terrain. Today it's "every baked chunk within `know`"; `sight` 4 exists
   and draws nothing. Make it a real per-peer view setting (chunks, a radius like the others): Terrains past it are
   hidden or fall back to known meshes.
3. **Known:** `know` 7 chunks (`KnownRing`, `FarRing`), plus far landmarks' `seenFrom` (`world/horizon.json`). Add a
   per-peer view override that changes it at run time, and redoes the haze (1.98 / (know x 96 m)).

Don't change Chemistry/ (u2's `world-12x10-plan` is in there; the known ring's own size can be passed in from the
view). Keep the defaults exactly as today, so smoke, farringclip and the goldens are unchanged.

## The sweep probe

A probe (`tools/probe.sh tiersweep`, or arguments on `perf`/`farringclip`) on the nine that walks or flies a fixed
path and, for a grid of settings (say active 1/2/3, drawn 2/4/7, known 4/7/12; trim it if a run gets long), reports
per setting: frame mean and p95, `mem_allocated_mb` / `mem_reserved_mb` (`PerfScenario.cs`), chunks per tier, and
the known ring's worker ms. Output pass/fail JSON like the others plus a small table. Build the yard on the valley;
point `Temp/map` at the nine only for the probe run, and put it back.

## Done

EditMode passes, smoke passes on a fresh yard, the sweep runs on the nine and its table goes in the report (and in
`docs/design/world-12x8-scope.md` under a "Measured" heading). Push the branch; don't merge to dev.
