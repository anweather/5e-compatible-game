Brief for the PC: the Hold, phase 4, what waves look like, and the first online run. Read `docs/design/the-hold.md` (phase 4). Branch `waves-view` off dev (722cc8ee or later). View, plus core only if a run finds a bug. Rebuild the yard first.

**Build:**
- **Warning:** `HoldWarning` on the HUD and on the castle: the next wave, theme, boss and a countdown.
- **Boss:** its name and a health bar that plainly reads as bigger than the rest.
- **Barricades:** a look of their own, appearing at the wave's start, breaking and visibly burning.
- **Climbers:** `cliff-stalker` goes straight up the slope and should read as climbing, not gliding.
- **Flyers:** `ash-wing` needs height and a shadow, and to drop visibly when frozen or wet.
- **F8 moment list:** check it opens, reads and starts a moment in the Editor and a dev player.

**Check with two real peers:**
1. Both go into a moment and play it with no desync; a late joiner comes in mid-wave.
2. A full wave with a boss, a barricade burning, a climber and a flyer on all peers.
3. Is the warning's 25 s enough to walk from the wall to the keep and attune? Time it on foot.
4. Raiders take the road in the player and stay out of Hollowmere.

**Keep:** the sandbox unchanged, words from the glossary, `hold`, `hands`, `handlooks`, `moments` probes green.

**Report:** one short message: the commit, gates with exit codes, the four checks, frame time in a late wave against an early one, and shots at 800 px plus one clip of a wave. Don't merge it. Andrew plays after this, so say how he starts a moment and which moments are best first (`cinders-*`, `roots-*` are the two themes the curve couldn't judge).
