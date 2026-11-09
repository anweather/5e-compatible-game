Needs: core
Area: the world plan's landmark solve (Chemistry/ WorldPlan*, GeneratedMap*), world/maps/nine/map.json, world/horizon.json, their tests
Branch: nine-volcano
Priority: next for u2

# The nine shows a volcano in the fog (u2)

Phase 6's done line (`docs/design/world-squares.md`, the phase table) is "walking from the valley into a second square,
a volcano in the fog". `far-ring-view` (landed 1eec423c, section 4 "As built") found that the nine's seed `valley-1`
places no spire and no volcano, so its far landmarks are only the great tree, the castle, a ruin and lone great trees.

1. **Say why:** does the plan's "every type at least once where the land allows" (section 4, after LandmarkBaking) run
   for the 3 x 3, and what stopped the volcano (no band high enough? no square left after the town and castle?).
2. **A must-have:** section 5's "must-haves" (line ~132, "a volcano") for the nine: its `map.json` asks for a volcano,
   and the solve places one where the land allows, deterministically from the seed. If the 3 x 3 can't hold one, say
   why rather than forcing it. A sea spire is welcome where an ocean square allows it, but isn't required.
3. **horizon.json:** the great tree's stand-in reads as a pale olive lens from the spawn. Darken it or give it the
   distance blue, and check the `seenFrom` values against the PC's finding (landmark haze is the land's within `sight`,
   then 90% at `seenFrom`). Change values only; the shapes and shader are the PC's.
Tests: same seed, same plan; the nine's plan holds a volcano; the valley stays byte-identical. The nine's golden changes
if the plan changes (one commit, say so; bump the generator version if chunks change). New fixtures need
`[Category("Unit")]` or `[Category("Simulation")]`, and in Unity `Does.Contain` is for strings only (Chemistry
CLAUDE.md). The PC compiles and lands it, then re-shoots `tools/probe.sh farringclip`.
