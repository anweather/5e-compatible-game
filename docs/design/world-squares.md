# The nine squares: curated landmarks, generated land (plan for Andrew, 2026-11-08)

Read against dev at `9e5fb4a9`. Nothing here is built. It builds on `maps-as-data.md` and `world-nine-options.md`.

## The idea in one paragraph

The world is a 3 × 3 grid of valley-sized squares, 576 × 576 cells (9 × 9 chunks) each, 1728 × 1728 cells in all. A
**square template** names what is curated: its landmarks as intents, where they may go, its elevation band and the
interface each side offers its neighbour. Everything else is left undefined. A core tool solves the nine squares
so their edges match, lays the land, routes the roads, rivers, ranges and canyons through the matched edge points,
places each landmark by its rules and fills in plants and things. It writes the result into a map folder as plain
data. Unity draws that folder with our terrain textures and looks, as it draws the valley now.

"Square" is a spell shape and a village square in `GLOSSARY.md`. This needs a word added there first: **world square**
(the code already says `squareCells`).

## 1. How it fits what exists

- **Maps as data.** One map folder, `world/maps/nine/`, with `size: [1728, 1728]` in `map.json`. It needs:
  - maps phase 6 (the scene lets go) and 7 (sea from `land`), so nothing in the scene assumes the valley;
  - nine-options phase 1: `MapCheck.Size` and `TerrainGrid.Valley()` read the size from `map.json`;
  - maps phase 8 (the host picks a map), so the nine map loads beside the valley.
- **BiomeField: both.** The solve picks a template per square; the template names the anchor biome (or a short list).
  That fills `biomes.json`'s `layout`. `BiomeField` then draws the borders and the variety inside, as now. The field
  is soft and crosses square edges; that's right for looks. Hard features (rivers, roads, ranges, canyons) don't come
  from the field. They come from the edge solve, so they line up exactly.
- **Landmarks are made of what we have.** Each materialization is rows we already read:
  - `stamps.json` map stamps (dome, cone, ridge, canyon, cliff, with paths): a volcano's cone, a lake's hollow, a crag;
  - `villages.json` (a recipe and a seed), `buildings.json` rows (docks, a stronghold, a lake house);
  - `things.json` props and items, appended per chunk;
  - a `places.json` entry, so `places()` and `here()` find it.
  - Missing: a great tree. No row makes one yet. A stand-in first (a scaled tree and a ring of roots); the face and
    the mouth are art (decision 5).
- **The theme engine (D11) is a cousin.** `ThemeBlueprint` already turns goals ("mountains, north, far, large") into
  geometry. Square templates are the same kind of thing at a coarser scale. Reuse its noisy ellipses and river curves
  where they fit; don't build a second solver for the inside of a square.
- **Lockstep (D4) and the map hash.** The generator runs as a tool, not on each peer. It writes the folder; the map
  hash covers every file; approval already refuses a peer without it. Integer maths and `WorldHash` anyway, so the
  same seed gives the same folder on the Mac, the PC and u2 (a golden checks it). Peers never run the solver.
- **The view.** `GroundBuilder` draws a map with no `ground/` from `land` and cells (`GroundFromLand`). The biome
  weights paint the looks (nine-options phase 7). Nothing new in the view except building chunks near the camera,
  not all 729 at once.

## 2. The data shapes

A **square template**, `world/squares/<id>.json`:

```json
{ "id": "lakeland-1", "band": "low", "biomes": ["temperate", "foggy-marsh"], "weight": 1,
  "landmarks": [ { "intent": "village-with-landmark", "where": { "inner": 0.6, "near": "water" } },
                 { "intent": "ruin", "where": { "band": "low", "awayFrom": "village-with-landmark", "min": 120 } } ],
  "edges": { "n": { "band": "low",  "offers": [ { "kind": "river", "may": true, "flow": "in" } ] },
             "e": { "band": "low",  "offers": [ { "kind": "road", "must": true } ] },
             "s": { "band": "low",  "offers": [] },
             "w": { "band": "hills","offers": [ { "kind": "range", "may": true } ] } },
  "ends": { "river": "lake", "range": "fade" },
  "rotate": true }
```

- `where` gives rules, never a cell: how far in from the edge, which band, near what, how far from what.
- `ends` says how a feature may stop here: a river in a lake, a range fading out, a canyon closing.
- `rotate` lets the solver turn the template a quarter (its edges turn with it). Four squares from one file.

A **landmark intent**, `world/landmarks/<id>.json`, with one materialization per biome:

```json
{ "id": "village-with-landmark", "footprint": 96,
  "by": {
    "temperate": { "name": "a forest village by a great tree",
                   "stamps": [ { "profile": "dome", "radius": 30, "rise": 16 } ],
                   "village": { "houses": [6, 10], "stoneInTen": 1 },
                   "things": [ { "kind": "great-tree", "at": "centre" } ] },
    "mountains": { "name": "a village at a volcano's foot",
                   "stamps": [ { "profile": "cone", "radius": 70, "rise": 480, "at": [0, -60] },
                               { "profile": "dome", "radius": 12, "rise": -60, "at": [0, -60] } ],
                   "village": { "houses": [5, 8], "stoneInTen": 8 }, "look": "ashplains" },
    "foggy-marsh": { "name": "a fishing village on the lake",
                   "stamps": [ { "profile": "dome", "radius": 40, "rise": -40, "at": [0, -50] } ],
                   "village": { "houses": [5, 9] }, "buildings": [ { "style": "plain", "parts": ["dock"] } ] },
    "*": { "use": "temperate" } } }
```

