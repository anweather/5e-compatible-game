# Primitives: the world from a few general parts

Andrew, 2026-11-01: "I don't wanna hard code the world. I wanna find some primitives that can be
modified to creat convincing effects. Maybe creating new temporary materials from the primitves of
the game?" And: "all bears now have 6 legs. Or all deer are now centuars with a human top half."
And J-058's houses, seahorses and gaits. J-055, J-057: no named content in code; a new thing is a new
combination of primitives, as data. Design only. Builds on `world-tools.md` (G1 to G6) and `RULES_MCP.md`.

## 1. What the primitives are today

**Terrain** (`rules/materials.json` "terrain", `TerrainDef`): `tags` (`blocking`, `ground`, `low`,
`soil`, `turf`, `trellis`, `roof`, `vessel`, `machine`, `seeThrough`, `fuses`, `muddy`...; reactions
match on them), `elevation` (height steps), `speed` (walking), `spring` (a water source level),
`burn` (catch, fuel, heat, leaves, fire look, status), `dig` (hardness, leaves), `break`, `uproot`,
`soak` (dry and wet ticks, leaves, dries, dampens fire), `scour` (wears, leaves), `grow` (into, after
ticks), `bake`, `crust` (into, for), `thaw` (into, water), `freeze`, `boil`, `hot` (for, quench),
`run` (spreads onto lower cells every N ticks), `quench` (into, when water meets it), `rust`
(every, leaves), `shatter` (leaves, cracks), `dissolve` (by, leaves), `conduct` (chains wet),
`lift` (an updraft), and creatures' `burn`/`freeze`/`soak` statuses. **Covers**: `tags`
(`slippery`, `climbable`, `spiky`, `snow`), `burn` (intensity), `melt` (into, water), `lasts` and a
wither line, `float` (every, reach: drifts on water), `dissolve`, `rot`, `scour`, `damage`, `speed`.
**Gases**: `decay`, `scald`, `blocksSightAt`, `hidesAfter`, `condenseEvery`/`Away`, `rainEvery`,
`rusts`, `sheds` (an element every N ticks), `burn.flash`. **All three**: a `look` (a family and
parameters: colour, height, glow, form), out of the fingerprint. **Reactions** (`reactions.json`):
element × conditions (tags, cover, gas, water, fire, frozen, hot, power, chance) → ordered effects.

**Hard coded by name or special case** (the honest list):
- **Water, above all.** A cell holds one `int Water` and a `bool Frozen`: the only liquid. Flow,
  springs, soak, damp, rain, deep water and breath, covers floating, the `water` and `frozen`
  conditions, `addWater`, boiling and freezing are all water's. `WaterTo` compares `"ice"` and
  `"steam"`; boiling always makes `steam` (`AddGas(c, "steam")` in Sim, React and Weather);
  `cold-steam` and `condense` know steam by id.
- **Fire.** `Fire`/`Fuel` are fire's own fields; `Apply` special-cases the element `"fire"` (fire
  generation, gas flash); burning leaves the `ash` cover and `smoke` by name; spread odds are global
  (`sim.json` "fire"), not per material.
- **Ice** is water's `Frozen` flag plus the `ice` terrain (`World.IceMaterial`) and `cracked`.
- **Lava** is nearly data (`boil`, `hot`, `crust`, `run`, `quench`, `grow`), but it runs as terrain
  copying itself downhill, not as a liquid that pools, and only water quenches it.
- **Weather**: `snow`/`snowbank` covers and the `meteor` spell are constants; the kinds of weather
  are a closed list; `sheds == "lightning"` is a special case.

## 2. Effects as a test

| Effect | Today | What stops it |
|---|---|---|
| Paint | One combination: a cover with `float`, a `look` colour, a spell that lays it | Slick covers aren't drawn from data (`looks.json` `needs`). Mixing is one reaction row per pair |
| Tar or honey | Half: a cover with `speed` 0.3 and `burn` slows and burns | It doesn't flow downhill or pool: only water moves. Needs a liquid |
| Lava | Mostly one combination today | Runs as terrain, so it doesn't level out or fill a pit; `boil` always makes steam; only water quenches it |
| Quicksand | Half: a terrain with `speed` and `muddy` | Sinking and breath read `Water` depth only. Needs a liquid that doesn't flow and drowns |
| A slick of ice | One combination: a cover like `frost` (`slippery`, `lasts`, `melt`) | Drawing it (the same slick-cover gap) |
| Glowing moss | Half: a cover like `vines` with a colour | Covers don't spread on their own (`grow` is terrain's); a cover's look takes no glow |
| Acid rain | One combination: a gas that `sheds` the `acid` element, laid by a spell or a mod | Not as weather (the kinds are a closed list); proposed only if wanted |

