# Builders: creatures and houses from parts

Split out of `primitives.md` on 2026-11-02, unchanged in substance, so that file stays on material
primitives. Andrew, 2026-11-01: "all bears now have 6 legs. Or all deer are now centuars with a human
top half", and J-058's houses, seahorses and gaits. J-055, J-057, J-058, J-059: no named content in
code; creatures and buildings are data, moved by rule. Builds on `world-tools.md` (G1 to G6),
`RULES_MCP.md` and `primitives.md` (P1 to P5, temporary materials).

## 1. Creatures today

A species is a Punchy `CreatureSpecies` Editor asset (59): body, palette, coats, size, `VariantOf`, limb
slots (by index, socket, direction, mirror, hand-set phase), gait and behaviour numbers; a `LimbType` (76) is
a role, tapered segments and seven hand key poses. `ChemYardBuilder`'s table (13 species) is baked into the
scene; `ValleyFauna` builds bodies at run time, drawn as instanced capsules (up to 120). A mod reaches none of
it. Six-legged bears already work in the Editor; centaurs work as a shape but wear deer coat (one palette per
creature: gap C2, a palette per slot). J-058: data, Punchy's style, our code, moved by rule.

## 2. A data format for creatures

Two rules tables, out of `Rules.Fingerprint` like looks, each replaced whole by a mod's entry as every
table is (`ModPatch`): **`rules/parts.json`**, the shapes (today's limb types), and **`rules/fauna.json`**,
the species. Parts are keyed by id within a species, so a row reads and patches by name, not index, and
`on` names the part a part hangs from. Today's deer as a row:

```json
"deer": {
  "name": "Deer", "size": [0.9, 1.1],
  "body": { "length": 1.15, "radius": 0.2, "taper": 0.95, "flatten": 1.15 },
  "coat": { "base": "#8c5e38", "accent": "#ebdecc" }, "coats": [],
  "parts": {
    "hind": { "part": "hoof-hindleg", "at": [0.55, -0.35, -0.75], "dir": [90, 0, 0], "mirror": true },
    "fore": { "part": "hoof-foreleg", "at": [0.55, -0.45, 0.72], "dir": [90, 0, 0], "mirror": true },
    "neck": { "part": "deer-neck", "at": [0, 0.5, 0.85], "dir": [-45, 0, 0], "scale": 0.85 },
    "head": { "part": "deer-head", "on": "neck", "dir": [55, 0, 0] },
    "ears": { "part": "deer-ear", "on": "head", "at": [0.045, 0.05, -0.2], "dir": [-70, 40, 0], "mirror": true },
    "eyes": { "part": "eye", "on": "head", "at": [0.05, 0.025, -0.13], "mirror": true },
    "tail": { "part": "stub-tail", "at": [0, 0.55, -1], "dir": [-40, 180, 0] }
  },
  "move": { "habitats": ["walks"], "gaits": ["walk", "trot", "gallop"], "walk": 1.3, "run": 9, "turn": 110 },
  "behave": { "wander": 30, "idle": [3, 10], "flee": 18, "group": [2, 5], "active": "day", "graze": 0.6,
              "thirst": 0.4, "play": 0.15, "laze": 0.3, "hunts": [], "hunger": 0.3, "chase": 10 },
  "spawn": { "density": 0.7 }
}
```

A part row (`"hoof-foreleg"`): `role` (leg, arm, tail, neck, head, ear, horn, eye, fin, wing, feature),
`segments` (length, radii, flatten, `colour`: base, accent, a fixed hex, or another palette, and glow),
`bend` (each joint's range in degrees, so a hock bends back and a knee forward), `spring`, `ripple` and
`follows` (ground). A slot may carry `colours: "<species>"` (C2) and `scale`/`thickness`; a variant is
`{ "variantOf": "black-bear", "scale": 1.3, "coat": {...} }`. **Habitats** are `walks` (feet on the ground),
`swims` (under the surface, at a depth range), `floats` (on it), `leaps` (an arc out of the water) and
`glides` (a held spread through the air after a leap). Spawning picks cells where one holds, reading
depth through `ICreatureGround`, as the port's `punchy-water` brings. The Mac's ranges for size, coat,
skin and hair (`life-variety`) are fields of `coat` and `size`, drawn per animal from its own seed.

**The export, once.** An Editor menu writes every species and limb asset out as rows, `ChemYardBuilder`'s
table as `spawn` and `Hunts` as ids: same numbers, so no art changes. An Editor test checks that each
body built from its asset equals the one built from its row; then the assets and the table go. The
`LimbPose` keys go too: the export reads `Idle` as the rest direction and the walk and run keys as each
joint's `bend` range (§3). **The Editor still** makes the creature material and shader and puts
`ValleyFauna` in the scene, nothing per species. **At run time** `ValleyFauna` reads `World.Rules` at start
and at every rule switch, builds a `CreatureBody` per species (plain arrays; segments are instances of one
capsule, no meshes) and swaps it into that species' living animals, keeping their places and seeds. A
live change costs one build per changed species (tens of microseconds) and the next frame's instances.

**Landed as (`creature-data`, 2026-11-02).** There are 58 species, not 59 (the count took in the `Limbs`
folder), and 80 limbs. Until gaits go by rule (§3) rows keep what the art still needs: a part's seven key
`poses`, and a slot's `phase` and `mirrorPhase`. `habitats` and `gaits` come with the work that reads them.
The export writes colours as `[r, g, b]`, as hex would round them; the reader takes hex too. Only what
spawns counts as a hunter when a species asks if it's hunted, as the old table had it. The assets, the export and the old
builder went once `punchy-water` had been through the export: rows are the source now, edited by hand, by mod or by the MCP.

## 3. Gaits by rule

Today each limb blends three walk and three run keys by a hand-set phase, then IK puts the foot on the
ground. Instead, a few rules in **`rules/gaits.json`**, view only; the keys become rest and `bend` (§2):
- **Which legs step when.** Legs pair up left and right, and the pairs are ordered front to back by
  socket. A gait is `alternate` (the phase between the two legs of a pair), `lag` (between one pair and
  the next), `duty` (the share of a cycle a foot is down) and `lift`. A leg's phase is `pair × lag + side ×
  alternate`, so phases need no authoring: walk (`lag` 0.25, `alternate` 0.5, `duty` 0.65), trot
  (0.5, 0.5, 0.5), gallop or bound (0.5, 0.1, 0.35, with rock), biped (one pair, 0.5), and with three
  pairs at `lag` 0.5 the tripod (front-left, middle-right, back-left together). `wave` (`lag` 1/pairs) is
  the spider's and centipede's ripple.
- **Stride from speed and leg length.** Hip height `h` is the legs' rest length. A creature changes gait
  at a Froude number `v²/(g·h)` (walk to trot near 0.5, trot to gallop near 2.5), and stride is about
  `2.3 · Fr^0.3 · h` (Alexander's relation for running animals). Cycle rate is speed over stride, capped
  so feet don't reach past the pair in front.
- **Feet by kinematics.** In stance a foot stays planted where it touched down, so feet don't slide.
  In swing it arcs (`lift` × leg length) to a target half a stride ahead of the hip along the velocity,
  on the ground (`ICreatureGround`). Two-bone IK (today's) bends thigh and shin within `bend`; feet and
  hooves keep to the ground's slope. The body rides at the planted feet's mean height, pitched and rolled
  to their plane, bobbing with the duty cycle.
- **Arms, tails, necks.** An arm swings against the leg on its side, more with speed. On a quadruped
  it counters the front pair, and it hangs at rest. Tails, ears and manes keep today's spring and ripple. A
  neck counters the body's pitch so the head stays level, then turns to what it looks at, as now.
- **No feet down.** `slither`: a wave travels down a body that follows the ground. `swim`: the same wave
  sideways in water, with fins fluttering at a rate from speed. `paddle`: the walk gait with feet stroking
  in a plane under the surface instead of on ground. `leap`: a ballistic arc from speed. `glide`: wings or
  fins held spread until the arc ends.

**Walk-throughs.** A **horse** is a mod's row (none is in the base rules): a deer's parts scaled up, thicker
legs, a mane; it walks, trots and gallops from the Froude thresholds. The **six-legged bear** adds a `mid`
pair and gets the tripod from its pair count. The **centaur** is four hoofed legs on the horse's gait, a
torso slot held to world up against the bob, arms countering the front pair. **Horses in the water** add
`"swims"` to `habitats`: they paddle in deep water and walk out. The **seahorse** is a new row: upright body,
`curl-tail`, a snout, a fluttering dorsal `fin`, `swims` only. The **flying fish** (a `punchy-water` fish row
with two pectoral `wing`s, `["swims", "leaps", "glides"]`) leaps from speed, glides, and splashes back.

**Cost.** A target, an arc and a two-bone solve per leg, about 1 µs in managed code: 10 µs for a creature
of ten parts, 1.2 ms for a crowd of 120 at worst (about today's blend and IK), near 0.4 ms with animals
past 40 m at half rate and no IK. Gate: `life-variety`'s crowd numbers, main p50 up no more than 0.3 ms.
**What looks wrong first:** feet skating at gait changes and tight turns (blend over a cycle); hoofed knees
bending the wrong way (the `bend` ranges); a six-legged bear's middle legs clipping at long strides (the
spacing cap); a centaur's torso bobbing; small fast animals like clockwork (per-animal jitter on `lag`).

## 4. A house as data: wall segments between join points

**Today** a village building (`Village.cs`, D12) is a rectangle of cells that are all wall, with one
`WallKind`, `RoofKind` and `RoofCover`, drawn by `BuildingMesher` from fixed palettes. J-059: outside
first. A building becomes parts, in `world` rows a mod adds (G5) or edits by id (G3):
- **Join points** sit on the half-cell lattice (0.75 m; a cell is 1.5 m), as integers in half cells from
  the row's `at` cell. What a join is follows from what meets there: one wall is an **end** (capped), two
  in line a splice, two at an angle a **corner**, three a **T**, four a **cross**.
- **Who trims whom**, one rule for every shape: at a join, a pair of walls in line runs through; then the
  thicker, then the taller, then the earlier id. Equal walls at a corner mitre; otherwise the first runs
  to the other's outer face and the rest stop at its face. No code per shape.
- **A wall segment** runs between two joins, along an axis or at 45° (bays, cut corners), in a `storey`,
  with a `height` (m), a `material` (a terrain id, so it burns, breaks or shatters as that terrain does),
  a `finish` (a `wall` look: pattern and colour, G2) and `openings` (`kind`, `at` in half cells from
  `from`). A row's `wall` gives the defaults.
- **Standard lengths** are whole and half cells. **Openings** (`rules/buildings.json`) are rows: a view
  width and height, sill and head, and what their cells become. `door` 1 cell (0.9 m, a `door` terrain
  that burns or breaks to a gap), `double-door` 2 (1.8 m), `window` 1 (`pane`), `wide-window` 2,
  `garage` 2 (2.7 m), `double-garage` 3 (4.9 m), `arch` (any width, no cell: open). An opening covers the
  cells whose centres lie within its span, half a cell clear of a join.
- **Floor**: `slab` or `plinth` and its height (view). **Roofs**: each over an outline of joins (not
  necessarily the walls', so a roof can cover a porch), `shape` `gable` (with `gables`: the walls that end
  in one), `hip` (the outline's straight skeleton, so any outline works), `shed` (rising from a `low` wall)
  or `flat` (with a `parapet` height), `pitch` as rise over run (`[4, 12]`), `overhang` (m) and `cover` (a
  roof terrain with its burn rules and look). Two roofs that meet are cut where they cross.
- **Details** are parts on a wall or a join, from the mesher's families (like look families): `viga`,
  `canale`, `chimney`, `porch`, `beam`, `shutters`, `planter`. A row picks them and their numbers; a new
  family is mesher code, proposed only when several styles want it. A **style** is a row of defaults
  (material, finish, heights, roof, details) plus a recipe that lays walls from the seed when a row has none.

**Two layers, one format.** The core rasterises parts to cells; the view draws the true geometry.
(a) **Outside only** (recommended). A wall on a cell-edge line takes the cells on its right from `from`
to `to` (outer walls run clockwise); one through cell centres takes those. The outside is flood-filled
from the row's border through open openings; what's left is `fill`: `solid` (the wall's terrain, today's block: villages keep it, so cells and goldens
hold) or `hollow` (a floor terrain behind closed doors: a burnt-out house leaves a ruin to walk into). The
view draws a 0.3 m wall at the cells' outer edge, so a wizard meets it where it's drawn. `Cell`, fire, gas,
water, pathing and the tick are unchanged. (b) **Walls a cell thick, with rooms:** inner walls rasterise
too, doors are gaps; no core change, but 15 × 12 m is 10 × 8 cells, 8 × 6 inside, and each wall a 1.5 m
band behind a 0.3 m drawing. (c) **Walls on cell edges**, a core primitive: north and west edge fields a cell
(a material id and its state, about 4 bytes, 0.6 MB over 150 000 cells); every neighbour step in flow, gas,
fire, conduct, push, raider pathing and line of sight checks the edge it crosses (5 to 10% on those passes);
hashed and saved only when set, so goldens and saves hold; the view's colliders already follow the geometry.
Only the rasteriser changes between them, never the format.

**The test: a single-family stucco house of the American Southwest** with an attached two-car garage,
15 × 12 m plus 6 × 6 m, a low-pitch tile roof and a recessed arched entry. A `southwest` style, the
`clay-tile` terrain comes in the same mod as a row, and `stucco-wall` (blocking, breaks to rubble, burns slower
than a log wall, J-063) is in the base rules. Joins are in half cells, x east and y south, the front facing south:

```json
{ "id": "lakeside#1", "at": [300, 150], "style": "southwest", "seed": 41, "storeys": 1, "fill": "hollow",
  "floor": { "kind": "slab", "height": 0.15 },
  "wall": { "material": "stucco-wall", "height": 2.9, "thickness": 0.3, "finish": { "pattern": "smooth", "color": "#d6b48a" } },
  "joins": { "a": [8, 0], "b": [28, 0], "c": [28, 16], "d": [18, 16], "e": [18, 14], "f": [14, 14],
             "g": [14, 16], "h": [8, 16], "i": [0, 16], "j": [0, 8], "k": [8, 8] },
  "walls": {
    "back":    { "from": "a", "to": "b", "openings": [{ "kind": "wide-window", "at": 4 }, { "kind": "window", "at": 14 }] },
    "east":    { "from": "b", "to": "c", "openings": [{ "kind": "window", "at": 5 }, { "kind": "window", "at": 11 }] },
    "front-e": { "from": "c", "to": "d", "openings": [{ "kind": "wide-window", "at": 3 }] },
    "recess-e": { "from": "d", "to": "e" }, "recess-w": { "from": "f", "to": "g" },
    "entry":   { "from": "e", "to": "f", "openings": [{ "kind": "door", "at": 1, "finish": { "color": "#3a6f9a" } }] },
    "arch":    { "from": "g", "to": "d", "openings": [{ "kind": "arch", "at": 0, "width": 3, "shape": "round" }] },
    "front-w": { "from": "g", "to": "h", "openings": [{ "kind": "window", "at": 2 }] },
    "party":   { "from": "h", "to": "k", "openings": [{ "kind": "door", "at": 3 }] },
    "west":    { "from": "k", "to": "a", "openings": [{ "kind": "window", "at": 3 }] },
    "garage":  { "from": "h", "to": "i", "openings": [{ "kind": "double-garage", "at": 1 }] },
    "g-west":  { "from": "i", "to": "j" }, "g-back": { "from": "j", "to": "k" } },
  "roofs": { "main": { "over": ["a", "b", "c", "i", "j", "k"], "shape": "hip", "pitch": [4, 12],
                       "overhang": 0.45, "cover": "clay-tile" } },
  "parts": [{ "kind": "chimney", "on": "east", "at": 8 }] }
```

The **flat-parapet variant** changes only the roof and details: `"shape": "flat", "parapet": 0.6`, and
`parts` gains `{ "kind": "viga", "on": "front-e", "every": 0.6 }` and `{ "kind": "canale", "on": "back", "at": 6 }`.

| What Claude would supply | Maps |
|---|---|
| 15 × 12 m house, 6 × 6 m garage, attached on one side | Exact: 10 × 8 and 4 × 4 cells |
| One storey, 2.7 to 3 m walls, slab on grade; low hip roof 4:12, 0.45 m eaves | Exact, view (cells have no height) |
| Stucco over block, earth colour, smooth; walls 0.3 m (0.45 adobe) | Material exact as a terrain; look exact; the core's band is a cell |
| Red barrel tile | Cover terrain and colour exact; the barrel pattern needs a `tile` roof pattern |
| Recessed entry 3 m wide, 1.2 to 1.8 m deep, round arch | Width exact; depth rounded to 1 cell; arch shape view |
| Front door 0.9 m, blue; garage door 4.9 × 2.1 m; windows and sills | View exact; cells 1, 3 and one pane each (two in one cell merge) |
| Facing the street; parapet, vigas, canales | Rounded to four directions; parts exact, view |
| Three bedrooms, two baths, kitchen, living room | Lost under (a); (b) or (c) later |
| Floor area, lot, setbacks, driveway, xeriscape | Lost (the site is `find_site`'s cells); a garden wall is a wall segment |

**The edits.** "Add a wing": new joins and three walls on a side, a T where they meet an old wall (its
openings there go), the roof's `over` grown; cells change. "Add a floor": `storeys` 2 and `storey: 1` walls
(the outer walls below, by default), roof raised; no cells change. "Make it a shack": the same outline in
timber with a `worn` finish, a `shed` roof at `[2, 12]`, fewer openings and no parts, or `style: shack`
with `walls` dropped so the recipe lays a 3 × 4 cell box from the seed; cells change either way.

**Through the room.** Joins, walls, openings and `fill` are shared: a G3 edit or G5 add, validated by
every peer, in the fingerprint as G5's things are. At the switch tick the core rasterises old and new (a
pure integer function with a digest, as `VillageTests` pins plans), lays changed cells fresh (as the dragon
freshens: no fire, cover, gas or water) and returns removed walls to ground. The rest is view only. The
checker refuses a site over a thing, a building, water or non-ground; a player or raider on a cell turning
to wall is pushed to the nearest free cell in entity-id order. A chunk not held gets the edit when built; a
revert lays the base again; a golden pins "a wing added". **Limits** (`budgets.json`): 24 × 24 cells, 64
joins, walls and openings, 32 parts, 3 storeys, 4 buildings a mod.

**Parts for a stronghold (the Hold, `the-hold.md` phase 1).** All rows, nothing named: a mod builds another within the budgets.
A row's `level: true` stands its walls from the highest ground under it and runs them down to the lowest, so their tops are one height; openings
stand on the ground beneath them, so a gate meets its road. `towers` (`at` a join, `radius` in half cells, `height`, `roof: cone`) take the
cells in that radius and draw a drum a wall walk passes through. `roads` (`through` joins, `width`, `terrain`) take cells past the walls. The details
`battlements` and `stair` draw a parapet and a way up the inner side: the view walks them (a ramp and a deck in a `Walk` collider), and the core has no
idea of them. The opening `timber-gate` is cells a hand swings (`materials.json` `swing`, `EffectKind.Swing`). Two rows of walls side by side make a wall
walk two cells wide. A row in a map's `buildings.json` with walls and no `built` is **laid** over `cells.txt` at the start
(`WorldBuildings.LayInto`), which is how a tree under it goes without `cells.txt` or `things.json` changing: the view hides the baked tree and repaints the cell.

**Places.** `places()` has `lake` and `sea` but no shores: a place reference takes a `side` and `edge:
"shore"`, so "the lake's north shore" and "the beach" need no names (or hand-write `north-shore` in
`places.json`). A read-only `find_site(size, near)` gives buildable cells; the mod carries the cells.

## 5. Through the MCP, and what stays safe

A species, part, gait, style or building is a row: `describe` it, change it, put it back whole (`add_species`,
`add_building`). **View only, out of the fingerprint:** `fauna.json`, `parts.json`, `gaits.json`, and a
building's heights, finishes, roofs and details. **Shared:** what rasterises to cells (§4). A raider is one
cell, its numbers in `entities.json`; its look names a species (G4), so six legs on a bone hound is a look
kept within its cell. The core never reads the view tables; the checker refuses a view row naming a core field.

## 6. Order of work, and the tests

1. **The data move** (M each, J-059: houses next, creatures alongside). Houses: building rows (§4), the
   rasteriser for (a), the mesher drawing segments, joins and roofs from rows, `Village.cs` and the cabins
   writing rows (`fill: solid`, so goldens hold), building ids (G1). Creatures: the export, the view building
   from rows, best the Mac's port's first branch (it owns `CreatureSpecies.cs`, `CreatureBody.cs`, `ValleyFauna`).
2. **Houses through the room** (M): G3 edits and G5 adds, `find_site`, shores. 3. **Gaits by rule** (M).
4. **C2**, a palette per slot (S).

**The tests**, each asked of Claude, each ending as one mod with no code naming its content: the Southwest
stucco house (§4) on the lake's north shore, then "add a wing", "make it a shack"; six-legged bears;
centaurs; seahorses both ways; flying fish.

## Questions for Andrew

1. Does the Mac do the data move as its port's first branch? *Recommend: yes, before `punchy-people`.*
2. Fauna mods per room or per player? *Recommend: per room, view only (J-056).*
3. A house edit over a player: push them out or refuse it? *Recommend: push them out, logged.*
4. New houses hollow (a burnt one leaves a ruin to walk into) or solid like today? *Recommend: hollow.*

## Proposed ADRs (titles only)

- Creatures are data: species and parts as rules rows, built by the view at run time
- Gaits by rule, not by pose
- Buildings are wall segments between join points, rasterised to cells through the room
