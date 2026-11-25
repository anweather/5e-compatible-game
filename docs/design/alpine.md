# The alpine massif (hills-12x10)

Andrew, 2026-11-24: "Let's add some high elevation alpine meadows in the middle with mountains around it and some
mountain passes. Then one region of elevation transition between that and the grasslands. And then start carving some
rivers based on real hydrology models for water flowing down the mountain from an alpine spring."

This is step 4 of the hills (docs/design/hills.md): the rolling hills and their roads, with an **alpine massif** in the
middle of the map. Its pieces, in order: the meadow and its ring of mountains with passes (1), the foothills between it
and the grassland (2), and rivers from a spring (3). 1 and 2 landed together (a ring of mountains with no foothills is a
cliff, and the foothills are part of the same shape); 3 came after Andrew chose where the rivers end (below, "Rivers").

## The shape

`map.json`'s `"hills"` takes `"massif"` (`HillsMassif`, Runtime/HillsMassif.cs):

```json
"massif": { "centre": [3456, 2880], "meadow": 700, "crest": 1050, "foot": 1450, "reach": 2650, "warp": 220,
  "meadowHeight": 1000, "peaks": 2000, "foothills": 160, "passWidth": 260, "passKeep": 12 }
```

The massif's rise is added to the hills' own land (`HillsLand.At` = `Rolling` + `Massif.At`), a pure function of the
cell, the seed and map.json, in integers only, so chunks made in any order, on any thread and on any peer agree:

- **Distance** from the centre, the short way round the torus, wanders by up to `warp` cells (two octaves of value
  noise, broad and fine), so the ring isn't a circle. Radii in cells: `meadow` 700 (1.05 km), `crest` 1050, `foot`
  1450, `reach` 2650 (4 km). The massif stays 230 cells clear of both seams (checked when it's read).
- **The meadow** (inside `meadow`): the hills' rolling lifted by `meadowHeight` (1000 units, 125 m). It stands 133 to
  151 m (the hills' rolling on it).
- **The ring** (`meadow` to `foot`): a ridge rising to its crest at `crest` (smoothstep each side), its height 0.4 +
  0.6 r² of `peaks` (2000 units, 250 m) over the meadow, where r is three octaves of ridged noise (lattices 360, 150 and
  60 cells, each on axes turned its own way so no crest runs along the lattice). The highest peak is 385 m.
- **The passes**: where a main road crosses the ring. When the roads are laid, their main lines go to the massif
  (`SetPasses`), which works out a coarse grid (every 16 cells over the ring's box) of how near each point is to one;
  within `passWidth` cells (390 m) of a main road's line the ring sinks to `passKeep` (12%) of its height (smoothstep),
  so a pass is a valley along the road with its col at the crest. Main row 4 and main column 5 run through the middle,
  so there are four passes (north, east, south, west); the road over them tops out at 157 to 181 m, with the mountains
  either side 267 to 368 m.
- **The foothills** (`foot` to `reach`): the uplift eases down from the meadow's to nothing (linear with eased ends, so
  its steepest is 1.25 times its mean, about 10%), with rolling bumps of their own (`foothills`, 160 units = 20 m, a
  300-cell lattice) that are most half way and nothing at either end. This is the transition region: rising land, not a
  cliff.

Land runs 45 to 3079 land units (5.6 to 385 m). The default drawable range is -136 to 2176 (-17 to 272 m), so the map
sets `"drawable": [-136, 3600]` (450 m), which the ground pack's heights spread over its 32766 steps (1.4 cm a step).
Cells' own `Height` (0.5 m steps, -6 to 8) is separate and unchanged.

**Ground by slope** (`ChunkGen.MakeHills`): a hills chunk works its land out a cell past its edges, and a cell off the
road is bare **rock** (`rock`, `'^'`) where it steps 7 land units or more to a neighbour (about 30 degrees, as
`World.LandSteep`), **scree** (`':'`, bare `dirt`) from 4, else grass. Scree is dirt, not `rubble`: rubble is dressed
with a GameObject a cell (CellDressing), thousands a chunk on a mountainside. The rolling hills never step 4, so outside
the massif a chunk is what it was (the spawn's and the east seam's road goldens are unchanged).

## Roads in the mountains

- **No lesser road into the ring.** The network is laid after the massif's shape: a side only gets a lesser road if
  neither square's straight line from that side's slot to its hub (and 40 cells either way, an arm's wander) touches the
  ring. 8 world squares in the ring have no road; the other 112 are all on the network. Main roads are kept: they are
  what the passes follow.
- **A grade limit** (`"roads"` `"grade"`: 12%, 1.44 land units a cell). Once the land is whole (massif and passes),
  every arm is checked (`HillsRoads.Grade`): sampled 4 times a piece along its line, an arm whose land steps steeper
  than the limit anywhere gets a **profile**, half way between the highest line under the land that keeps to the
  grade (a cut) and the lowest over it (a fill), and within the grade of its ends. Within 16 cells of either end it
  keeps to the land, so it meets the arms at its hub and over its side whatever they do. A road cell on a graded arm
  takes the profile's height at its nearest point on the line; the bank leans back to the land over 6 cells as before,
  so a deep cut's bank is steep enough to be rock. An arm whose two held ends are further apart than the grade allows
  (none on this map now, after widening the foothills) gets the grade it needs, a little over (`Arm.Over`). Arms that
  are gentle enough keep the land's own line, so the hills' roads away from the massif are unchanged.
- 29 arms are graded. The deepest cut is 2.5 m; the highest fill is 12.5 m, where main column 5 crosses a dip in the
  north foothills on an embankment (its banks steep enough to be rock). No switchbacks are built (Andrew hasn't decided cuts against switchbacks); a road just takes
  the grade it can and is cut and filled to it.

## Seeing it from far off

The known ring ends about 670 m out (its haze 98% there), so from the spawn, 6.5 km away, the known chunks never reach
the massif. `FarRing.Massif.cs` draws the massif whole as one mesh: its land every 16 cells (24 m) wherever the massif
rises 15 m or more, rock where it's steep and high on the ring (from 31 m over the meadow, all rock by 112 m), with a far
landmark's own thin haze (FarLand.shader; 90% at 12 km), so its mountains stand on the horizon from anywhere. It's made on
a worker once the far ring is up. It stands 3 m under the land and isn't drawn nearer than the known ring's edge (the shader's new
`_NearClip`), so inside the known ring the chunk terrains and known meshes are the land and it never shows through
them. On the torus it stands at the copy nearest the camera.

## Tests

`HillsMassifTests`: the meadow stands at least `meadowHeight` over the hills and the ring's peaks over 100 m above it,
past its reach the land is the hills' own, and the drawable range holds the highest land; each main road crosses the ring
through a pass (its road at most the col, the land well off it 50 m and more higher; four passes at least); no lesser
road's line touches the ring; rock and scree on the mountains, grass more than rock, and every edge cell of the sampled
chunks (ring, meadow, foothills, a pass) is the land and the ground the cell alone works out, so chunks meet; chunks made
in reverse on many threads by a second load hash the same, and a known chunk samples the made chunk's land; goldens for
a ring chunk (70, 45) `9061c639` and a foothills chunk (86, 45) `9c15349d`. `HillsTests` and `HillsRoadsTests` now
allow rock and scree, check the grade along the main roads (at most 4 land units between neighbouring cells of a walk
along a line: 1.44 a cell, and a diagonal step can move the line's nearest point two cells) and that every square off
the network is in the ring; the meadow's middle chunk (54, 45) is `8e975151` (it was `4ceac909`). The late-joiner test
(two peers walking the main road over the east seam, a joiner replaying and one loading the save) is unchanged and
passes.

## Results (2026-11-24, M2 Pro, development player, 1920 x 1080, `-fps-probe -perf-offscreen -perf-memory`)

Scripts and logs: `~/5e-logs/alpine/` (`all.sh`, `table-a1.txt`, `a1-*.log`, `*.rss.csv`). Frame times in ms. The stand,
seam walk and road walk are the roads' (hills.md); the **pass walk** is new (`-perf-scenario roadwalk -perf-road column,5
-perf-from 3300,500`, `PerfStage.RoadWalk.cs`): the wizard starts on main column 5 at the foot of the north foothills
(about 60 m up) and walks it south for 480 s, 3.2 km: up through the foothills, over the north pass (its col about 175
m) and down into the meadow, every frame on a road cell.

| Run | mean | p95 | p99 | worst | made on a read in the run | peak RSS |
|---|---|---|---|---|---|---|
| stand at the spawn x3 (30 s) | 9.98-10.00 | 12.04-12.15 | 12.5-12.7 | 38 (the 20 s frame) | 0 | 1.57 GB |
| seam walk x3 (150 s, 4 crossings) | 9.76-9.78 | 12.03-12.07 | 12.7-12.8 | 42 (the 20 s frame) | 0 | 1.66 GB |
| road walk x3 (480 s, 3.0 km, over the east seam) | 9.92-9.93 | 12.18-12.19 | 13.0-13.1 | 41.5 (the 20 s frame) | 0 | 1.90 GB |
| pass walk x3 (480 s, 3.2 km, over the north pass) | 9.38-9.39 | 12.40-12.43 | 13.2-13.3 | 25.6-29.7 | 0 | 1.91 GB |
| bot walk seed 1 (not in the bar) | 9.68 | 19.43 | 43.4 | | 0 | 1.87 GB |

- 0 exceptions and 0 errors of ours in all 13 runs (the 6 Metal pipeline errors for URP's Particles/Lit are every
  map's). PLAY READY in 4.4 to 4.7 s every run (4.2 to 4.5 before); no stall.
- Load: the massif's setup at load (its pass grid and the roads' grading, on the main thread as the map is read) adds
  about 150 ms (the "ground terrains placed" step, 417 to 570 ms against 272 to 427). The far massif's mesh (75,054
  vertices) is made on a worker. In .NET the map loads in 210 ms with the massif.
- Chunks made on a read: the 41 at load in every run (47 to 54 for the pass walk, which starts 5 km from the spawn: the
  wizard is put down there before the run starts); none during any walk.
- Chunk make time (worker): at load 823 to 1045 ms for 231 chunks (3.6 to 4.5 ms a chunk, with the far ring's and the
  massif's meshes making beside them); during the road walk 3.2 ms a chunk (1.8 before), during the pass walk 3.6 ms in
  the massif. The road walk's cost was cells near the massif's box working out its noise only to find they're past its
  reach: `Touches` is now a circle, and a road walk after it (`a2-road-1`: mean 9.92, p95 12.19) makes a chunk in 2.3 ms;
  a pass walk after it (`a2-pass-1`: 9.39 / 12.43, worst 26 ms) is unchanged at 3.6 ms a chunk in the massif. In .NET,
  1.2 ms a chunk in the massif and 0.85 at the spawn.
- The pass walk's worst frame is 30 ms and none is over 30: crossing the foothills and the pass costs nothing the hills
  didn't.
- The bot's p95 misses as before (its fire; tracked separately).

Stills (800 px), `~/5e-logs/alpine/shots/`: `from-spawn.jpg` (the massif on the horizon from the spawn, 6.5 km off),
`from-grassland.jpg` (from the grassland 2.7 km east of the centre), `foothills.jpg` (the north pass from the foothills'
foot, the main road winding up to it), `pass.jpg` (at the north pass's col on the road, looking back down its valley),
`pass-in.jpg` (from the col toward the meadow), `meadow.jpg` (from the middle of the meadow, the ring all round),
`high.jpg` (60 m up on the ring's inner flank). Past the known ring's edge (about 670 m) the land is at 98% haze and the
far massif stands clearer beyond it, so a pale band of haze lies between the near land and the mountains in every view;
from 60 m up it reads as a flat pale floor (`high.jpg`). That's the map's short land haze (a streaming choice) against
seeing the mountains far off; thinning the land's haze would show more land to the horizon, which the known ring would
then have to draw.

## Rivers: where they end (Andrew chose B)

Step 3 waited on this; Andrew chose **B** (2026-11-24): a lake in the meadow, spilling through one generated gorge in the
ring away from the roads, then a river down the foothills to a lake in a grassland basin, with fords where it crosses
roads (no bridges; a main road too, if it must). The options as they were put: The map has no sea, so every river ends in a basin, and the massif makes one big one: the meadow
lies 133 to 151 m up and its lowest way out is a col at 157 m or more (the passes' cols, where the main roads go), so it
is a closed basin about 24 m deep. Water from a spring on the ring's inner flank runs down into the meadow and, if
nothing takes it away, fills the meadow to the lowest col and spills down that pass's valley, along the road. A first
look (`HillsMassifTests.WhereASpringsWaterGoes`, explicit: steepest descent on a 16-cell grid, filling each pit to its
spill) also shows how many small pits the coarse land has (most fill with under a hectare and spill on), which is why
the rivers need depression filling (priority-flood) before routing, as planned.

The options:

- **A. A tarn in the meadow** (endorheic). Springs high on the ring's inner flank; the rivers run down into the meadow
  and end in a lake at its lowest point, its level set under the meadow's spill (its size a map setting, say 0.1 to 0.3
  km2), as a lake in a dry basin stands where evaporation matches what flows in. Short rivers (0.5 to 1 km), all inside
  the massif; the meadow stays a meadow; no road crossings but the meadow's own (fords).
- **B. A tarn, then out through a gorge.** As A, but the lake spills: the generator cuts one gorge through the ring
  (away from the road passes) to just under the tarn's level, and the river runs on out of the massif, down the
  foothills, and ends in a lake in the first grassland basin that holds it. The whole story of a mountain river, 3 to 5
  km; it crosses lesser roads (fords) on the way down.
- **C. Out over the lowest pass.** The honest fill-and-spill: the river leaves by the lowest col, which is a road's
  pass, so river and road share the valley floor down the foothills (fords at every crossing, or the road on a bank
  beside it). Natural, but the roads were routed through the passes for themselves, so they'd fight the river.
- **D. Springs on the outer flanks only.** Rivers run outward down the foothills into the grassland and end in lakes in
  its basins; the meadow stays dry.

Recommended: **B** (it shows the most of the hydrology and keeps the passes for the roads), or **A** if a river that
never leaves the massif is enough. Whichever: rivers are static `'r'` cells in a carved bed (the `river` terrain's
`spring` keeps them topped to the bank, as the valley's), lakes `'w'`, worked out at load from map data on a coarse grid
(priority-flood, D8, flow accumulation, all integer) and carved by each chunk as it's made; a road over a river is a
ford (the bed shallow under the road), no bridges.

## Rivers (step 3)

`map.json`'s `"hills"` takes `"rivers"` (`HillsRivers`, Runtime/HillsRivers.cs):

```json
"rivers": { "springs": 2, "springUp": 180, "lakeDepth": 20, "gorgeWidth": 110, "gorgeFall": 4, "minLake": 24, "minEndLake": 120, "endLake": 300 }
```

**The gorge** (`HillsMassif.SetGorge`, cut when the generator is made, after the passes and before the roads are
graded): it starts at the meadow's lowest point (sampled every 16 cells), runs out the way, of 64 round evenly, that no
pass reaches through the ring (the middle of the widest such run), wandering up to 70 cells either side of its line by
seeded noise. Within `gorgeWidth` cells of it the ring is taken away and the land is cut down to the gorge's floor:
`lakeDepth` land units (2.5 m) over the meadow's lowest land as far as the crest, falling `gorgeFall` (4%) past it. So
the meadow is a basin whose lowest way out is the gorge, 2.5 m over its bottom.

**The drainage** (`HillsRivers`, worked out once as the map is read, in integers, from map data only): the land is
sampled every 16 cells round the whole torus (432 x 360 nodes) and
- **filled** by priority-flood (Barnes et al. 2014) from the map's lowest node, 8 neighbours, a binary heap keyed by
  level then arrival, so ties go first come first served and water crossing a filled flat runs toward its spill;
- **routed**: each node drains to the node that reached it in the flood (D8), so every flow line ends at the lowest
  node, none in a pit;
- **accumulated**: each node's catchment (the nodes draining through it), summed in the flood's order backwards, so it
  only grows downstream.

**The rivers**: two springs on the ring's inner flank (`springUp` 180 cells out from the meadow's edge, where no pass
reaches, a sixth of the way round or more from the gorge and each other, picked by the seed). From each, down the flow
lines. A filled depression on the way of `minLake` nodes or more is a **lake** at its fill level (the meadow's lake,
0.45 km2 at 136 m, and a pond in the foothills); a smaller one the river cuts through. The first depression of
`minEndLake` nodes or more past the massif's foot is the **end lake**: filled from the hollow the river comes down to
(steepest descent), or the nearest other hollow of that basin, up to `endLake` nodes (and any at the same level), its
level its highest node's land plus a unit: well under the basin's spill, a lake with no outlet. An end lake that would
come within 34 cells of a main road's line isn't taken; the river runs on to the next. Here: 0.18 km2 at 15 m, by the
spawn's main road. The second spring's river runs into the first's (in the meadow). The main river is 6.8 km from its
spring at 252 m to the end lake (about 4.5 km of it past the gorge); the tributary 2.4 km.

Along each river, the **surface** at each node is its lake's level in a lake, else its land less a depth that grows with
the catchment (0.5 m at a spring to 2.5 m), never higher than upstream and never lower than the next lake down (or where
it joins a river already laid); the **half width** grows with the catchment (1 to 5 cells). The nodes' line is unwrapped
(it runs on over a seam) and its corners cut twice (Chaikin, ends kept), surface, width and catchment cut alike, so they
still never rise / shrink / fall.

**A chunk carves its own part** (`ChunkGen.HillsPoint`, a pure function of the cell, so chunks meet at their edges and
over the seams):
- a cell by a lake's nodes whose own land is under the lake's level is lake (`'w'`, at the level);
- a cell within a river's half width is river (`'r'`, at the surface there); where a road crosses it, a **ford**;
- a road cell near a river comes down to its surface at the road's grade (at least 10 cells, at most 120);
- a river's bank leans back from its surface to the land over 4 cells, never under it;
- a road over a lake is a **causeway** a unit over the water (a lesser road over the meadow's lake: main roads are kept
  clear of end lakes, and the meadow's lake is the meadow's lowest ground);
- the outlet: where a lake cell's river runs lower than the lake, the river wins, so the lake spills into it.
Water is static: `'r'` is the `river` terrain (its `spring` keeps it at 4 units, the bank's level, as the valley's
river), `'w'` a pit with water 4; both are water bodies the sim keeps level (no land slope between them), so the rivers
don't flood the map. Fords: 75 road-line points cross water (18 on main roads: in the meadow where the tributary crosses the main roads by their junction,
row 9 by the end lake). Known chunks mark river and lake samples, so the far ring paints them water; the far massif's
mesh paints its lakes water.

**The view**: a hills map's lakes are drawn as flat surfaces (`HillsLakes`, one mesh each over the grid squares round its
nodes, at the copy nearest the camera), as the valley's lake is: their pit cells are lake (`ValleyChemistry.LakeLevelOf`,
now per cell), so the cells' water mesh leaves them alone (a lake is 200,000 cells and more). The rivers are the cells'
water (`CellWaterMesh`). Two view fixes came with them: the water mesh doesn't read a cell of a chunk the source hasn't
made (it makes it ahead on the worker and draws the tile later: reading it made the chunk on the main thread, 15 a river
walk), and on a world that wraps its points are placed at the copy nearest the camera of their own place, not measured
from cell (0, 0)'s nearest copy (a world away from a far cell's: the water was drawn a world off); and its cells' state
folds a cell over a seam (an end lake over the south seam read past its arrays).

**Tests** (`HillsRiversTests`): the flood leaves no pit (every node's fill at least its land, one lowest node draining
the whole map, every other draining to one of its 8 neighbours no higher, its catchment growing downstream); each
spring's river falls all the way (surface never rising, catchment never falling, never narrowing; the springs on the
ring well up the flank; the main river ending at its end lake's level past the mountains; 4 to 9 km; an end lake of 5 to
40 hectares); every cell along each river's line is water and none rises more than a unit, and no river cell of the main
river's chunks is a pit (each has water beside it no higher); the meadow's lake spills through the gorge (it stands at
the gorge's floor; where the river crosses the crest it's in the gorge, no pass there); no lake covers a road and roads
cross rivers by fords; every edge cell of the chunks along the rivers is what the cell alone works out, and their known
chunks sample the same land and water; a river over a seam is the same either side (a small torus whose river runs over
its east seam: the cells either side and a cell's copy a world off read the same river); the rivers and their chunks are
the same on a second load and in reverse on many threads; goldens (the spring's chunk, a reach's, the end lake's); and
two peers walking down the main river from its spring with fire bolts into the water, a joiner replaying and a joiner
loading the save at tick 240, agree chunk by chunk.

### Rivers: results (2026-11-24, M2 Pro, development player, 1920 x 1080, `-fps-probe -perf-offscreen -perf-memory`)

Scripts and logs: `~/5e-logs/alpine/` (`all3.sh`, `a3-*.log`; `a4.sh`, `a4-*.log` after the last water-mesh fix). The
**river walk** is new (`-perf-scenario roadwalk -perf-river`, `PerfStage.RoadWalk.cs`): the wizard starts on the main
river's bank where it runs into the gorge and walks down it, its half width and 4 cells to its right where that's dry,
for 480 s: 2.6 km, through the gorge, past the foothills' pond and over two fords toward the end lake.

| Run | mean | p95 | p99 | worst | made on a read in the run | peak RSS |
|---|---|---|---|---|---|---|
| stand at the spawn x3 (30 s) | 9.96-10.01 | 11.96-12.22 | 12.5-12.7 | 34.7-38.7 (the 20 s frame) | 0 | 1.58 GB |
| seam walk x3 (150 s) | 9.78-9.79 | 12.08-12.15 | 12.7-12.9 | 40.5-44.2 (the 20 s frame) | 0 | 1.67 GB |
| road walk x3 (480 s, 3.0 km) | 9.92-9.93 | 12.22-12.24 | 13.1-13.2 | 42.1-42.4 (the 20 s frame); 65.9 once (below) | 0 | 1.91 GB |
| pass walk x3 (480 s, 3.2 km; `a4`) | 9.41 | 12.43-12.44 | 13.3 | 29.4-40.5 | 0 | 1.95 GB |
| river walk x3 + 1 (480 s, 2.6 km) | 8.37 | 11.95-12.04 | 14.2-14.4 | 35.3-42.7 (chunk crossings) | 0 | 2.09 GB |
| bot walk seed 1 (not in the bar) | 9.67 | 19.70 | 42.1 | | 1 | 1.88 GB |

- 0 exceptions and 0 errors of ours in every run (the 6 Metal pipeline errors are every map's). PLAY READY in 4.8 to 5.1
  s every run (4.4 to 4.7 with the massif alone); no stall.
- **The hydrology at load**: the map is read in 326 ms on a worker (`MAP LOAD` line), 159 ms of it the rivers (sampling
  155,520 nodes, the flood, the routing and accumulation, the rivers and lakes); in .NET about 80 ms. Nothing of it runs
  in play, and nothing grows with the map in play: a chunk carves its part from the rivers' lines near it.
- Chunks made on a read during a walk: none. The first table (`a3`) had one in each pass walk (the cells' water mesh
  finding its banks read a cell of a chunk not made; fixed, `a4`), and before that 15 in a river walk (the water mesh's
  tiles and banks; fixed).
- Chunk make time (worker): during the road walk 2.5 ms a chunk, the pass walk 4.0, the river walk 4.0 (with its water);
  at load 5.2 to 6.0 ms a chunk (231 made while the far ring's and massif's meshes and the rivers are worked out beside
  them).
- Frames over 30 ms: the river walk's are chunk crossings' activations (`ChemGrid.ChunkApply` 12 to 15 ms, the roads'
  were 10 to 12), now with water in the chunks. The road walk's 65.9 ms is one frame of `ChemGrid.RenderOthers` (39 ms)
  and a collection, once in three runs.
- Memory: the river walk peaks at 2.09 GB (1.9 for the road and pass walks): its water.

Stills (800 px), `~/5e-logs/alpine/shots/`: `spring.jpg` (from the main spring on the ring's inner flank, its stream
running down to the meadow's lake, the causeway across it), `meadow-lake.jpg` (on the lake), `gorge.jpg` (into the gorge
from the meadow side), `gorge-high.jpg` (60 m up in the gorge, the river running through it between its walls),
`river.jpg` (a reach in the foothills, its banks), `ford.jpg` (main row 9 coming down to the river and over it),
`ford-lesser.jpg`, `end-lake.jpg` (the river running into its end lake), and the massif's (`from-spawn.jpg`,
`meadow.jpg`, above).

Left: the haze band (above; noted, not decided); a ford's road dips to the water at its grade (up to 120 cells each
side), so a road crossing a deep reach has long ramps; the meadow's lake is crossed by a lesser road on a causeway, a
land unit over the water; on the flat meadow the lake's shore is ragged where the land is within a little of its level (the terrain's coarse chunks); rivers end where the map's land puts their end lake (here by the spawn's main road, kept 34
cells clear of it); no bridges, no switchbacks; the river is static water (it doesn't flow or carry anything).
