# One material model (design note)

Andrew, 2026-10-28: a **material** is the single source of what something
does when an effect reaches it. A material has one settings group per
**verb** (what happens to it). Having the group is what turns the behaviour
on: the `flammable` tag and component go away, and a `burn` group takes
their place. Terrain cells, covers, built things (walls, roofs), props,
items and, later, creatures all *name* a material; none of them carry
behaviour of their own.

This is a plan, not built yet. The steps at the end keep the shared
goldens (`CellHash`) and lockstep exact at every merge.

## Where we are (2026-10-28)

| What | Where its behaviour lives today |
|---|---|
| Terrain (dirt, grass, logwall, stone, thatch, timber, arid, arctic, lava...) | `materials.json` `terrain`: tags (`flammable`, `blocking`, `breakable`, `conductive`, `soil`, `turf`, `low`, `vessel`, `roof`...) plus loose keys (`flammability`, `fuel`, `heat`, `burnsTo`, `breaksTo`, `dugTo`, `hardness`, `soakEvery`, `soakEveryWet`, `waterTo`, `fireLook`, `spring`, `elevation`, `growsTo`) |
| Covers (oil, grease, ash, snow, snowbank, thorns) | `materials.json` `covers`: tags (`flammable`, `slippery`, `spiky`, `snow`) plus `flammability`, `fuel`, `burnIntensity`, `lasts`, `damage`, `fireLook` |
| Gases (steam, smoke, fog) | `materials.json` `gases` (decay, how they move) |
| Props (crate, barrel, boulder) | `entities.json`: `components` (`flammable`, `breakable`, `heavy`), `hp`, `mass`, and since today a `material` (timber, rock) that sets catching and burning |
| Items (lantern, spyglass...) | `items.json`: `components` (`brittle`), `mass`, `breaks`, and a `material` that nothing reads yet |
| Creatures (wizard, goblin, raiders) | `entities.json`: the `creature` component, `hp`, statuses (`wet`, `frozen`, `burning`) through `entityRules` in `reactions.json` |
| Buildings | `world/villages.json` `materials`: a wall material (stamped on the footprint's cells) and a roof material (the view's roof) |
| Components | `components.json`: `flammable`, `creature`, `conducts`, `floats`, `breakable`, `heavy`, `brittle` |

So one idea, "this burns", is spelled four ways: the `flammable` terrain
tag with `flammability`/`fuel`, the `flammable` cover tag, the `flammable`
component, and the `burning` status rule. The model folds them into one.

## The data shape

`materials.json` gains a `materials` table. Each entry has a name, the
tags that are *properties* rather than behaviours (`ground`, `blocking`,
`low`, `soil`...), and one group per verb it has. Leaving a group out
means the verb does nothing to it.

```json
"materials": {
  "thatch": {
    "name": "Thatch",
    "burn":  { "catch": 0.6, "fuel": 18, "heat": 2.5, "leaves": "rubble", "look": "flare" },
    "soak":  { "dry": 80, "wet": 400, "dampens": 0.5 }
  },
  "logwall": {
    "name": "Log wall", "tags": ["blocking"],
    "burn":  { "catch": 0.15, "fuel": 140, "leaves": "rubble", "look": "wood", "log": "Log walls burn and collapse" },
    "break": { "hardness": 2, "leaves": "rubble" }
  },
  "stone": {
    "name": "Stone wall", "tags": ["blocking"],
    "break": { "hardness": 3, "leaves": "rubble" }
  },
  "metal": {
    "name": "Metal", "tags": ["ground"],
    "conduct": { "chance": 1 }, "rust": { "every": 2000, "leaves": "rusted-metal" }, "dig": { "hardness": 0 }
  },
  "glass":  { "name": "Glass", "shatter": { "drop": 1.5, "leaves": "shards" } },
  "flesh":  { "name": "Flesh", "burn": { "catch": 0.5, "status": 24 }, "freeze": { "status": 30 }, "soak": { "status": 30 } }
}
```

Terrain, covers, props, items and creatures then *name* one:

```json
"terrain":  { "grass": { "material": "grass", "elevation": 0 } },
"entities": { "crate": { "name": "Crate", "hp": 6, "mass": 1, "material": "timber" } },
"items":    { "bullseye-lantern": { "name": "Bullseye lantern", "material": "glass" } }
```

A terrain keeps what is about *the cell* (elevation, spring, growing
into): the material keeps what the stuff does. A thing keeps what is about
*the thing* (hp, mass, use).

## The verbs

Each group's keys, and what reads them today.

