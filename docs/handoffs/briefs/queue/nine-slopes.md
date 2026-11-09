Needs: core
Area: unity/Assets/Prototype/Chemistry/ChunkGen*.cs, GeneratedMap*.cs and their tests; how cells.txt marks stone
Branch: nine-slopes
Priority: before core-boundaries and late-join-core (it blocks the 3 x 3 playtest)

# The nine's steep slopes are walls (u2; blocks walking out of the valley)

play-nine (landed 487f537e) found: ChunkGen writes `#` on every steep slope, and `#` plays as a blocking stone wall.
That's about 35k cells, including nearly the whole seam along the valley's east and north edges (a few gaps), so a
player can hardly walk out of the valley.

1. **Say what `#` means where it's read** (the cell store, the view, walking) and what the valley's own steep faces
   use. Steep generated land should be walkable or climbable as the valley's hills are, and only true cliffs (a drop a
   player couldn't climb in the valley either) or a landmark's own stone block.
2. **The seam:** the valley's edge must be crossable along most of its length, and always where a road, river crossing
   or the plan's edge slots meet it. Add a test: from the valley's spawn, a walk reaches every square's spawn across
   each of the valley's open sides (the spawn test already walks; make it honour `#` as a wall, so it would have caught
   this).
3. Count `#` cells before and after on three seeds, and say how many remain and why.
Keep the valley byte-identical; goldens may change in one commit (say so). Layer 1; the PC compiles and lands it.
