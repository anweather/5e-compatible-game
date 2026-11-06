Brief for the PC: judge maps phase 5 on a real GPU, then fix the stamp frame. From `dev` (9372ba25 or later). One Unity job at a time. The Mac is on `hold-castle`; nothing here needs it. Don't ship `Builds/PcDev` or `Builds/PcRel`: they contain `PcPerf`.

**Part 1, the ground switch** (`GroundSwitch`, modes off, on and land; `docs/FAST_LOOP.md` has the `groundbuild` probe). Windows player, real window, 1080p:
1. The look, off and on side by side, including the far-terrain base map. Are they the same?
2. Walk the chunk seams with it on. Any cracks or steps?
3. Start-up, off, on and land. Land added about 2 s on the Mac, and the limit is 2 s.
4. Frame cost under load, off and on.
5. Grass growing back after clearing (`RestoreDetail`), which has never run in play.
6. Two peers with a late joiner, on. Land mode's late-joiner offsets wait for phase 8.

Pairs of shots go in the report as paths for the PM. Land mode's green mountains and brown beach are known; say only if something is worse.

**Part 2, on a branch `stamp-frame`, #151:** spread `CellHeightfield.Update` over frames or fill it per chunk. First check what `World.Land` and `World.Weather` need of it. The shared world must not depend on a half-filled heightfield (D4); if it does, stop and say so. No golden moves. Measure r32 land and undo, the dragon's reset of a stamped valley, and a joiner's replay against your profiling numbers.

**Report:** one short message: part 1's six answers, then the commit, gates with exit codes, the before and after, and whether the Unity C# compiled. Don't merge it.
