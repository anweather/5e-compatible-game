Needs: unity
Area: ValleyCoast, Ocean, WaterCurrents, the shore map, AmbientLife, ChemYardBuilder (rows, spawn), TerrainGrid,
grid.yardCells and the lake level (unity/Assets/Prototype/Scripts and Scripts/Editor); a bake menu and its core test;
world/maps/valley read only except a re-bake that must be byte-identical
Branch: maps-phase-7

# Maps as data, phase 7: the sea and the rest of the valley's assumptions (the PC; critical path to the 3 x 3)

`docs/design/maps-as-data.md` row 7, plus what phase 6 (a6d42718) left: `ValleyCoast` drives the sea, beach, shore
map and `WaterCurrents`, and `AmbientLife` sits inside the coast root; `ChemYardBuilder` still lays the yard rows and
spawns the wizard at height 0.1; `grid.yardCells`, the lake level and `TerrainGrid.Valley()` assume the valley.

0. **First, a bake menu:** Prototype > Bake Map Data rewrites a map's `ground/` and `cells.txt` from its stamps,
   villages and rows (phase 6 removed the old bake, so a `stamps.json` edit no longer reaches the drawn ground). Prove
   it rewrites today's valley byte for byte. Then a core test that a map's inputs hash, stored in `ground.json`,
   matches its inputs, so a stamp edit without a re-bake fails loudly.
1. **The sea from `land`:** `Ocean` with the sea level from `map.json` and a mask from `land`; the beach and shore map
   from `land` (a runtime step or in `ground/`); `WaterCurrents` from the same. Lakes and coves stamped into `land`
   show as water (the Mac's 1b finding: a stamped hollow held no water and the valley's sea ignored a baked cove).
2. **The rest:** `AmbientLife` out of the coast root; yard rows and spawn from the map (spawn on the ground at the
   map's spawn place); `grid.yardCells`, the lake level and `TerrainGrid` from `map.json` and its size (map-size,
   3ec4718c, made `MapCheck` take any size).
3. **Prove a second map loads:** the test map in its fixtures folder (the Mac is moving flat-1728 there) starts and
   you can walk it, with sea where its land is below the level.
Keep the valley identical: before/after shots from the town, the castle and the shore at 1280 px (check at 480 px),
probes `smoke`, `online`, `swim`, `hold`, `groundbuild`, `landshapes`, and GroundLandTests. Layers 2 and 3 of
`docs/TESTING_LAYERS.md`. Push each step that keeps the valley identical; remove this claim in the last commit. The
lead lands it.
