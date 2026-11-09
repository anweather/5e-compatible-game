Needs: core
Area: unity/Assets/Prototype/Chemistry/ChunkGen*.cs, WorldPlan*.cs (rivers and roads only) and their tests; no valley
map files change
Branch: chunk-gen-polish

# ChunkGen: what the first heights picture shows (u2)

The lead looked at `nine-valley-1-heights.png` from `chunk-gen` (94406795). Fix, in order, with a before/after picture
of the same seed and two more seeds:

1. **Striped seams at the valley's edge:** along its north side (x 0-576, y ~1110-1150 in the nine's cells) and along
   the south coast east of it (y ~1590-1680), the land shows vertical comb stripes, as if each column blends on its own.
   The meet over 90 cells should be smooth in both directions: no step over 2 land units between neighbouring cells
   that the stamps didn't make. Add a test for it.
2. **Rivers in straight segments:** the river is three ruler lines from the spring to the cove. Make it wander by
   seeded noise inside its corridor (as roads do; the paper prototype's rivers), wider downstream, still only downhill.
3. **A grey rectangle** at about (0-330, 1100-1130) west of the river's turn, under the valley's north edge: find what
   it is (a stamp, a lake with no water, a blend fallback) and fix it or say why it's right.
4. **The central plateau** is one white dome three squares wide: give the band's relief variety (ridges, a shoulder, a
   saddle) so it reads as mountains, not a cap.
Keep: chunks pure and order-free (goldens may change, update them in one commit and say so), the valley byte-identical,
seams matching. Layer 1; the PC compiles and lands it.
