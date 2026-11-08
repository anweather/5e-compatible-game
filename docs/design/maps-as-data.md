# Maps as data, with terrain through the MCP (2026-11-04)

Design only; nothing here is built. It follows `second-map-2026-11-02.md` (#69), cleanup item 6 and `world-acts.md`. Sizes
and timings marked *estimate* weren't measured; no Unity job was run for this.

## The answer

A map becomes a folder, `world/maps/<id>/`, and the game builds the terrains from it at the start, so a build holds every
map and the scene holds none. Land moves in one way: a **land stamp**, an integer change to `land`. `act` applies one live
for everyone, and keeping a map folds the stamps into its folder. The view needs no new path, because a stamp reaches the
drawn ground through the code that already eases a dug cell. The -6 to +8 range doesn't change at all: mountains belong in
`land`, not in a cell's height.

## #69 against dev (ceff6b4d)

Still true: 81 chunks in `Generated/Valley` (13 MB), the cell map in `ChemGrid.map`, approval on build only
(`NetSession.cs:207`), `land` in no checksum (`NetChem.cs:511`, `Others` has rules, things, sky, defense, casts), the
yard's corner as height 0 (`ValleyTerrain.cs:343`), spawn at 0.1 (`ChemYardBuilder.cs:68`), `Cabins[2]` (now
`ValleyShape.cs:445`), `Pack("world")` top folder only, 117 lines calling `ValleyShape`/`ValleyCoast` in `Editor/`.

Changed: `ModValidator` and `StandInValley` now share `GameFolders.World`, so finding 8 is one line to fix, not three. `act`,
`undo_act` and `ActText` exist. `TerrainGrid` is in the core. The valley's `land.json` is 152 KB. Add to the costs: the
scene is 5.5 MB and the shore map 4 MB, and the shore map is per map too.

## 1. The land as data

```
world/maps/<id>/
  map.json       id, name, size, sea level, spawn cell, what it expects (section 5), which places it dresses
  cells.txt      the cell map, out of the scene
  land.json      LandFile, as now: integers per cell, what the core reads
  ground/        optional, view only: drawn heights, paint, trees, details per chunk (the ground pack)
  things.json, villages.json, places.json, buildings.json, looks.json ...    as in world/ now; absent means none
```

**Is "ship a height file" right?** Yes for repeatability, but it's less than #69 implies. `land.json` holds one integer per
cell and only the water reads it. The drawn valley has 4 × 4 samples a cell on the middle nine chunks, plus paint, trees and
grass, and that's where most of the 13 MB is. So there are two levels:

- **A map with no `ground/`** is drawn from `land` and the cells alone: heights interpolated from `land`, paint from
  `CellPaint`, trees and grass scattered from a seed by the cell's terrain. This is the Drowned Chain, and every map shaped
  through the MCP. No bake at all.
- **The valley has a `ground/`:** one last bake, exported once and committed (exported by the bake's last job, Prototype > Export Valley Ground, retired with it in phase 6: 1.6 MB in all, within 0.064 m of `land` on every cell, in plain git). Heights are 899 thousand samples (9 × 257²,
  72 × 65²), 1.8 MB raw at 16 bits. The riverbed, the yard's gully and the soft paint stay exactly as they are.

Both fill the same arrays, and one builder turns arrays into `TerrainData`. `land.json` stays what the core reads and stays
byte for byte, so **no golden moves**. A test holds the two together: `ground/` at each cell centre within one unit of `land`.

**What becomes of the bake.** The 81 assets go, and `ChemGrid.map` has (phase 1). The exporter is the bake's last job, after which
`ValleyTerrain` and the height half of `ValleyShape` can retire, and D11's float noise stops mattering. That shrinks cleanup
item 6 to the place builders: no `MapPlan` through the land code. The source of the valley's land becomes `ground/`, so it's
committed, unlike `Generated/`. Prototypes (terrain layers, trees, grass) stay assets, on one template `TerrainData`.

**Loading.** `ChemGrid.BuildWorld` reads the chosen folder; `ValleyGround` gets its chunks from the builder, not the scene.
`ValleyGround.Bind` already makes a runtime copy of all 81 terrains, so the builder replaces that copy instead of adding to
it. `Recover` then restores from the arrays. Start-up: *estimate* about level with now, to be measured on the PC before the
scene loses anything (phase 5). The limit: no more than 2 s added on a Windows player, or the pack stays and the builder
waits. Build size: *estimate* down about 8 MB for the valley; a new map without `ground/` is about 200 KB against 13 MB baked.