## 3. The smallest set of new primitives, each for a family

- **P1. Liquids.** "A cell holds water" becomes "a cell holds a liquid": `Water` stays the amount,
  and a new `Liquid` is a small integer kind (0 = water, so every save and golden reads as today).
  A `liquids` table gives each kind `thick` (flows on one tick in N; 0 never: quicksand), `hot` (heats
  and lights what it touches: lava), `boils` and `freezes` (into a gas, a terrain or a cover, replacing
  `"steam"`/`"ice"`), `drowns` (counts for breath), `floats` (covers ride it), `soaks` and a `look`.
  Two kinds never share a cell: a liquid flows only into dry cells or its own kind, and where two meet a
  reaction row decides (lava meets water: rock and steam). Family: water, tar, honey, lava, mud
  water, acid pools, quicksand, paint as a liquid, melted wax.
- **P2. Covers that spread.** `spread: { every, chance, onto: tags, notOnto }`: a cover creeps to a
  neighbouring cell from the world's seeded RNG. Family: moss, mould, slime, creeping frost, rust
  bloom, fairy rings.
- **P3. Products by data.** `burn.ash`, `burn.smoke`, `boil.gas`, `quench.by` (a liquid kind), with
  defaults equal to today's ids, replacing the literals in §1. Same results, so the goldens hold;
  it's the cheap reshaping J-057 asks for.
- **P4. Glow on cover and liquid looks** (view only): moss, embers, paint, lava's light.
- **P5. Mix.** Covers or liquids in one `mix` group make a derived material when they meet (§4).

**What a liquid costs.** `Cell` is a class of about 20 fields; one more `int` is 4 to 8 bytes a
cell: about 1.2 MB over the 150 000 cells the lag fix walks (`docs/perf/2026-11-01-load.md`).
`Flow` already runs only on wet cells; P1 adds one compare per neighbour, and a thick liquid flows
less often, so it costs less. At rain's 34 000 wet cells, `tick.flow` (up to 30 ms a second) grows
by about 5%: 1.5 ms a second, 0.25 ms a tick, inside the 6 ms a frame ticks may spend. It's integer
state through the accessors (D9 seam 3) and moves with the interning step (BACKLOG §10). **Saves:**
the cell array gains an element written only when non-zero, and old saves load as water. **Cell
hash:** `AddIfSet` on the new field, so it's hashed only when non-zero; every golden (`TerraformTests`,
`ChemistryTests`) is unchanged, and one new golden pins a lava pit and a tar pour. The TS sandbox is
frozen (J-042), so nothing to mirror.

## 4. Temporary materials

A material made at run time from primitives through the MCP, as a new row in a mod's patch
(`add_material` in a draft, or `apply` with patch JSON), switched on in the room at a tick like any
mod (D13): validated by every peer, kept in the save's room, replayed for a late joiner.
- **An id every peer agrees on.** Cells hold string ids today, so a mod's row is `<mod>/<name>` and
  can't collide with base ids. Once interning lands, integers are given at each switch from the
  effective rules in a fixed order (base tables in file order, then each live mod's new rows in
  switch order). Every peer switches the same mods at the same tick, so they agree. Integers never
  reach a save, which keeps strings.
- **How it lapses.** A row must give `lapses: { into, after? }`, `into` a base id. After `after`
  ticks, or when the mod is reverted, the material retires: the room keeps a retired list (id →
  into), active chunks are rewritten at that tick, and an inactive chunk on becoming active (the
  catch-up path), so no cell is ever left holding an id with no row. **A late join** gets the
  snapshot and the retired list with the room. **A save** stores the live mods, the retired list and
  the cells' strings: loading it back restores both.
