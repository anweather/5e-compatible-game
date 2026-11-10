Needs: core
Area: unity/Assets/Prototype/Chemistry/ (the World's map-sized arrays outside the chunk blocks), its tests, docs/design/sparse-world.md
Branch: sparse-world-3 (off `origin/sparse-world-2`, not on dev yet; the PC compiles step 2 first)
After: sparse-world-2

# The sparse World, step 3: the core's other map-sized arrays (#204, D17)

Step 2's bench on the nine: the World 317.6 MB before, 84.5 MB after, of which 57 MB is step 3's: the core's arrays
still sized by the map (the design lists them, up to ~0.8 GB at 9,720 chunks). Make each per chunk (in the block, or
a sparse map keyed by chunk for what's rare), or say why one must stay whole and what it costs on the Scale Island.
Hot paths allocate nothing; goldens and the save golden unchanged; the late-join and packing tests still pass.

Measure the World's memory on the nine and on the Scale Island (under dotnet, the island's chunks made one row at a
time as the lean bake does, if the VM can't hold more) before and after.

The core gates pass. Push the branch; the PC compiles it and tries the island in a player. Report the numbers and
what's left that grows with the map.