Offsets are cells from the site. The place's name comes from a seeded name list. The intent says "a village with a
landmark", never where it goes.

An **edge interface** is one feature at one slot along a side. A side has 9 slots, one per chunk:

```json
{ "side": "e", "slot": 4, "kind": "river", "width": 6, "flow": "out" }
{ "side": "n", "slot": 2, "kind": "range", "width": 2, "height": 300 }
{ "side": "n", "slot": 6, "kind": "canyon", "width": 18, "depth": 200 }
```

Two sides match when the slots are equal, the kinds are equal, widths agree within one class, and a river leaving
one square enters the next. A side also has a band, so the land meets at the same height.

The **elevation bands**, in `world/bands.json`:

```json
{ "bands": [ { "id": "sea", "land": -40 }, { "id": "low", "land": 20 }, { "id": "hills", "land": 120 },
             { "id": "high", "land": 300 }, { "id": "peaks", "land": 700 } ],
  "touch": "neighbours", "rivers": "downhill or level" }
```

Squares that share a side differ by at most one band. Sea never touches high or peaks; foothills sit between.

## 3. The generator, step by step

1. **Pin the valley.** The south-west square is the valley, as it is. Its edges aren't declared: the tool reads them
   from its `land.json` at the borders (the band at each slot, where water leaves, where a road could leave).
2. **Bands.** A small solve over eight free squares, five values each. Sides differ by at most one. Templates can
   require a band. If a square has no band left, it gets **hills** (the foothills fill) and the solve goes on.
3. **Templates.** Wave-function style. Each square starts with every template (and its rotations) that fits its band
   and its anchor biome. Pick the square with the fewest options left, roll a seeded weighted choice (a template not
   used yet in this world counts more), and remove the neighbours' options that can't match its edges.
4. **Edges.** Each `must` offer needs a partner slot on the neighbour; each `may` offer is kept only if the neighbour
   takes it or the feature has an `ends` here. A dead end means backtrack: undo the last choice, take the next.
   With eight squares and 20 templates × 4 turns, full backtracking is a few thousand steps, well under a second.
5. **Repair, so it never fails.** A **filler** template exists for every band: plain land that takes any interface
   and ends any feature. After 10,000 steps the hardest square becomes a filler. A filler in a world is logged.
6. **Base land.** Each band's height, blended across sides over 300 cells (the field's shape blend), plus the
   biome relief (nine-options phase 3). The valley stays exactly as it is and blends out over 60 cells.
7. **Ranges and canyons.** A path through the matched edge slots, wandering by seeded noise. A range is ridge map
   stamps along it, its height from the band; a canyon is one canyon stamp with a `path` (that exists today).
8. **Rivers.** Route from the in slot to the out slot or to the lake that ends it, least-cost on the land, and never
   uphill. Carve the channel, lay `river` cells. The drainage port (nine-options phase 4) then adds small streams.
   Rivers need the whole map at once, so this step is always baked.
9. **Landmarks.** For each intent: seeded candidate sites on a jittered grid inside the square, scored by its
   `where` rules on the shaped land, the best one taken. Its materialization is picked by the biome at that site.
   If no site passes, relax the rules once, then fail `MapCheck` by name.
10. **Roads.** Least-cost paths, graded, joining the landmarks to each other and to the road slots on the edges. A
    road meeting a river makes a ford (no bridge row is data yet).
11. **Plants and things.** Trees and props from the biome rows (nine-options phase 5), seeded per spot, appended per
    chunk so ids stay stable. Then `places.json` regenerates and `MapCheck` runs.

**Baked into the folder:** the plan (`world.json`: seed, picks, bands, edges), `land.json`, `cells.txt`, `stamps.json`,
`villages.json`, `buildings.json`, `places.json`, `things.json`. Rivers, roads and landmarks must be. Things must be
too, since saves name them by id.

**Could be made on demand:** the biome weights for paint (3.2 ms a chunk), grass and small trees, rocks on faces.
They are pure functions of seed and cell and decide nothing shared. That answers J-079's open question for this
design: bake what is shared, make what is only drawn.

## 4. Variety without stamping

- Eight free squares a world. "No template twice in a world" needs at least 8 per band mix in play. With five bands
  and the valley pinned low, about **16-20 templates** covers most solves; 12 is enough to start, with rotation.
- Rotation gives four squares per file. Mirroring would double it, but sunlight and river flow make some look wrong;
  leave mirroring off.
