Brief for the PC: look at `props-follow-land` drawn (#142). The Mac checked the carry rule in EditMode, but no stamp has moved a real prop yet.

Stamps run in a game on the valley, in open country with crates or barrels on it (place some with `act` if none are there).

1. **Dome, 15 m, radius 15.** Crates ride up as it eases: 0 off the ground at the end, none buried, none flung.
2. **Canyon, -15 m.** Crates sink with the ground and land on the floor; none hang.
3. **Undo each.** Crates go back with the ground and rest where they were, give or take a slide.
4. **An item on the ground** (not held) on a dome and a canyon; **one held** stays in the hand.
5. **A wizard** on the same stamps: lifted on rising ground, falls on sinking, never under it.
6. **Steep:** cone, 40 m, radius 8, with a crate near the top. It slides or tumbles down and rests; it doesn't sink in or fall through.
7. **Pushed mid-ease:** shove a crate as the ground rises. It goes, lands, and rides again.
8. **Inactive chunk:** stamp a far chunk with things loaded, then walk there. They're on the ground, not buried.
9. **Two peers:** the client's crates match the host's through 1 and 3.
10. **Dragon's reset** with crates on a stamp: they end at home on flat ground.
11. **Cost:** `tools/probe.sh perf` through a radius 32 stamp, against the numbers in `maps-stamps-pc-run.md`. It adds one physics query per moving stamp each frame.

**Report:** what each did, by number, and the frame times for 11.
