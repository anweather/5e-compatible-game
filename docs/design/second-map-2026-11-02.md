# A second map: how much of the world is data? (2026-11-02)

The code behind this test stays on the `second-map` branch (afff4c9), not on dev: `SPELL_WORLD_MAP`, `world/maps/` and `MapId` below exist only there.

Andrew: "Create another map that isn't the valley and let's see how bad it is." This is the
experiment's result, ugly parts included. Nothing here was refactored; branch `second-map`, not
for merging unless the PM says so.

## The answer

**The map's layout is data, the land is not.** A theme file really can place a whole new world,
and the core doesn't care: chemistry, chunks, lockstep and the late joiner all worked on the new
land first time. But everything between the theme and the cells is Editor code that bakes one
scene for one map. The game can't load a map; a build *is* a map.

| Question | Answer |
|---|---|
| 1. Can a game start on it? | **Yes, from a rebake.** `SPELL_WORLD_MAP=drowned-chain` set on the Editor, then Build Chemistry Yard, then the normal start. Not a choice at runtime |
| 2. Can you walk it? | **Yes.** 1.08 km corner to corner in 246 s with no stuck, no fall (`mapwalk`). A straight line doesn't work: the shores are cliffs |
| 3. Fire and water on its land? | **Yes.** Fire Bolt lit 4 cells, 93 after 10 s; Create Water doused it and grew shoots; Fireball too (`mapcast`) |
| 4. Two peers? | **Yes.** `online`: 7/7 checksums, 3/3 after a reset, no desync. `roomthing`: 4/4 with a late joiner. In the Editor; no player build was made |
| Can a mod carry a map today? | **No.** See below |

## The map: The Drowned Chain

`world/maps/drowned-chain/`: a theme, things, one village, no cabins, and a baked `land.json`.
A chain of islands running south-west to north-east across open sea, with one 154 m peak.

**Why this one.** The valley has land everywhere and water as a feature. This turns that inside
out: 69% of the cells are sea (228,261 of 331,776), no lake, no river, no flat floor, and most of
the land is a slope. Every valley assumption about a lake, a river, a coast on one side and a yard
at height 0 gets tested at once, and it shows up the water simulation's cost. A desert would have
shown less: it's the same land with another colour (`looks.json` draws desert as grassland).

## How it was run, in three steps

Each step is what the next one had to fix, so the order is part of the result.

1. **Every data file swapped, no bake** (the valley's scene, the map's folder). No error, `mapview` and `mapcast` green, and the world on screen is still the valley: 42,760 pit cells, the same land. The
   cell map and the terrain live in the scene, so the files moved only the props and the land
   under the water. **This is the worst breakage: it fails silently.**
