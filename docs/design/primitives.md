# Primitives: materials from a few general parts

Andrew, 2026-11-01: "I don't wanna hard code the world. I wanna find some primitives that can be
modified to creat convincing effects. Maybe creating new temporary materials from the primitves of
the game?" J-055 and J-057: no named content in code; a new thing is a new combination of
primitives, as data; a new primitive only when a whole family needs it. Design only. Builds on
`world-tools.md` and `RULES_MCP.md` (D13). Creatures and houses as parts are in `builders.md`.

## 1. What the primitives are today

**Terrain** (`materials.json`, grouped by verb, `MaterialGroups.cs`). `tags` (`blocking`, `ground`,
`low`, `soil`, `turf`, `muddy`, `sandy`, `trellis`, `rootstock`, `roof`, `vessel`, `machine`, `gate`,
`seeThrough`, `fuses`): reactions match on them, and the verbs below add `flammable`, `breakable`,
`conductive`, `bakes`, `crusts`, `thaws`, `brittle`, `scalds`. `elevation` (steps) and `spring` (water it
keeps) shape water; `speed` slows walkers. `burn` {catch, fuel, heat, intensity, leaves, status, look}:
catches and spreads fire. `dig` {hardness, leaves}, `break`, `uproot`, `scour` {leaves, wears}: what
earth and sand leave. `soak` {dry, wet, dampens, leaves, dries, status}: how fast thin water sinks in,
how damp dulls burning, mud from dirt. `rust`, `hot` {for, quench}: stays hot after fire. `bake`,
`crust`, `thaw`, `grow` {into}: heat, cold and time turn it into another terrain. `freeze` {} and
`boil` {}: water on it turns to ice or steam. `run` {every}: it moves to a lower cell. `quench`:
water sets it. `shatter` {leaves, cracks}, `dissolve` {by, leaves}, `conduct` {chainsWet}, `lift`.
**Covers**: `tags` (`slippery`, `climbable`, `spiky`, `snow`), `burn`, `melt` {into, water}, `lasts`
(withers), `float` {every, reach} (drifts over open water), `dissolve`, `rot`, `scour`, `damage`,
`speed`. **Gases**: `decay`, `scald`, `blocksSightAt`, `hidesAfter`, `condenseEvery`/`Away`,
`rainEvery`, `rusts`, `sheds` {element, every, chance}, `burn.flash`. **All three** carry a `look`
(family, colour, parameters), view only. **Reactions** match an element on a cell by `terrainTag`,
`cover`, `gas`, `water`, `frozen`, `fire`, `hot`, `melts`, `conductive`, `power`, `chance`, and run
ordered effects (set terrain or cover, add gas or water, ignite, boil, freeze, melt, thaw, bake, crust,
condense, dissolve, shatter, conduct, scour, grow, raise, push). `sim.json` holds the global numbers.

**Hard coded by name or special case** (the honest list):
- **Water, above all.** A cell has one `int Water` and a `bool Frozen`; it is the only liquid.
  `Flow`, `Springs`, `Soak`, `RainFrom`, rain's fill and dry, `Float` (covers ride water only), the
  still-water skip, the `water`/`frozen` conditions, `addWater`, `boil`, `freeze`, `melt`,
  `condense`, slipperiness (`IsSlippery`: frozen water), defence's `deepWater`, the dragon's
  `shelterWater`, breath and the view's water mesh all mean water. Its colour is the view's.
- **Steam, smoke, ash by name.** `AddGas(c, "steam")` in six places (hot metal, `Quench`, boiling
  ground, fire on water, the `boil` effect, rain dousing); `condense` checks `Gas == "steam"`;
  fire's smoke is `"smoke"`, burnt cover is `"ash"`. A new liquid could boil only into steam.
- **Fire.** `Fire`/`Fuel` are its own fields; `Apply` special-cases the element `"fire"` (generation,
  gas flash); spreading and fanning re-apply `"fire"`; spread odds are global (`sim.json` fire).
- **Ice.** `Frozen` plus `waterTo == "ice"` (a string compare in `Soak`) and `IceMaterial = "ice"`
  (`World.Shatter`, raiders), whose `shatter` frozen water borrows.
- **Lava** is nearly data (`boil`, `hot`, `crust`, `run`, `quench`, `grow`), but it is terrain: `run`
  moves the terrain a cell at a time, so it never pools by volume, fills a pit or levels out, and
  only water quenches it (`Cool` reads `Water`).
