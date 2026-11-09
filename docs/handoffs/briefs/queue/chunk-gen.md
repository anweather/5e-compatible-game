Needs: core
Area: unity/Assets/Prototype/Chemistry/ChunkGen*.cs and Chemistry/Tests/ChunkGenTests.cs (new); BiomeField (anchors
from the plan); Chunks/ThingId/the cell store only where negative chunk rows need it; world/maps/nine/ (new world file);
no valley map files change
Branch: chunk-gen (off origin/world-plan until world-plan lands on dev, then merge dev)

# ChunkGen: a chunk from seed and plan (world squares phase 4, u2; critical path to the 3 x 3 playtest)

`docs/design/world-squares.md` sections 5 (level 2), 6 and phase 4 in section 9. `Chem.WorldPlan` (branch `world-plan`,
being compiled on the PC now) is the input; the paper prototype `tools/world-paper/index.html` is the visual spec.

For the first playtest the rings wait (J-080): all nine squares are generated at load, so phase 5 is skipped for now and
the PC goes straight to phase 6 with what this makes. Keep the API one chunk at a time so the rings drop in later.

1. **`ChunkGen.Make(seed, plan, cx, cy)`**, pure: reads only the plan and functions of position (BiomeField weights with
   anchors from the plan, band heights blended over 300 cells, relief, river channels carved by distance to the plan's
   polylines, roads drawn through the matched side slots, landmark stamps whose footprint reaches the chunk, then plants
   and things seeded per spot, appended per chunk so ids hold). Never reads a neighbouring chunk's cells.
2. **The valley pinned** at its square: those chunks come from `world/maps/valley/` unchanged, and the chunks next to it
   meet its sides (the plan already reads the valley's sides from `land.json`).
3. **Negative rows and columns:** check `Chunks.ChunkOf`, the cell store's indexing and `ThingId`'s packing take them,
   or place the nine with the valley's origin so none are needed. Say which you chose and why.
4. **The world file** `world/maps/nine/map.json` (seed, 3 x 3, generator version, the valley pinned) and the input hash
   as section 6 says (`MapIdentity` hashes inputs, not output).
5. **Output a map the PC can load:** whatever phase 7 of maps-as-data (landed 18f59b04: `-map`, sea from land,
   `map.json` gives sea, yard, spawn) needs to start the nine as one 1728 x 1728 map. Generating into the player's data
   folder at load is the end state; a bake into a gitignored folder is fine for the first playtest if it is faster.

Done: goldens of chunk hashes made in two orders (the same), seams that match across every chunk side, per-chunk time
and the whole nine's time and memory, and height pictures of the nine (PNG, small) in the report. Layer 1 of
`docs/TESTING_LAYERS.md`; land with land.sh (the PC compiles it: it changes Unity C#).