- **Limits** in `rules/budgets.json` (base rules only, so a mod can't raise them): at most 8 new
  materials a mod and 32 live in a room; derived mixes at most 16; every row only primitives the
  checker knows; `simulate`'s slowdown gate as today. A stray mod can't fill the table or stall the
  tick.
- **Mix.** Rows in one `mix` group (say `paint`) that meet make `a+b` (ids sorted), interned when it
  first appears. Ticks run in a fixed order, so every peer derives the same id at the same moment. It
  takes the mean of the two rows' numbers (rounded down), the tags they share, and the mean of their
  looks' colours, and lapses with its parents. Past the cap, the cell keeps what it had. Red and blue
  make purple with no row per pair.

## 5. Creatures today

A species is a Punchy `CreatureSpecies` Editor asset (59 in `ThirdParty/Punchy/Fauna/`): body, palette and
coats, size, `VariantOf`, `Parts` (limb slots by index, socket, direction, mirror, hand-set phase), gait and
behaviour numbers. A `LimbType` (76 in `Limbs/`) is a role, tapered segments and seven hand key poses.
`ChemYardBuilder`'s table (13 species, a density each) is baked into the scene; `ValleyFauna` builds each
body at run time and draws segments as instanced capsules (up to 120 animals). A mod reaches none of it.
Six-legged bears already work in the Editor (one more slot; gait and foot IK take any number of legs).
Centaurs work as a shape but come out in deer coat: one palette per creature, and a human wears `Base` as
clothes and `Accent` as skin (gap C2, a palette per slot). J-058: creatures and buildings are data, in
Punchy's style with our code, moved by rule. The Mac's port (`punchy-life.md`) lands in §6's format.

## 6. A data format for creatures

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
joint's `bend` range (§7). **The Editor still** makes the creature material and shader and puts
`ValleyFauna` in the scene, nothing per species. **At run time** `ValleyFauna` reads `World.Rules` at start
and at every rule switch, builds a `CreatureBody` per species (plain arrays; segments are instances of one
capsule, no meshes) and swaps it into that species' living animals, keeping their places and seeds. A
live change costs one build per changed species (tens of microseconds) and the next frame's instances.

## 7. Gaits by rule

Today each limb blends three walk and three run keys by a hand-set phase, then IK puts the foot on the
ground. Instead, a few rules in **`rules/gaits.json`**, view only; the keys become rest and `bend` (§6):
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
legs, a mane feature. It walks, trots and gallops from the Froude thresholds with no clip. The **six-legged
bear** is the bear's row with a `mid` pair, and it gets the tripod from its pair count. The **centaur** is
four hoofed legs on the horse's gait, with a torso slot held to world up (an upright part on a quadruped
springs back against the bob), and arms countering the front pair. **Horses in the water** is the horse row
with `"habitats": ["walks", "swims"]`: it paddles where the water is deep and walks out, free. The **seahorse**
is a new row: an upright body, a curled tail (`curl-tail`), a snout, a fluttering dorsal fin (role `fin`),
`swims` only. The **flying fish** is a fish row from `punchy-water` with two pectoral fins (role `wing`) and
`["swims", "leaps", "glides"]`: it leaps from speed, glides fins out, and splashes back.

**Cost.** A target, an arc and a two-bone solve per leg, about 1 µs in managed code: 10 µs for a creature
of ten parts, 1.2 ms for a crowd of 120 at worst (about today's blend and IK), near 0.4 ms with animals
past 40 m at half rate and no IK. Gate: `life-variety`'s crowd numbers, main p50 up no more than 0.3 ms.
**What looks wrong first:** feet skating at gait changes and tight turns (blend over a cycle); hoofed knees
bending the wrong way (the `bend` ranges); a six-legged bear's middle legs clipping at long strides (the
spacing cap); a centaur's torso bobbing; small fast animals like clockwork (per-animal jitter on `lag`).

## 8. A house as data

**Today.** A village's plan (`Village.cs`, core, D12) gives each building a kind, a footprint of wall
cells, storeys, `WallKind`, `RoofKind`, `RoofCover` and door and window cells; `BuildingMesher` draws one
rectangle with fixed palettes; the cabins are a C# constant built in the Editor. **As data**, a building is a row:

```json
{ "id": "cabin#2", "at": [412, 188], "facing": "south", "style": "cottage", "seed": 77, "storeys": 2,
  "rooms": { "main": { "size": [6, 5] }, "wing": { "size": [4, 4], "on": "main", "side": "east", "at": 1 } },
  "walls": "logwall", "roof": { "shape": "gable", "cover": "shingle", "pitch": 40 },
  "doors": [{ "room": "main", "side": "south", "at": 2 }], "windows": { "every": 2 } }
```

Rooms are rectangles of cells fastened to a side of another; their union's outline is wall cells, a door
cell joins two rooms, and each room has its own roof, so a wing's ridge meets the main one. A **style**
(`rules/buildings.json`, beside **B1**: wall kinds, roof covers and shapes as rows replacing the enums) gives
defaults and a recipe laid from the seed in integer maths: `shack` (one 3×4 room, worn logs, few windows),
`cottage`, `mansion` (3 to 5 rooms, two wings, 2 to 3 storeys, stone, two chimneys). **The edits:** "add a
room" or "a wing" adds a `rooms` entry on a side; "add a floor" is `storeys` + 1; "add a door" appends to
`doors`; "more windows" lowers `every`; "make it a mansion" or "a shack" sets `style` and clears `rooms`
for the recipe to lay again. "Build a house on the North Shore" or "on the beach" is a new row at a place.

