# Primitives: the world from a few general parts

Andrew, 2026-11-01: "I don't wanna hard code the world. I wanna find some primitives that can be
modified to creat convincing effects. Maybe creating new temporary materials from the primitves of
the game?" And: "all bears now have 6 legs. Or all deer are now centuars with a human top half."
J-055 and J-057: no named content in code. A new thing is a new combination of primitives, as data,
and a new primitive is added only when a whole family of effects needs it. Design only, no code.
Builds on `docs/design/world-tools.md` (G1 to G6) and `docs/RULES_MCP.md`.

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

## 3. The smallest set of new primitives

Each unlocks a family; a single effect never justifies one.
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

## 5. Creatures and buildings are made of parts too

**Creatures today.** A species is a Punchy `CreatureSpecies`, an Editor `ScriptableObject` asset
(`ThirdParty/Punchy/Fauna/*.asset`): a body (length, radius, taper, flatten), `Base` and `Accent`
colours with weighted `Coats`, a `Size` range, `VariantOf` (recolour and rescale another species),
`Parts` (a list of `LimbSlot`s: a `LimbType`, a parent part or the body, a socket, a direction,
mirror, gait phase and mirror phase, scale, thickness), gait (walk and run speed, bob, rock,
`Upright`) and behaviour (wander, idle, flee, group size, near water, active time, grazing, thirst,
play, laziness, `Hunts` by species reference, hunger). A `LimbType` (`Fauna/Limbs/*.asset`, 76 of them)
is a role (leg, arm, tail, neck, head, ear, horn, eye, feature), tapered segments with an accent or
fixed colour and glow, and seven key poses. `ChemYardBuilder`'s `Fauna` table names 13 species with
a density each and bakes them into the scene; `ValleyFauna` builds each body at run time with
`CreatureBody.Build(species)`. All Editor assets: a mod can't reach any of it.

**"All bears have 6 legs."** *Body plan and slots:* a bear is two mirrored leg pairs (sockets at z
-0.7 and 0.72); a third pair is one more `LimbSlot` at z 0 with the same `BearForeleg`. *Gait:* each
slot has its own phase, so the pairs at 0, 0.33 and 0.67 make a ripple; `MeasureGait` averages stride
and stand height over any number of body legs; two-bone IK places every body leg's foot. So it works
today as data, in the Editor. Lying down folds every leg that hangs below the body, so the extra pair
folds as well. *Gap:* only that the species is an asset.

**"All deer are centaurs with a human top half."** *Grafting:* a `HumanTorso` slot (a feature) at the
deer's front pointing up, with `HumanArm` (mirrored) and `HumanHead` (and `HumanHair`) parented to
it, and the deer's neck and head slots removed. Slots already chain by parent index, so the shape works
today. *Gait:* the deer's legs are unchanged; arms swing from their own poses. The head turns towards
what it looks at, but `Upright` is false, so it nods like a deer's (fine, or a per-slot `upright`
flag later). *Hair and skin:* this is where it stops. A creature has one `Base` and `Accent`: a human
wears `Base` as clothes and `Accent` as skin, a deer `Base` as coat, so the torso comes out in deer
coat. The gap is **a palette per slot**: a slot may take its colours from another species' coat
(`colours: "human"`), so the top half gets human skin and hair, and the bottom half keeps its coat.
Per-individual hair and skin variety is the Mac's `life-variety` (`docs/handoffs/briefs/punchy-life.md`
item 3), so this follows it. *Behaviour:* still a deer's (it grazes, flees); a behaviour row could
come from another species, but the test doesn't need it.

**The smallest general gaps** (each for a family: insects, chimeras, winged beasts, tall goblins):
- **C1. Species as rows.** `rules/fauna.json`: every species' fields above, parts naming limb types
  by name from the shipped library, `variantOf`, coats, and the spawners (the `ChemYardBuilder` table:
  species, density, near water). An Editor step writes today's assets out as rows once; at run time
  `ScriptableObject.CreateInstance` builds a `CreatureSpecies` from a row and `CreatureBody.Build` runs
  as now. A patch can then `add` a slot or swap a list, and `Hunts` names ids. Limb types stay assets
  for now (a pose is hard to write as data); a row may scale and thicken them.
- **C2. A palette per slot** (above), on top of `life-variety`'s per-animal colours.
- **Don't collide with the Mac.** It's porting upstream Punchy's newer parts now: individual
  villagers, `HumanChild`, the changed humanoid species, `Hair*` and `Beard*` limbs, `StyleGroup`,
  then `life-variety`'s ranges in data and the water species. C1 and C2 touch `CreatureSpecies.cs`,
  `CreatureBody.cs` and `ValleyFauna`, which it owns now. Start C1 after `life-variety` merges, and
  write its rows from the assets as they stand then, ranges included.

