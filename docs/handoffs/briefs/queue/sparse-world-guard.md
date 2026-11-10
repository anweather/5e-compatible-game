Needs: core
Area: unity/Assets/Prototype/Chemistry/ (World.Pack.cs, the unpack on read, World build paths: MakeFrom, KeepStart, the map-sized arrays), its tests
Branch: sparse-world-guard (off origin/dev bec28fc5 or later, or fold into sparse-world-3 if it's still open: say which)
After: sparse-world-3

# Step 2's follow-ups from the PC (sparse-world-2 landed bec28fc5)

1. **Unpack off the main thread must fail loudly.** On the PC, view workers (the terrains' arrays worker, the ground's
   Parallel.For) read cells; a read of a packed chunk unpacked it off the tick's thread, put a chunk in the unpacked
   list twice, and `PackDue` threw in `EndTick` 701 times on one walk. The view now unpacks on the main thread first,
   but the next worker that reads cells would corrupt the world silently. Make an unpack from any thread but the one
   that owns the World throw a clear exception (or make reads of a packed chunk from other threads decode without
   unpacking, read-only); a test for each. No cost on the tick's own reads.
2. **The Scale Island still stops in the World build.** In a player with 4.4 GB free, private memory went 2.06 ->
   3.62 GB between 7 and 8 s of the World build (39.8 M cells), before `WORLD BUILT`, and reached 4.47 GB at 41 s.
   Name what allocates there (MakeFrom, KeepStart, the map-sized arrays of step 3, BakedSource reading cells.txt
   whole) with a dotnet run building the island's World from its bake, and fix it or say what step 3 covers.
   The island's bake is a map folder the VM can make (the bake is 20 s, 60 MB peak).

The core gates pass. Push; the PC compiles it and tries the island again.