- **6-8 landmark intents** with 3 materializations each are 18-24 distinct landmarks before placement differs.
- Per instance, seeded: the landmark's site, the village's seed, the place's name, the wander of every path.
- From nine-options: rare features (one roll per region: a hotspot, a salt lake, a lone grove), a nudge to each
  biome's relief per region, border blends that vary along a border.
- What makes it feel unstamped: a template says little (one or two intents, a band, edges); the land between is the
  field's. Two copies of "lakeland" in two worlds share a story, not a shape.

## 5. Phases

| # | Phase | What Andrew sees | Size | Needs | Where |
|---|---|---|---|---|---|
| 1 | **First slice: one intent, three ways.** `LandmarkIntent` and its materializer in the core; a top-down picture of each (land shaded, cells coloured) | Three PNGs: the forest village by a stand-in great tree, the volcano village, the fishing village | S | Nothing | u2 |
| 1b | **The slice in the valley.** On a branch never merged: the three written into the valley's `stamps.json`, `villages.json` and `buildings.json` at three open sites, yard rebuilt | Three 30 s clips walking up to each | S | 1 | PC |
| 2 | **Templates and the solve.** Bands, templates, edges, backtracking, fillers; 12 templates; a picture of the 3 × 3 per seed | Ten seeds as ten diagrams, edges and bands marked | M | 1 | u2 |
| 3 | **Land for the nine.** Base land from bands and biomes, valley pinned, the folder written | Height pictures of the 1728² land for those seeds | M | 2; nine-options 3 | u2 |
| 4 | **Cross-square features.** Ranges, canyons, rivers through matched slots; roads | Pictures with rivers and roads crossing squares | L | 3; drainage port | u2 |
| 5 | **Landmarks and things.** Every square's intents placed, trees and props, places, `MapCheck` | A full plan picture and a folder that passes `MapCheck` | M | 4; nine-options 5 | u2 |
| 6 | **Play it.** Size from `map.json`, the nine map loads, chunks built near the camera, biomes painted | Walking from the valley into a second square; a two-peer run | L | maps 6-8; nine-options 1 and 7 | PC (Mac second) |
| 7 | **More of it.** Templates and intents to 20 and 8, rare features | New worlds that don't repeat | M, ongoing | 5 | u2; Andrew writes templates with Claude |

Phases 1-5 are core only and run on u2 while the PC does maps 6-8. The PC only waits at 1b and 6.

## 6. Risks

- **Start-up and memory.** Nine times the cells: about 0.5 GB of `Cell` objects (estimate) and 729 chunks of terrain.
  On the 32 GB PC beside an Editor that's tight. Baking keeps start-up to reading files; terrains only near the
  camera. If cells are too much, `Cell` becomes arrays (D9). Measure in phase 6 with a flat 1728² map first.
- **Lockstep.** The solver runs once, as a tool; peers read files. The danger is only the drawn on-demand parts
  disagreeing, which decides nothing. A golden on the plan and `land.json` hash for two seeds on u2 and the Mac.
- **The Hold and the valley.** The castle (`hold#1` at [112, 352]) is in the pinned valley, so nothing moves. A
  desert Hold becomes a landmark intent later, but `hold.json` holds one castle: a second Hold is its own job.
- **Saves and thing ids.** Ids are chunk plus place in the list. Cells grow south in y, so a world with the valley
  at the south-west and its origin at the north-west moves every valley chunk by 18 rows, and every id and save with
  it. Either chunks north of the valley get negative rows, or the valley moves once with a save migration (decision
  2). A save also names the map hash: a regenerated world is a new map.
- **Where the solver can fail.** No band fits (fillers fix it). A river forced uphill (bands forbid it; reroute or
  end in a lake). A landmark with no site (relax, then fail loudly). A road across a canyon (detour; no bridge yet).
  Four squares meeting at a corner: sides match but the corner can pinch; the 300-cell blend smooths it, and a check
  on corner heights catches the rest.
- **Content.** The great tree, a volcano's lava and smoke, docks that read as a fishing village: stand-ins first.

## 7. Decisions for Andrew

1. **One map or nine?** (a) One folder, 1728², one hash. (b) Nine folders stitched at load. **Recommend (a):** one
   `MapCheck`, one hash, rivers and roads cross without seams in the data.
2. **Where north goes.** (a) Squares north of the valley get negative chunk rows, so the valley keeps every id and
   save. (b) The valley moves 18 chunk rows once, with a save migration, as in October's (3, 4) move. **Recommend (a)**
   if `ChunkCoord` takes negatives cheaply (to check in phase 3); else (b) once, before anyone has a nine save.
3. **One world or a world per game?** (a) One curated world, baked and committed, the same for everyone. (b) A seed
   per new game, the host bakes at start and peers fetch it. (c) A small library of baked worlds the host picks
   from. **Recommend (a) now, (c) next;** (b) waits until phase 6 measures the bake's time.
4. **How much may repeat?** (a) No template twice per world, rotation allowed. (b) Repeats allowed, weighted down.
   **Recommend (a)** with fillers as the only exception.
5. **The great tree's face.** (a) A stand-in tree now, the face later as art. (b) Model it first. **Recommend (a):**
   the slice proves the intent, not the art.