2. **The theme baked, height swapped, nothing else** (`second-map-theme-only-breakage`). The
   valley's things stay where they were put in metres: the falls hanging in the sky, the tower and
   a river running down a mountain, the sea drawn only south-east of the valley's old coastline,
   a square lake, and the seabed bare, with a forest on it. (That bake also has #6 below in it.)
3. **35 lines saying "not on a map"** gave the map you see in `second-map-drowned-chain-walk`.

## What worked from data alone

- **A theme places a world.** Zones, mountains and looks solved first try into islands (`ThemeBlueprint`,
  `ThemeField`), in a folder laid out like `world/`.
- **`world/*.json`**: things (8/8 spawned, stable ids, in the checksum), a village (Saltwick laid and
  built by `VillageLayout`, roofs and all, its pad levelled), and the missing ones are simply absent:
  no `golf.json`, `gauntlet.json` or `puzzles.json` means no golf, Gauntlet or puzzles, with no error.
- **The core.** Nothing in `Chemistry/` needed changing to run the land (one path, below). Cells,
  chunks, water, fire, the dragon (seen on both peers), weather (rain, storm, wet ground; `sky` fails one, 6 of 32 puddles froze), Shape Earth, the room.
- **Chunks.** 86 switches on the walk, 15 to 35 active. No hole, no fall (never over 0.01 m under the ground).
- **Fauna and water life** read the cells: 34 animals out; carp, trout, dolphins, ducks, reeds and
  lily pads found the sea. Land birds didn't (`spawners`: 0 turkeys, quail, heron, ptarmigan, roadrunners, not looked into).
- **Swimming** worked (205 m on one walk). The open sea's swell is higher than the probe allows (`swim`: head 0.16 m under at times).
- **Sound:** cast sounds played; there's no ambient map of its own. Not listened to.
- **Perf:** no worse than the valley. Editor, 15 s held at the start: 7.9 ms mean against 13.3 ms. Only that was timed, not a walk or a player, so all it says is that 228,000 cells of sea cost little while their chunks are inactive.
- **The world edge** holds, as in the valley (`edge`: only its "on the ground" checks fail, as the wizard is swimming there).
- **No minimap exists,** so nothing to break.

## What broke, ranked by how much it blocks a second map

Sizes: small (hours), a day, days, a week or more.

| # | What | Where | Size, and why |
|---|---|---|---|
| 1 | **The land is a baked Editor asset.** 81 `TerrainData` assets (13 MB) and the cell map (`ChemGrid.map`, 331,776 characters in the scene) are made by Editor code. Nothing at runtime turns data into terrain, paint, trees or grass. One build holds one map | `Editor/ValleyTerrain.cs`, `Editor/ValleyBuilder.cs:WireChemistry`, `ChemGrid.cs:16`, `ValleyChemistry.cs:21` | **Week or more.** A runtime land builder: heights, layers, trees, details, on a loading screen. All of it now leans on `AssetDatabase` |
| 2 | **Heights can't be remade the same way twice.** Two bakes of plain dev code differ: the terrain's hash and the river's `channelDepths` change every run (the cell map doesn't). Heights come from Punchy's float noise, editor-only by D11. `land.json` works only because it's baked once and committed | `Editor/ValleySurroundings.cs`, D11 | **Week or more** to generate per peer: the height noise moves to `WorldNoise`, with goldens. **Small** if a map ships its heights as a file instead |
| 3 | **The theme isn't the map.** It steers only `ValleySurroundings`; heights and cells go through `ValleyShape` and `ValleyCoast`, static classes full of metres. 119 calls to them in `Editor/`. The DSL has no sea: "unpainted is sea" is my rule, 14 lines. Theme id was a const | `Editor/ValleySurroundings.cs:27`, `Editor/ValleyShape.cs` | **Days.** One interface for "the land at a point", valley and generated behind it |
| 4 | **Places are code, not data:** court, bridge, river rocks, tower, Defense field, standing stones, outcrop, falls, coast props, the lake | `Editor/ValleyBuilder.cs:Dress`, `ValleyTower.cs`, `DefenseFieldBuilder.cs:26`, `CoastBuilder.cs` | **A day each, a week or more in all.** Each needs a site in a world file and its code freed from fixed metres. Raiders have no field here; AmbientLife (gulls, crabs, fish) is built by the coast's builder, so it goes too |
| 5 | **No map identity on the wire.** Nothing says which map a peer runs. Approval compares builds only. **`land` isn't in the checksum**: `CellHash` doesn't read it, so two peers on different land agree until water moves | `Net/NetSession.cs:207`, `Net/NetChem.cs:540`, `World.cs:223` | **A day.** A map hash in approval and the checksum. Read from code; a mismatched peer wasn't run |
| 6 | **Height 0 is the yard's corner.** `PlaceTerrains` lifts or drops all the land so the yard's corner reads 0. On a map where that corner stood 38 m up, the whole world dropped 38 m: most of the islands were under the sea, and **a walk went along the seabed with every check green** | `Editor/ValleyTerrain.cs:343` | **Small,** but it took stills to see it |
| 7 | **Spawn is at height 0.1.** Put right, #6 left the wizard under the land: falling at 4.4 km by the end of a probe, with no error, and the probe passed | `Editor/ChemYardBuilder.cs:68` | **Small** |
| 8 | **The world folder is a second truth.** `ModBudgets.Load` and `StandInValley.Load` work out `world/` for themselves, beside `rules/`. The game read the valley's 20 things on the map, and the checksum with them. Fixed for the game; **the rules tools aren't**: `find_site` still offers "the lake's north shore" on a map with no lake, off the valley's land, and `cabin#2` is known and unknown at once | `ModValidator.cs:61`, `Mcp/StandInValley.cs:41`, `Mcp/Workbench.cs:33` | **Small** for the path; **a day** with the chat's tools and words |
| 9 | **Sea, one coast only.** `Ocean` is a half-plane from `shoreOrigin` (a hack here: reach back 1.6 km). The shore map's swell rings the world's edge in hard stripes from the air. Lake level and sea level are one constant, -0.6 m | `Ocean.cs:129`, `ShoreMapBaker.cs:21`, `ValleyShape.cs:16` | **A day** |
| 10 | **Cabins are required.** "No cabins" crashes the bake: `Cabins[2]` picks the bridge's place, `Cabins[0]` is everywhere | `Editor/ValleyShape.cs:455`, `:28` | **Small** |
| 11 | **Size and shape are fixed.** 576 × 576, 9 × 9 chunks, full detail on the middle nine by number. All the map's far islands are coarse terrain. Not tried at any other size; `ValleyChemistry.Bind` only logs an error | `TerrainGrid.cs:110` | **Days** |
| 12 | **The cell legend has no sand, rock or snow.** Beaches are `dirt`, peaks `grass`, since a map is one legend character a cell. `looks.json` draws desert as grassland. Theme `heart` terrains go nowhere | `ChemGrid.cs:110`, `Editor/ValleyBuilder.cs:CellKind` | **A day** |
| 13 | **Probes and showcases are valley-bound:** yard cells, Hollowmere, the crate. `smoke` fails 2 of 28 on this map, for data alone (`levitate_lifted`, `things_spawned`: 2 of 8 props off their cell, probably rolled on a slope). `online` fails 3 of 37: the valley's glow ball and crate, and a remote cast's sound 38.6 m from the ear, not looked into. Not a blocker; it just means green doesn't cover a map | `ProbeScenarios.cs`, `ProbeOnline.cs` | **Days,** when a map is real |
| 14 | **Packaging.** `Pack("world")` copies only the top folder; `ThemeTests` pins the list of themes, so a new file in `world/themes/` fails a test. That's why the theme is in the map's own folder, not `world/themes/` | `Editor/PrototypeBuild.cs:242`, `ThemeTests.cs:30` | **Small** |
| 15 | **The shores are cliffs** (my height rule, not the game's): the land rises tens of metres in a few cells, past the wizard's slope limit. A straight walk stuck 5 times; a route that keeps under 45° exists and hugs the beaches | `ValleySurroundings.MapHeight` | **Small** |

## Can a mod carry a map today?

**No.** Not close: a mod changes rules, the map isn't rules.

**What a mod holds** (`ModPatch.ShapeErrors`): elements, the rule tables, reactions, spells, critters,
sim, combos, the building tables, and a `world` section of exactly two keys: `buildings` (rows) and
`add` (at most 16 things a mod; `rules/budgets.json`). A look may name a place in `where`.

**What it can't hold:** land, the cell map, terrain, villages, places (read-only), a theme, a seed,
or anything drawn in the scene. `world/` isn't read from a mod at all.

**What peers would have to agree on,** beyond what lockstep checks now: the same cells, `land`, things,
villages and buildings, and the same *drawn* ground under the cells, since the wizard's height comes
from the terrain. Only cells and things are checked. Land isn't (#5), and the drawn ground never was.

**A late joiner today** builds the world from its own scene (`ChemGrid.StartWorld`, `grid.map`), then
replays the casts and rule switches since the start. Mods reach it as patch text in that history
(`SendRoomHistoryRpc`). So a mod that replaced the map would arrive *after* the joiner has built the
wrong world, and history would have to start with "this is the map". The size is untested: `land.json`
is 104 KB, against a mod's few KB.

**Shortest path,** in order:

1. **Maps as content, not mods** (days). `world/maps/<id>/`, a map picked at the host's start, its id
   and a hash in approval and the checksum, land in the checksum. Every map baked and shipped in the
   build, one scene each. Backlog 28's mode then just names a map. 13 MB a map. No mod carries land; a
   mode that names a shipped map works. **This is what I'd do first.**
2. **A map in a mod** (a week or more on top). The mod carries the theme, `land`, the cell map as a
   file, and the host sends it before the world starts; needs #1 and #2, the runtime land builder.

Backlog 28's "M, more if the land has to move into mods" is fair: it's M with step 1, and well past L with step 2.

## Every code line changed

94 added, 36 removed, in 12 files; a further 248-line probe file. 35 of the added lines name
`ChemGrid.MapId`, and that count is the finding: each is a place where the valley is baked in. All are
off with no flag.

| File | Lines | What |
|---|---|---|
| `ChemGrid.cs` | 8 | The flag: `MapId` from `SPELL_WORLD_MAP`; `GameFolders.World` points at `world/maps/<id>` (the branch predates it: there it was `ChemGrid`'s `FindWorldDir`) |
| `ChemGrid.Rules.cs`, `ModValidator.cs` | 6 | The core's budgets take the world folder (#8) |
| `Editor/ValleySurroundings.cs` | 17 | Theme id; the mask is always land; `seaMask`, `MapHeight` |
| `Editor/ValleyShape.cs` | 30 | Skip bridge and path; off: lake, river, channel, court, tower, falls; beach and sand |
| `Editor/ValleyCoast.cs` | 4 | The coast is nowhere (`Out`, `InIsland`, `IsLand`) |
| `Editor/ValleyBuilder.cs` | 42 | Skip the lake, coast props and places (moved to `BuildValleyPlaces`), painted cells, the yard, the river's current; beach trees and cells |
| `Editor/CoastBuilder.cs` | 6 | `BuildSea` public; sea mesh reaches over the world |
| `Editor/DefenseFieldBuilder.cs` | 4 | Off |
| `Editor/ValleyTerrain.cs` | 2 | Height 0 is sea level (#6) |
| `Editor/ChemYardBuilder.cs` | 1 | Spawn on the ground (#7) |
| `ProbeScenarios.cs`, `ProbeSecondMap.cs` | 6 + 248 | `mapview`, `mapwalk`, `mapcast` |

**The valley didn't change.** Goldens and all tests pass unchanged. A bake of this branch against a
bake of `origin/dev`, no flag: same `grid.map` (`0a4c8f5213`), same river cells, same shore map. The
terrain's bytes and `channelDepths` differ, but they differ between two dev bakes too (#2).

## What wasn't tested

- A player build: all of it ran in the Editor, the second peer a virtual player. `Pack` would need
  work first (#14). Real windows, real input, a real controller.
- Peers on different maps (#5), from code only. Maps of another size (#11).
- `land.json` is from the bake before the spawn fix. Bakes differ slightly (#2), so it's near that
  terrain rather than exact, which also means a fresh bake needs a fresh `land.json`.
- `fire`, `earth` and the probes outside this list weren't run on the map. Villagers: the probe looks
  for Hollowmere by name, so whether Saltwick's folk appear is unknown.
- Perf on a walk or in a player. The real save guard, Claude and the chat: nothing here ran them.

## What it cost

About 1 h 45 m of wall time. Bakes are quick: 64 to 80 s. Real prompts: 0. Tokens: can't be seen from inside a run.

## To run it

```sh
SPELL_WORLD_MAP=drowned-chain Unity -batchmode -projectPath unity   # no value is the valley
# Build Chemistry Yard, then, with no land.json, tools/probe.sh mapview bakes it
tools/probe.sh mapcast --notes 300,325
```

Rebuild without the flag to get the valley back: the yard scene and `Generated/` hold whichever was baked last.
