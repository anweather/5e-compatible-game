Needs: core
Area: unity/Assets/Prototype/Chemistry/ (RollingHash and the World's chunk accessors), core tests
Branch: chunk-hash-168

# A per-chunk dirty cell hash for lockstep checks (#168)

Read issue #168 and `RollingHash`'s header first. Today RollingHash hashes a world-sized slice each tick (valley 30
ticks to cover, the 1728 x 1728 nine about 135, ~1.2 ms a tick under Mono), so a desync on a big map shows after
~22 s, not ~5 s. A cached hash per chunk, recomputed only for chunks changed since the last check, catches a desync
at once at a fraction of the cost.

It waited for D9 (cells behind accessors). That is done: `sparse-world-1b` made nothing read `World.Cells`; every
cell goes through `World.ChunkCells` / chunk accessors, and chunks are packed (`sparse-world-2`) or per-chunk arrays
(`sparse-world-3`). So change tracking can sit at the chunk, not at 62 write sites.

Do:
1. Design note first (a short section in `docs/design/` or the ADR for lockstep; see Lockstep #99): where a chunk
   is marked dirty (the one place a chunk's cells are written or unpacked-for-write), and how a packed chunk hashes.
   If marking dirty at the accessor can miss a write path, say which and stop to report instead of guessing:
   false or missed desyncs are worse than the slow hash.
2. Implement: a cached FNV-1a hash per active chunk, recomputed only when dirty; the rolling check combines them in
   a fixed (ordered) chunk order. Deterministic: integer state, no Unity, no iteration over a hash set.
3. Goldens: `CellHash` / FNV-1a goldens in `TerraformTests.cs` and `ChemistryTests.cs` must not change (the
   whole-world hash is unchanged; this is a faster way to the same check). Add tests: a change in one cell changes
   the combined hash; unchanged ticks recompute nothing (count recomputes); a late joiner's replay equals the host's.
4. Measure under dotnet: the nine and the valley, ms a tick before and after, in the report.

Don't: touch the Unity view or `Scripts/`; change rules JSON; port anything to the TS sandbox (frozen). u2 is shut
down, so nothing else is in the Chemistry folder right now except the Mac's `playtest-tools` (Scripts/ only).
Done when: `dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes`
pass. Branch pushed; the PC compiles it in Unity (it changes Chemistry/, which Unity also builds) and lands it.
Report: commit, test counts, before/after ms a tick, and the writes you could not prove are covered.