| Verb | Group keys | Replaces |
|---|---|---|
| **burn** | `catch` (0..1, chance a fire lights it; a prop counts 1/catch ticks), `fuel` (ticks), `heat` (spread multiplier), `leaves` (material after), `look` (flare / wood / oil), `log`, `status` (ticks, for things) | `flammable` tag, `flammability`, `fuel`, `heat`, `burnsTo`, `burnLog`, `fireLook`, `burnIntensity`, the `flammable` component, `e-fire-flammable` |
| **melt** | `into` (the cover left, or none), `water` (units it gives) | built on covers (snow, snow bank): the `meltCover` effect, from fire and heat; `fire-ice` is still a reaction |
| **bake** | `into` (the terrain heat turns it into) | built: sand to glass, mud to brick, through the `heat` element (Forge) and the `bake` effect; plain fire bakes nothing |
| **freeze** | `leaves` or `status` (ice, frozen) | `waterTo: "ice"` (arctic), Ray of Frost's freeze, the `frozen` status |
| **soak** | `dry`, `wet` (ticks per unit soaked in), `dampens` (how much a damp cell's or wet thing's `burn.catch` and `burn.fuel` drop), `leaves` (what water soaking in turns it to: dirt to mud), `dries` (what it turns back to once no longer damp: mud to dirt), `status` (wet, for things) | `soakEvery`, `soakEveryWet`, the `wet` status |
| **boil** | `leaves` (steam) | `waterTo: "steam"` (lava) |
| **break** | `hardness`, `leaves`, `hp` for things | `breakable` tag/component, `breaksTo`, `hardness` |
| **shatter** | `drop` (fall height that breaks it), `leaves`, `cracks` (built: intact, cracked, shattered; the first hit cracks it, the next breaks it. Glass ground, window panes, things of glass, and the material `ice`, which is frozen water's and frozen creatures') | `brittle` component, items' `breaks`; `leaves` built on terrain: a blast, shaken ground or a heavy thing turns a cell of it to that (`World.Shatter`: a window `pane` to an open gap of rubble), and a pane with no wall beside it falls (`WallLost`) |
| **conduct** | `chance`, `chainsWet` (through water next to it) | `conductive` tag, `conducts` component; `chainsWet` built: on flesh, bone and chitin, a wet creature beside freshly charged water is shocked as if in it (`World.ShockedFromBeside`); a group holding only `chainsWet` doesn't make the material conduct |
| **float** | `lift` (above 1 floats), `every` (a cover: on open water it spreads to the open water beside it every this many ticks), `reach` (how many cells from where it was laid; `Cell.Slick` counts down; built for oil, `floatEvery`, `floatReach`, `World.Float`) | `floats` component, PropState's mass rule |
| **grow** | `into`, `every` | `growsTo`, `growLog`, Cell.Life ripening |
| **rot / rust** | `every` (ticks wet in all), `leaves` | rust built: `rustEvery`, `rustsTo`; `Cell.Rust` counts a wet cell's ticks on the soak pass (`World.Rust`). Rot is new |
| **dissolve** | `by` (an element: acid), `leaves` (a terrain's is a terrain; a cover's a cover, or none) | built: the `dissolve` effect of `acid-cover` / `acid-terrain` in reactions; brambles to dirt, oil, grease and thorns eaten (Acid Splash) |
| **dig** | `hardness`, `leaves` | `dugTo`, `hardness` |

## How the verbs combine

Each verb reads the cell's or thing's current state, so the combinations
fall out of order and one rule each. These are already true today unless
marked *new*.

- **Soaked burns poorly:** a damp cell (`Cell.DampUntil`, or raining), or a
  thing with the wet status, catches with its `burn.catch` scaled by
  1 - `soak.dampens`, and what catches burns its `burn.fuel` scaled the same
  (rounded down, at least 1 tick). Built (`World.Dryness`, `DampFuel`); rain
  also still damps spread globally through `RainDamp`.
- **Wet puts fire out:** water on a burning cell puts it out, unless the
  burning thing floats on it (oil, grease). That's today's `Burn` rule;
  it becomes "a `burn` without `float` is put out by water".
- **Frozen things shatter:** a thing with the `frozen` status gains a
  `shatter` it lacks (a struck frozen goblin breaks) (*new*).
- **Wet ground conducts:** lightning chains through water (conduct over wet
  cells, up to `sim.conductLimit`) and shocks every creature in it; with
  `conduct.chainsWet` a wet creature on the bank beside it is shocked too
  (shock-water, built).
