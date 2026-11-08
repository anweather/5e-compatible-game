Needs: core
Area: unity/Assets/Prototype/Chemistry/World*.cs (the cell hash), unity/Assets/Prototype/Scripts/Net/NetChem*.cs (the
checksum), their tests; world/maps/flat-1728 (move it); MapIdentity.cs
Branch: chunk-hash

# A cell hash per chunk, so a big map's lockstep check doesn't hitch (world squares; given to the Mac)

The Mac's map-size measurement (3ec4718c): `NetChem.Checksum` calls the full `World.CellHash` every 30 ticks (5 s) on
the main thread; on a 1728 x 1728 map under Mono that's 190 ms each time, a hitch every 5 s online. It's the blocker
for an online 3 x 3 playtest.

1. **A cached hash per chunk,** recomputed only for chunks changed since the last checksum (dirty flags set where
   cells change), combined in a fixed chunk order into the same kind of world hash. Deterministic (D4): every peer gets
   the same value for the same state. A test: after random edits, the incremental hash equals a full recompute, on
   the valley and on flat-1728, over many ticks; and the existing lockstep goldens still pass, or move on purpose if
   the combined hash's definition changes (say which).
2. **Measure:** the checksum's cost per call on the valley and on 1728 before and after, under .NET and in the Editor.
3. **flat-1728 out of the shipped maps:** move it to a test-fixtures folder (not `world/maps/`), so `MapIdentity.Offered`
   doesn't hash its 3 MB `cells.txt` at every join and builds don't ship it. Update MapSizeBench to read it there.
Layer 2 and 3 of `docs/TESTING_LAYERS.md`: full EditMode once (this changes `Chemistry/`), the `online` probe, and
land it yourself with `land.sh auto chunk-hash --compiled`.