- **Weather.** `snow`/`snowbank` and the `meteor` spell are constants, `sheds == "lightning"` is a
  special case, and the kinds of weather are a closed list.

## 2. Effects as a test

| Effect | Today | What stops the rest |
|---|---|---|
| **Paint** | One combination: a cover with `float`, a colour, an element and a spell that lay it | Slick covers aren't drawn from data (`looks.json` `needs`); it spreads only on water; a mod may add one element, so two colours are two mods; mixing is a row per pair |
| **Tar, honey** | Half: a cover with `speed` 0.3 and `burn` slows and burns | It can't pour, run downhill or pool: only water moves |
| **Lava** | One combination (the base `lava-pool`) | Runs as terrain, so no pool by volume; only water quenches it; it can only boil water to steam |
| **Quicksand** | Half: terrain with `speed` 0.2 and `muddy` | Nothing sinks: depth is water's alone, and so is breath |
| **A slick of ice** | One combination: a cover like `frost` (`slippery`, `lasts`, `melt`) | Drawn only by id (the same slick gap) |
| **Glowing moss** | Half: a cover like `vines` with a colour and `lasts` | Covers never spread (`grow` is terrain's); no look glows |
| **Acid rain** | One combination: a gas with `decay` and `sheds` acid, laid by a spell | Not as weather (a closed list); the rain itself is water, so no acid puddles |

What fails: what **moves as a liquid**, what **spreads by itself**, **products named in code**, **glow**.

## 3. The smallest set of new primitives

- **P1. Liquids.** "A cell holds water" becomes "a cell holds a liquid": tar, honey, lava, quicksand,
  paint, acid pools, oil that pours, mud floods, melted wax. Below.
- **P2. Covers that spread.** `spread` {every, chance, onto: tags, notOnto, reach} on a cover: on the
  soak pass it may lay itself on a neighbour, from the world's seeded RNG, in N, E, S, W order. Moss,
  mould, slime, creeping frost, rust bloom, fairy rings, a stain that bleeds.
- **P3. Products by data.** `burn.ash` and `burn.smoke` (the cover and gas left), a liquid's `boils`
  and `freezes`, a gas's `condense.into`, `quench.by` (a liquid tag), `sim.json` weather's snow covers
  and meteor spell. Defaults equal today's ids, so every golden holds, and the steam, ash, smoke, ice
  and snow literals go. Family: every new liquid's vapour and solid, sooty ash, a cold that leaves
  rime instead of ice. It is J-057's "reshape special cases where cheap".
- **P4. Glow** (view only, out of the fingerprint): `look.glow` 0 to 1 on any look, drawn emissive and
  lit at night. Moss, embers, lava, luminous paint, fungus, a magic circle.
- **P5. Mix**: rows of one `mix` group that meet make a derived material (§4).

**P1 in detail.** `Water` stays the amount; a new `int Liquid` is the kind, an index into a
`liquids` table where **0 is water**, so an untouched world is exactly today's. A row:
`{ "thick": 4, "heap": 3, "soaks": false, "floats": true, "drowns": true, "hot": { "for": 300 },
"boils": "smoke", "freezes": "pitch", "sets": { "into": "rock", "after": 600 }, "touch": "tar",
"tags": ["sticky"], "speed": 0.3, "look": { "color": "#3a2a10", "glow": 0 } }`.
`thick`: it flows on one tick in N (water 1; 0 never: quicksand). `heap`: the drop it needs to flow on
the flat (water 2), so honey stands in a mound. `soaks`: thin pools sink into the ground. `floats`:
covers ride it. `drowns`: its depth counts for breath and sinking (quicksand, tar pits). `hot`: it
heats and lights what it touches, through today's `Heat` and fire rules. `boils`, `freezes`, `sets`:
P3's products. `touch`: the element it applies to a cell of another kind it would flow into.
**Two kinds never share a cell.** A liquid flows only into a dry cell or its own kind; meeting another,
it applies its `touch` element there at power 1, and the reactions decide: lava touching water hits
the `lava` rows on a wet cell, and a row says rock and steam. No row: the other liquid is a wall.
Reaction conditions gain `liquid` (an id or a tag); `water` keeps meaning kind 0.

**Cost, against the lag fix's numbers** (`docs/perf/2026-11-01-load.md`: 150 000 cells walked,
ticks within 6 ms a frame, `tick.flow` up to 30 ms a second in rain's 34 000 wet cells):
- **Memory.** `Cell` is an object of about 120 bytes (five references, fourteen ints, a bool); one
  more int is 4 bytes, likely none after padding: at most 0.6 MB over 150 000 cells. The still-water
  snapshot adds a `byte[]` a chunk (4 KB), about 0.2 MB. As struct-of-arrays later (D9), a byte a cell.
- **Tick.** `Flow` already runs only on wet cells. P1 adds one load and one compare per neighbour,
  and an array lookup (not a dictionary) for a non-water kind. All-water worlds pay about 5% of
  `tick.flow`: 1.5 ms a second at rain's peak, 0.25 ms a tick. Thick liquids flow one tick in N, so a
  honey lake costs less than water. `Soak`, `Float` and `Cool` read the kind's flags instead of
  literals: no new passes. The view's water mesh takes a colour per kind (a vertex colour).
- **D9.** Seam 2 from the start: an integer id in hot state, the table in the rules, accessors, tags.
- **Saves.** The cell row is a positional array (`World.Save`); the liquid's **id string** is appended
  only when not water, so old saves load as water and a save never depends on an index.
- **Cell hash and goldens.** `AddCell` adds the kind only when non-zero, as it does `Height` and
  `Life`: every `CellHash` and FNV-1a golden in `TerraformTests` and `ChemistryTests` is unchanged.
  New goldens pin a honey pour down a slope and lava running into a pond. The sandbox is frozen.
- **Base lava** stays terrain until a liquid lava mod proves better (question 2).

## 4. Temporary materials

A material made at run time: a row (terrain, cover, gas or liquid) in a mod's patch, written with the
MCP's `add_material`, checked by `validate`, tried by `simulate`, sent by `apply` as a rule switch at
a tick (D13). Every peer validates and composes the same mods in the same order, so all agree.
- **Its integer id.** Today cells hold strings, so step 1 needs none: a mod's ids must not clash with a
  base id or another live mod's. Once ids are interned (BACKLOG §10, and P1's kind from the start),
  the table is rebuilt at each switch: base rows in file order, then each live mod's rows in room
  order, then derived mixes in the order they were made. Loaded cells are remapped in that tick (one
  pass, about 1 ms over 150 000 cells; switches are rare). Nothing on the wire or on disk holds an
  index: saves, snapshots and the cell hash use id strings, so a late joiner building its own table
  needs no index from anyone.