- **Hot metal:** fire reaching metal leaves it hot for `hot.for` ticks
  (`Cell.Heat`, the `fire-heat` reaction). A creature on it takes 1 fire damage
  every `burnDamageEvery` ticks; water on it boils off a unit a tick as steam,
  each unit taking `hot.quench` ticks of its heat (built).
- **Steam scalds flesh and condenses:** thick steam (2 or more) scalds a creature
  whose material has `scald` (flesh; not bone or chitin) as fire damage; it
  drifts and spreads as any gas does, and each time a decaying unit takes a
  cell's count from a multiple of `gases.steam.condenseEvery` (2) it leaves a
  unit of water there, but only `gases.steam.condenseAway` (1) cells or more
  from heat: not in, or next to, a burning or hot cell, so it doesn't rain
  straight back onto the fire. Cold condenses it at once (`cold-steam`). Built.
- **Wind carries and fans:** Gust of Wind's air pushes each cell's gas (smoke,
  fog, steam) one cell along it, shoving a different gas ahead rather than
  merging it; against a wall it stays (`pushGas`). It fans a fire of 2 or more
  hotter and on downwind (`fan`), blows out a small one, and keeps hot metal
  hot (`air-hot`, the `hot` condition). Built.
- **Lightning fuses sand:** a strike on terrain tagged `fuses` (sand) turns
  it into glass (`lightning-fuse` in reactions; built).
- **Heat melts, cold freezes:** `melt` and `freeze` check the cell's water
  and cover first, as the fire and frost rules in `reactions.json` do now.
- **Hot burns light neighbours:** `burn.heat` (thatch flaring lights log
  walls), as built today.

## Creatures as flesh

A creature names `flesh` (or `bone`, `chitin` for raiders) like anything
else. Its `burn.status`, `freeze.status` and `soak.status` replace the
creature branch of `entityRules`. `hp` stays on the thing. A bone-hound
can resist fire because bone has no `burn` group, not because of a raider
`resist` list. The `creature` component stays: it's about moving and
taking damage, not about a material.

## Migration, in small test-guarded steps

Each step merges on its own with the usual checks (core and sandbox suites,
both checkers with the same messages, smoke and a probe). The rule for
every step: **the same rules give the same world**. Goldens move only in a
step that says so, and then in both engines together.

1. **Read the groups as aliases (no behaviour change).** The loaders turn
   today's keys into the groups (`flammable` tag + `flammability` + `fuel`
   → `burn`). The checkers accept either spelling. Goldens untouched. Test:
   every terrain and cover yields the same groups both ways, in C# and TS.
2. **Code reads groups, not tags.** Replace `Has("flammable")`,
   `CoverHasTag(c, "flammable")`, `conductive` and the component checks
   with "has a `burn` (`conduct`, `float`...) group". Goldens untouched,
   since the data didn't change. The two `burning` paths (cells and props)
   share one `Catches`.
3. **Move `materials.json` to the new shape** (a one-off script, its
   output committed), with terrain and covers naming materials. The aliases
   from step 1 still read old mods, so `docs/MODDING.md` examples keep
   passing. Goldens untouched.
4. **Things name materials only.** `entities.json` and `items.json` drop
   `flammable`, `conducts`, `floats`, `brittle` and `breakable` in favour of
   their material. `components.json` keeps `creature` and `heavy`.
   PropState and the sandbox's entities read the material. Goldens
   untouched where the numbers match; where a component had no matching
   material (a brittle item), add the material first, in its own commit.
5. **Creatures as flesh, bone or chitin.** Move the creature branch of
   `entityRules` into those materials' `status` keys. The defense golden
   is the guard: unchanged, or moved in both engines with the reason in
   the commit. *Built:* `burn`, `freeze` and `soak` take a `status`
   (flesh 9 / 30 / 60, bone no burn, chitin no freeze); the creature rules
   (`e-fire-creature`, `e-water-creature`, `e-cold-wet`) read it through
   `if.verb` and `statusOf`, and things keep their fixed ticks. A group
   holding only its `status` doesn't make the material flammable or freeze
   water. The defense golden didn't move.
6. **New combinations, one per merge,** each with its golden moved in both
   engines and a probe: damp ground dampens catching, frozen things
   shatter, rust, then dissolve.
7. **Drop the aliases** once no rules file or mod example uses the old
   keys. The checkers then reject them with a message naming the new key.

Lockstep: materials are rules, and rules reach every peer the same way they
do now (`NetChem`'s rule switches), so no step changes how peers agree.
Everything a verb does stays integer state through the world's seeded
generator, as it is today.
