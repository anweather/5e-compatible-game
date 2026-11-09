Needs: core
Area: unity/Assets/Prototype/Chemistry/ (the world plan, known chunks, landmark data) and their tests; rules or map data for landmark types
Branch: far-ring-core
Priority: after nine-slopes
After: the PC builds the view half (known-chunk meshes, landmark far meshes with their own haze, a sea plane)

# The far ring, core half (u2)

`docs/design/world-squares.md` section 3 (known chunks) and section 4, with its "On paper (2026-11-09)" notes. The
paper prototype is `tools/world-paper/` (its Horizon panel). The view half is the PC's; this brief is only what the
view reads, all of it a pure function of the seed and the plan, nothing shared depending on it.

1. **Known chunks:** for each chunk within `know` (7) of a character and past `sight`, hold the dominant biome and
   heights on a 9 × 9 grid (every 8th cell: `BiomeField` at 81 samples plus relief), as section 3 says. Stop one chunk
   past the coast. An accessor the view polls (chunk id to biome and heights), filled off the tick, handed over at a
   tick boundary in chunk order. Measure the load cost (the design guesses ~15 ms for 144 chunks).
2. **Landmarks from the plan:** each landmark's type, position, rough size and `seenFrom`, which is a **radius** in
   chunks, not a Chebyshev square. Values from the paper: volcano and spire ~14, castle, great tree and outcrops 9-10;
   nothing below `know` + 2 (today's 5, 6 and 3 for great tree, castle and lake sit inside the known ring). Put them
   where landmark types live (rules or map data), not in code.
3. Tests: same seed gives the same known ring and landmark list on two runs; the ring stops at the coast; a landmark
   is listed from a character exactly when within its `seenFrom` radius.

Keep the valley byte-identical and the goldens unchanged (this adds data; it changes no cells). Layer 1; the PC
compiles and lands it.