- **How it lapses.** A row must give `lapse` {into, after?}: `into` a base id (or none for a cover or
  gas; a liquid lapses to water or dries), `after` ticks from its switch, at most 21 600 (an hour). It
  retires on `after` or when the mod is reverted. At that tick the core sweeps loaded chunks, turning
  every cell holding it into `into` and clearing its `Life`; inactive chunks are swept when next
  active, from a retired list in the room state (hashed in `CastHash`, saved). A material-wide
  `after` is apart from a cell's own `lasts`, which still withers single covers sooner.
- **Late join and saves.** A joiner gets the snapshot (cells by id string), the live mods and their
  switch ticks, the retired list and the mixes; a save holds the same (`SaveRoom`), lapses resuming.
- **What may name it.** Only its own mod: another mod's rows, things, items and spawn tables can't, so
  a lapse never leaves a dangling reference. Things can't be made of a temporary material.
- **Limits** (`budgets.json`, base rules only): 8 new materials a mod, 32 temporary rows live in a
  room, 16 derived mixes, 255 ids in all (a byte); P1's `thick` at least 1 and P2's `every` at least
  30 ticks, `chance` at most 0.5; at most one lapse sweep a tick (queued); `simulate`'s 2× slowdown gate
  before `apply`. A stray mod is refused, never half applied.
- **Mix.** Rows that share `mix: "paint"` and meet (a liquid touching another, or a cover laid on
  another) need no reaction row: the core derives `a+b` (ids sorted), a pure function of the two
  rows. Colour mixes as paint does (`Colors.cs`); numbers are the mean, rounded down; tags are those
  both have; it lapses with the later parent. Ticks run in a fixed order, so every peer makes it at
  the same moment and appends it to the table and the room's mix list. Red and blue make purple with
  no row per pair. Past the cap the arriving one stops as at a wall.

## 5. Order of work, and the first test