**The switch (phase 5).** `GroundBuilder` makes the chunks when `GroundSwitch` says so, off until the PC has judged it: `-ground-builder on`
in a player (`land` draws a map's land alone even with a `ground/`), `unity/Temp/ground-builder` in the open Editor, `GROUND_BUILDER`
on either. The template is the scene's first chunk until phase 6, which needs a real asset for it. A map's drawable range is `drawable`
in `map.json`, `[low, high]` in land units, the valley's when left out: every peer's map has it, so a stamp is refused alike.
`MapCheck` holds a range a map says to its land. The valley says none, since its range is where stamps stop and its sea bed lies below that.
Measured on the Mac, in a development player: `Bind` 45 ms baked, 225 ms built, plus 21 ms to read the pack; a map's land takes 2 s to work out.

## 2. Identity

**Map id and hash.** The hash is over every file in the folder, sorted by path, `ground/` included, since peers should draw
the same ground even though it decides nothing.

- **Approval:** the client's `ConnectionData` gains the id:hash of each map it has, beside `BuildVersion`. `Approve` refuses
  through the existing `response.Reason`: "The host is on The Drowned Chain, and this copy doesn't have it" or "...has a
  different The Drowned Chain". That's the screen a build mismatch already shows.
- **Which map:** a `NetworkVariable` on `NetChem`, read in `OnNetworkSpawn` before `grid.ResetWorld()`, which already rebuilds
  the joiner's world. So a late joiner builds the right world and then replays history, and no map goes through history.
- **Checksum:** `LandHash` joins `Others`, "" until land is stamped, then the hash of the stamps so far, as `ActText` does.
  `CellHash` isn't touched, so no golden moves, and the two peers on different land that #69 feared are refused at the door.
- **Lockstep:** stamps are commands, integer maths, the world's order. Physics decides nothing. The drawn ground stays a
  view of `land`; the wizard's height comes from it, which it already does (D4).

## 3. Terrain through the MCP

**One primitive.** A **land stamp** is a row: centre, radius, a profile (flat, dome, cone, ridge along a direction), rise in
land units, roughness, seed. A mountain is a large rough dome, or several; a valley is a negative one (J-055: no mountain
tool). Profiles are integer maths with `WorldNoise`, so a row of about 100 bytes on the
wire expands alike on every peer.

**Canyons and cliffs (2026-11-04).** Two more profiles, with a **face**: a wall that wanders along a line, a cell wide for every
6 m of it, in bands of rock. `rise` is the wall's height. A canyon cuts down at most 14 m and raises the rest as a rim, which
is how it gets walls past the -17 m floor, and its floor climbs to the land at both ends so nobody is trapped. `rough` is 40
when left out, on four octaves of integer noise, so a plain ask isn't a smooth slab. Land has one height a point, so there's
no true overhang: `LandRocks` sets boulders into the faces and jutting at the lips, only drawn, worked out by each peer from
the land. Land a stamp makes steep paints as rock, and `TerrainRock.shader` maps the rock layer from the sides at two sizes,
so a wall doesn't smear and a mountain doesn't show the tile. Known gaps: forest trees stay on a face, and a canyon cut
beside a lake leaves the lake's surface ending in a straight edge above the floor.

**Paths and the map's own stamps (2026-11-07).** A canyon's or cliff's line can be a **path**, points from its middle as a
tenth field of the row: the face follows it round its bends, `radius` either side, the high side of a cliff on the same hand
all the way, and its ends taper (and a canyon's floor climbs out) over three tenths of `radius` past the first and last
points. That's how a canyon gets a dog-leg without stamps summing where they meet. A map shapes itself past its `land.json`
with **map stamps**, `stamps.json` (`MapStamps`): laid as the world is made, after the rows and off their laid land and off
water, the same on every peer and in the map hash. They're stamped land, so they paint rock, grow `LandRocks` and draw as
any stamp does, but no undo or dragon's reset takes them, `LandHash` stays the acts' alone, and a save keeps them as
`mapStamps`. A tree on a crag's face or a deep floor they make goes to grass (`World.IsStampedRock`), and the yard's bake
leaves it out, so the forest stands on the tops. A floor a stamp cut a crag deep paints as rock where it would be grass, so
a canyon's floor is stone, and a canyon's `trail` (a terrain, dirt) is laid along the middle of its floor, where it wanders off its line, and stays earth. The trail is laid with the stamp, not in `cells.txt`, which is data since phase 6 (the yard's build no longer writes it). The valley's one: the canyon beside the river north of
the Hold. (Its castle cliff was a stamp too, a rock wall beside the castle, until castle-cliff-wing, 2026-11-07, made it the
castle's own pad: see the-hold.md.) Since phase 6 the bake is gone, so the valley's `ground/` keeps the stamps it was exported with: a change to the valley's
stamps leaves its drawn ground behind until something writes `ground/` again (`keep_land`, phase 9).

**Live:** `act` gains `what: "land"` with the row, through `RoomChange.Act` and the room's change mode. `World.Acts.cs` keeps
it. **`undo_act`** subtracts the same integers, so unlike laid ground it always works in any order; a stamp that would leave
the terrain's range is refused, never clamped, or undo would break. The save keeps stamps (as `ActText`), and a late joiner
replays them. The dragon's reset takes land back to the start, as it does cells (`KeepStart` needs `land`).

**To keep:** a tool `keep_land` folds the stamps into the map's `land.json`, and into `ground/`'s heights if it has
one. It's a tool for making maps, not for a player: refused unless the rules MCP runs
on a repo folder. A kept map changes the map's hash, so it's a new map to peers, which is right.

**The drawn land and the cells stay one thing.** `CellHeightfield.GroundOffset` is `(elevation + height) × step`. It gains a
third term, `(land now - land at the start) × unit`, from an array that's null until the first stamp. The stamp calls
`Reshape` on its cells and everything after is the existing path: easing, rectangles, `SetHeightsDelayLOD`, one collider sync
a frame, repaint. No baked base moves, so `Recover` and a reset work as they do.

**Water.** `land` is already what water runs on, so a dug basin fills and a raised bank turns the flow, with the world's
chunks freshened round the stamp. `SetLand` overwrites water bodies with `NoLand`, so the raw integers must be kept. First
version: a stamp that touches a lake, river or sea cell is refused. An island rising from the sea means draining a body of
water, which is its own job.

**Cost per edit.** Wire 100 bytes; core about r² integers. View, full detail: 16 samples a cell, so radius 16 is 16,000
samples and radius 64 about 262,000, four whole chunks, eased every frame for as long as it moves. Collider syncs are one
chunk a frame, so a big stamp lags its drawn ground by a few frames. The worst reshaping frame on record is 31 ms (Editor,
2026-10-26, a wildfire), and big stamps have never been measured. So `budgets.json` gets `land.maxRadius`, 32 to start, to
raise once measured on the PC. Not known: whether baked tree instances follow `SetHeightsDelayLOD`, and how resting props take
ground moving under them (`ValleyGround`'s header warns of it). A stamp over a building or a village pad is refused.

**The height range.** Leave it. `BlockAt` is 3: a cell at 1.5 m is a wall, so raising `max` makes taller walls, not hills
(`high-walls.json` does exactly that). Hills go in `land`, whose integers have no limit that matters. The real limit is
`TerrainData`: `MaxHeight` is 300 m at 15 bits, about 1 cm a step, and the Drowned Chain's 154 m peak fits. `map.json` can
state a taller one. What's missing is the core's view of hills: only water and golf read `land`, so raiders walk over a
mountain and sight and spells pass through it. It's true of the valley's hills today, and a mountain makes it obvious
(question 2).

**The water level.** Half of it falls out. `lakeLevel` (-0.6 m, in the view) and `generate.water.below` (-4 units, in
`places.json`) become one sea level in `map.json`. Changing it live doesn't: it flips cells between water and land and moves
the sea's mesh and shore map. Stays later, as in `world-acts.md`.

## 4. Places

| Place | For a second map |
|---|---|
| Sea | **Data.** `Ocean` is a half-plane; it needs the level from `map.json` and a mask from `land`. The shore map's bake moves to a runtime step or into `ground/` |
| Spawn | **Data.** A cell in `map.json`, height from `land`, no more 0.1 |
| Lake, river | **Already data** once `places.json` is made per map from its `land`. `channelDepths` goes in the valley's `ground/` |
| AmbientLife (gulls, crabs, fish) | **Freed** from the coast builder: it's built with the coast, so a map with a sea loses it. Its own root, reading the cells |
| Villages, things, golf, puzzles, Gauntlet | **Already data**; absent means none |
| Cabins | **Optional:** `Cabins[0]`, `[2]` stop being required |
| Court, bridge, river rocks, tower, Defense field, stones, outcrop, falls, coast props | **Left out.** Each stays code, baked into its own root in the scene; `map.json` lists the roots it wants, and only the valley does. *Built (phase 6): `dresses` in `map.json` names them (`lake`, `rocks`, `river-rocks`, `court`, `landmarks`, `falls`, `bridge`, `tower`, `defense-field`, `coast`, `skyline`), and a `PlaceRoot` not named switches itself off as the scene starts.* Making one data is a job for the day a second map wants one |

The Defense field absent means no raiders' field, as golf absent means no golf. `ProbeScenarios` needs the same manners.

**`find_site`.** `GameFolders.World` takes the map, so `places.json`, `land.json` and `StandInValley` all read the map's folder
and a lake that isn't there can't be offered. `PlacesFileIsUpToDate` runs per map. `things.json`, `cabin#2` and the budgets
follow from the same line.

## 5. Failing loudly

| Silent failure | What catches it |
|---|---|
| Data swapped, no bake, the valley on screen | Gone: nothing is left in the scene to disagree. Every probe's result starts with the map id and hash |
| The world dropped 38 m | Gone with the yard-corner rule: heights are absolute, from the sea level. And `map.json` says `expects: { landShare, highest, lowest }`, checked against `land` by a core test and at load |
| A spawn that falls 4.4 km | The core test: spawn is on land, above the sea level. Every probe gains `fell`: under the ground by 2 m for 1 s fails the run, whatever else passed |
| Peers on different land | Approval refuses; `LandHash` catches a stamp that went astray |
| Drawn ground and `land` disagree | At `Bind`: 64 seeded cells compared, over one unit is an error that fails a probe. The `ground/` test does all of them |
| A place, thing or village off its land | The map check: each place's cells match what it claims, a lake is water, a village is above the sea. Size other than 576 refused by name; `ValleyChemistry.Bind` only logs now |

One check, `MapCheck`, in the core, run by a test over every folder in `world/maps/` and at load, as `Rules.Check` is for rules.

## Order of work

Each phase lands alone, with the valley unchanged and dev playable. No golden moves in any; a phase that finds it must, says so.

| # | Phase | Size | Where | Collides with |
|---|---|---|---|---|
| 1 | **Folder.** `world/` to `world/maps/valley/`, `map.json`, `GameFolders`, `cells.txt`, recursive `Pack`, `MapCheck`, `find_site` | 2-3 days | Mac | Anything in `world/`: one quick merge, announced |
| 2 | **Identity.** Hash, approval, `LandHash`, a mismatched peer really run | 2 days | Mac; PC for one two-player run | `NetSession` only |
| 3 | **Land stamps live.** The row, `act`, `undo_act`, third term, `budgets`, save, reset | 3-4 days | Mac; PC for the cost | **Backlog 30**: `World.Acts.cs`, `Workbench.Acts.cs`, `RulesTools.cs` |
| 4 | **Export.** The valley's `ground/`, its tests, the `fell` and `Bind` checks | 2 days | Mac (one bake; the PC's is no better) | None |
| 5 | **Builder.** Arrays to `TerrainData`, behind a switch beside the baked chunks; look compared, start-up measured | 4-5 days | Mac to code; **PC GPU to judge** | `host-ux` if it's in `ValleyGround` |
| 6 | **The scene lets go.** Terrains out (`ChemGrid.map` went in phase 1), place roots, `ValleyTerrain` retired. *Done 2026-11-08 (PC): `MapTerrains` makes the chunks at load, `EditorGround` for the builders, the build writes no `cells.txt`; each place that stays code has a `PlaceRoot`, and `map.json`'s `dresses` names the ones a map wants* | 3 days | Mac, Editor | **Any Editor builder work**: one owner of `ChemYardBuilder`, `ValleyBuilder` |
| 7 | **Sea and life.** `Ocean` from `land`, shore map, level from `map.json`, AmbientLife freed | 3-4 days | **PC GPU** | `host-ux` looks |
| 8 | **The test.** Host picks a map; the Drowned Chain's folder from `second-map`, no `ground/`; a player build, two peers; probes that mean it | 3 days | **PC** | Backlog 28 names a map after this |
| 9 | **Keeping.** `keep_land`, and a hill shaped by asking Claude then played on | 2 days | Mac | Backlog 30, lightly |

About five weeks of one worker; 3 can start after 1 and runs beside 4 to 7, so Andrew can shape land live weeks before a
second map loads. The Drowned Chain is the test of 8, not a feature: what breaks there goes back to the tools. Backlog 30's
"real mountains" and part of "the water level" close with 3 and 7. Land 3 after named acts, or it fights them.

**Not closed off for a map in a mod:** the folder's layout, a hash per folder, the builder reading paths rather than Unity
assets, and the map chosen before the world starts. Not designed further.

## Decided (Andrew, 2026-11-04, J-073)

1. Land is built at run time.
2. A mountain doesn't stop raiders, sight or spells yet.
3. A live stamp is at most radius 32 cells (48 m), one at a time.
4. Sea level is set per map for now: no stamps across water, no live water level.
5. The host picks the map, with the valley as the default.
6. The dragon's reset undoes stamps, and the automatic dragon is off (`dragon.budget` 0), so it comes only when summoned.