**Places.** `places()` and `here()` exist, with `lake` and `sea` from `land.json`, but nothing names a
shore. Rather than naming each, a place reference takes a `side` (north, east... of its centre) and
`edge: "shore"` (dry ground within N cells), so "the lake's north shore" and "the beach" (`sea`'s shore)
need no names; a hand-written `north-shore` in `places.json` does too, if Andrew wants it on the map. A
read-only `find_site(size, near)` gives buildable cells (ground, flat to a height step, no things, off
lanes and courses); the mod carries those **cells**, not the name.

**Shared state.** Walls, windows and door gaps are cells; storeys, roof shape and cover, style and colours
are view only (the roof isn't a cell), so they stay out of the fingerprint like looks. A change to
rooms, doors, windows, walls or a new row is a G3 world edit (one building by id), and a new house is G5's
`add`. It goes through the room like any mod, every peer validating it, and it's in the fingerprint as
G5's effective things are. **At the switch tick** the core works out the building's old and new cells (a
pure integer function, pinned by a digest as `VillageTests` pins plans), lays each changed cell fresh
(the dragon's freshening clears fire, cover, gas and water there) and returns removed walls to ground.
**Anything standing there:** the checker refuses a site over a thing, another building, water or a
non-ground cell (every peer knows them); a player or raider on a cell turning to wall is pushed to the
nearest free cell, in entity-id order, and ambient life steers round. A chunk not held gets the edit
when it's built, as it gets its village's plan. A **revert** lays the base building again the same way.
The cell hash covers the result; one golden pins "a shack becomes a mansion".
**Limits** in `budgets.json`: a building fits 24 × 24 cells, at most 8 rooms and 3 storeys, at most 4
buildings a mod. The mesher's work is drawing several rooms with roofs that meet.

## 9. Through the MCP, and what stays safe

A species, part, gait, style or building is a row: `describe` it, change it, put it back whole (`add_species`
and `add_building` drafts beside `set_look`). **View only, out of the fingerprint:** `fauna.json`,
`parts.json`, `gaits.json`, styles, storeys, roofs and colours; each peer redraws at the switch. **Shared,
in the fingerprint and cell hash:** what's on cells: a building's walls, windows, doors and site, and a wall
kind's terrain (log to brick changes what burns). A raider is one cell, its `hp`, `mass` and speed in
`entities.json`; its look names a species (G4's `creature` family), so six legs on a bone hound is a look
kept within its cell. A raider filling more cells is a core change, not proposed. The core never reads the
view tables, and the checker refuses a view row naming a core field.

## 10. Order of work, and the tests

1. **The data move** (M): the export, the view building from rows, the assets and `ChemYardBuilder`'s
   table gone. Best the Mac's port's first branch (it owns `CreatureSpecies.cs`, `CreatureBody.cs` and
   `ValleyFauna`), so its new species land as rows. With it, buildings as rows: B1, ids (G1), cabins (J-056).
2. **Gaits by rule** (M), with the crowd numbers before and after.
3. **Houses through the room** (M): G3 edits and G5 adds for buildings, `find_site`, shores.
4. **P3** (S) and drawing slick covers: paint, a slick of ice.
5. **Temporary materials** (S to M); **P2** and **P4** (S each): glowing moss.
6. **C2** and **P5** (S each); **P1, liquids** (M to L, with interning).

**The tests**, each asked of Claude, each ending as one mod with no code naming its content: six-legged
bears; centaurs; seahorses (horses that swim, and the animal); flying fish; "add a wing to this house";
"make it a shack"; "build a house on the north shore of the lake"; red and blue paint that runs.

## Questions for Andrew

1. Does the Mac do the data move as its port's first branch? *Recommend: yes, before `punchy-people`.*
2. Liquids (P1, 1.2 MB, 0.25 ms a tick) now, or tar and honey as slow covers first? *Recommend: covers first.*
3. Fauna mods per room or per player? *Recommend: per room, view only (J-056).*
4. A house edit over a player: push them out or refuse it? *Recommend: push them out, logged.*
5. Centaurs that behave like people, or only look like them? *Recommend: looks only for now.*

## Proposed ADRs (titles only)

- A cell holds a liquid kind, not only water
- Temporary materials: mod-made rows with a lapse, ids given at the switch
- Creatures are data: species and parts as rules rows, built by the view at run time
- Gaits by rule, not by pose
- Buildings are rows of rooms, laid on cells through the room