1. **P3, products by data** (S), and slick covers drawn from data (`looks.json` `needs`); goldens hold.
   *Core done (branch `products-data`): `sim.json` fire.ash/smoke, water.boils/freezes, weather.snow.cover/bank,
   weather.meteors.spell, and a material's `burn.ash`/`burn.smoke`. View done (branch `products-view`): the slick
   and scorch families drawn from data (`looks.json`), ash and snow painted by the rules' names (RULES_MCP.md "Products").
   Not P3 and kept: `waterTo`'s two flat-format words, the storm's `lightning`, `sheds == "lightning"`.*
2. **Temporary materials on strings** (S to M): `lapse`, the sweep, the retired list, the limits.
   *Core done (branch `temp-materials`, RULES_MCP.md "Temporary materials"): every material a mod adds is temporary;
   `lapse` {into?, after?}; a lapse takes the whole mod off (its rows name the material, so they go with it), at the end
   of the tick, one a tick; revert and lapse sweep every loaded cell at that tick (Sanitize already wrote inactive chunks,
   so no wake-time sweep); the retired list in the room (save, `CastHash`); budgets `materials.maxPerMod` 8, `maxLive` 32,
   `maxLapse` 21 600. Black ice passes on two peers (`TempMaterialTests`). View done (branch `products-view`, probe `productsview`):
   RULES_MCP.md "In the view".*
3. **P2 spread** and **P4 glow** (S each).
4. **P1 liquids** (M to L): the kind, the `liquids` table, `Flow`'s rule, `touch`, the flags in
   `Soak`/`Float`/`Cool`/rain, the coloured water mesh, new goldens. Then the interning step.
   *Core done (branch `liquids`, RULES_MCP.md "Liquids"): `Cell.Liquid`, the table (water built in, `thick` 0 to 600,
   `heap` 2 to 16, `soaks`/`floats`/`drowns`, `boils`, `touch`, `speed`, `tags`, `look`, `lapse` into water or dry), flow
   by thick and heap, touch at power 1 (the base `lava-pool` row on a pond for a mod's molten rock), the flags in
   Soak/Float/Cool/rain, `liquid` as a condition and with `addWater`, interning (remap by id at every switch; the cell
   hash, saves and patches by id), temporary like any mod material, the still-water pass waiting out `thick`, the MCP's
   kind `liquid`. Every golden holds; new goldens for honey, red paint and molten rock (`LiquidTests`).
   Then `hot`, `freezes`, `sets` (branch `liquids-2`, `LiquidProductTests`): heat that lights what it touches and
   cools, a freeze into a terrain or cover wherever water would freeze (with the base `cold-liquid` row), a
   `Cell.Sets` count into a terrain; goldens for a mod's magma and syrup. Left: the view's coloured water mesh,
   breath and wading by `DrownDepth`.*
5. **P5 mix** (S, on P1 and temporary materials).

**The first test** (after step 2): asked of Claude, "give me a spell that lays a slick of black ice
that melts in a minute, and take it away again after ten minutes". It ends as one mod: a cover
`black-ice` (`slippery`, `lasts` 360, `melt`, a dark blue look, `lapse` {after: 3600}), one element,
one reaction row and one spell; ten minutes later every peer's slicks are gone. Then: glowing moss
that creeps over the river stones (P2, P4); red and blue paint that runs and makes purple (P1, P5);
a honey spill; a quicksand pit that swallows a raider. No code names any of them.

## Questions for Andrew

1. Products by data and temporary materials first, liquids after? *Recommend: yes; P1 is the big
   family but the M-to-L step, and the first two make every later test cheaper.*
2. When liquids land, does the base lava move to liquid lava (new lava goldens)? *Recommend: not until
   a mod's liquid lava has been played and looks better.*
3. Must every run-time material lapse within an hour, or may one last while its mod is on?
   *Recommend: it lasts while the mod is on, `after` optional; a revert always sweeps.*
4. Mixes: mean of every number, or only colour mixes and the rest from the first parent? *Recommend:
   colour mixes, numbers mean (a thick and a thin paint make a middling one).*
5. Creatures and houses moved to `builders.md` (its four questions stand there): fine? *Recommend: yes.*

## Proposed ADRs (titles only)

- A cell holds a liquid kind, not only water
- What burning, boiling and freezing leave is data
- Temporary materials: mod-made rows that lapse, by id string on the wire, interned at each switch
- Mixed materials are derived from their parents, not listed per pair