**Buildings today.** A village (`Village.cs`, in the core, D12) is a plan from `world/villages.json`
and a seed: each building's kind (house, inn, shop), footprint of wall cells, front, storeys (1 or
2), `WallKind` (timber, stone, log), `RoofKind` (gable, hip), `RoofCover` (thatch, slate, shingle),
door and window cells (`pane`). `villages.json` "materials" maps each wall kind and roof cover to a
terrain id, so walls burn as their material says. `BuildingMesher` draws it from a `Spec` and the
building's seed: fixed colour palettes per kind (`Plasters`, `Stones`, `Thatches`...), a pattern per
wall kind, any number of storeys (logs capped at one). The cabins are `ValleyShape.Cabins`, a C#
constant built in the Editor (`ValleyBuilder`).

**A house with a new roof, an extra storey and brick walls.** *Brick walls:* a `brickwall` terrain row
(`blocking`, `trellis`, `break` into rubble, no burn) is data today; a `brick` wall kind isn't (the
enum is closed), and the mesher has no brick pattern. *Extra storey:* the plan's `Storeys` is drawn
as written and touches no cell (the roof isn't a cell), so it's view only, but nothing lets a mod set
it. *A new roof:* a red-tile cover is a roof terrain row (`roof` tag, a look); a new *shape* (gambrel,
flat) is mesher code. Gaps: **B1.** wall kinds, roof covers and roof shapes become rows (a wall kind:
terrain, `pattern`; a roof: cover terrain, shape from the mesher's fixed list, pitch, overhang),
replacing the enums; that's G2's `pattern` on a `wall` look family, with colours read from the
materials' looks rather than palettes. **B2.** One building edited by id is G3 (G1 gives it the id);
the cabins becoming village buildings (J-056) puts them on the same path. A new roof shape is a
family of its own, proposed only when more than one is wanted.

## 6. Through the MCP, and what stays safe

A species, a limb slot, a wall kind or a roof is a row a mod patches (`set_part`, `add_part` drafts
beside `set_look`). **View only, out of `Rules.Fingerprint`:** all of `fauna.json` (bodies, slots,
coats, gait, behaviour, spawners), a building's look, pattern, roof shape and storeys. Each peer
redraws at the switch (`RedrawLooks`); ambient life stays per peer, as it is now. **Shared state, in the
fingerprint and the cell hash:** anything that lands on cells. A wall kind's terrain (log to brick
changes what burns) and a footprint go through G3's `world` section and are laid at the switch tick
like the dragon's freshening, with a golden. A raider is one cell, with its `hp`, `mass` and speed in
`entities.json` (already in the fingerprint). Its *look* names a species (G4's `creature` family),
so six legs or a bigger body on a bone hound is view only, scaled to stay within its cell. A raider
that fills more cells is a core change and isn't proposed. The rule that keeps it safe: the core never
reads `fauna.json` or a building's look, and the checker refuses a view row that names a core field.

## 7. Order of work, and the tests

1. **P3, products by data**: S, no behaviour change, goldens hold. With the slick-cover drawing
   gap closed, paint and a slick of ice work as one mod each.
2. **Temporary materials** (§4) with string ids and `lapses`: S to M, needs no interning.
3. **P2, covers that spread, and P4, glow**: S each; glowing moss is one mod.
4. **C1, species as rows**, after `life-variety` merges: M. Then 6-legged bears are one mod.
5. **B1** with G1 to G3: M. Then the brick house is one mod.
6. **P1, liquids**: M to L, with the interning step. Then tar, honey, quicksand and lava pits.
7. **C2, a palette per slot**, and **P5, mix**: S each. Then centaurs and purple paint.

**First test:** "pour me some red and blue paint that runs and stains", asked of Claude, ends as one
mod (two temporary covers, a spell row, `lapses` into nothing) with no code naming paint. **The
creature test:** "make all bears have six legs" and "make all deer centaurs with a human top half",
each asked of Claude, each ending as one mod patching `fauna.json` (a slot added; slots swapped, with
`colours: "human"`), seen on every peer, fingerprint unmoved. **The building test:** "give the house
by the lake brick walls, a red tile roof and another storey" ends as one mod with a G3 world edit.

## Questions for Andrew

1. Liquids (P1) cost about 1.2 MB and 0.25 ms a tick. Build it, or try tar and honey as slow covers
   first? *Recommend: covers first, P1 when a pooling liquid is wanted (lava pits, quicksand).*
2. Should temporary materials lapse by default after a while, or stay until the mod goes? *Recommend:
   stay until reverted; `after` is optional.*
3. Fauna mods: per room (everyone sees the centaurs) or per player? *Recommend: per room, through the
   mod path, still view only (J-056 said animals as data per room).*
4. May a centaur behave like a person (walk to the well, go in at night), or only look like one?
   *Recommend: look only now; behaviour from another species' row is a later C3.*
5. Brick walls change what burns, so they're shared state. Allowed in an Open room from anyone's
   Claude? *Recommend: yes, like any mod; the room's mode already decides.*

## Proposed ADRs (titles only)

- A cell holds a liquid kind, not only water
- Temporary materials: mod-made rows with a lapse, ids given at the switch
- Creatures and buildings are data: species, parts and building kinds as rows, view only off cells
