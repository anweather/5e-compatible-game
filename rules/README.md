# Rule tables

The game's interaction rules as data. The web sandbox (`../sandbox/`)
runs on these files today, and the C# `Game.Core` (D3) is meant to load
the same files later, so **a rule change made here changes every
prototype**. Nothing in this folder is specific to the sandbox.

| File | What it holds |
|---|---|
| `materials.json` | Terrain (tags, flammability, fuel, what it burns, breaks or is dug into, elevation), covers (oil, grease, ash) and gases (steam, smoke) |
| `reactions.json` | **The chemistry table.** What each element does when it reaches a square (`cell`), and to a creature or object standing there (`entity`) |
| `spells.json` | The shared SRD spell kit: element, shape, power, damage, push force, earth shaping |
| `critters.json` | Critters: what each emits, how often, and in what shape |
| `entities.json` | Things on the board: HP, mass, flammable, explodes |
| `items.json` | Things to carry in the Unity yard (SRD items and cantrips): mass in kg, light (`bright` and `dim` in feet, `color`, a `cone` in degrees), `use` (`light`, `zoom`, `send`, `point`), `breaks` (impact speed in m/s and what it spills). The sandbox carries and checks them but has no items |
| `machines.json` | What each machine needs before it powers the gates |
| `sim.json` | The numbers behind the parts written in code: fire spread, water flow units, height range, gas decay, push and slide, damage |
| `mods/` | Example live mods (`docs/MODDING.md`), not part of the base rules. Each is `{"title", "idea", "patch"}`, which the sandbox's Experiments panel and the C# `ModPatch.Parse` both accept |

## Elements

`fire`, `cold`, `water`, `air`, `earth`, `lightning`, `grease`, `oil` and
`thunder`. A spell, a critter, spreading fire or a conducting charge
*applies an element to a square*. That is the only way anything reacts.

## How `reactions.json` is read

```jsonc
{ "id": "fire-ice", "element": "fire",
  "if": { "frozen": true },        // every key must hold
  "do": [{ "melt": true }],        // run in order
  "log": "Ice melts back into water",
  "notable": true,                 // log even when it happened indirectly (spreading)
  "stop": true }                   // don't run later rules for this element on this square
```

- For an element arriving at a square, rules with that `element` run
  **top to bottom**. Order is meaning: `fire-oil` comes before
  `fire-water`, so burning oil floats on water instead of boiling it.
- **Conditions:**
  - `cover` (list)
  - `fire` (`true`, `false` or a minimum)
  - `fireBelow`
  - `water` (unfrozen water present)
  - `frozen`
  - `gas` (`"any"` or an id)
  - `terrainTag`, `notTerrainTag`
  - `conductive`
- **Effects:**
  - `ignite` (an intensity, or `"cover"`), `extinguish`
  - `boil`, `melt`, `freeze`, `condense`
  - `addWater` (a number, or `"power"` for the caster's power)
  - `setCover`, `spreadCover`
  - `break`, `setTerrain`, `raise`
  - `conduct`
  - `fan`, `pushGas`, `clearGas`
  - `mark` (machines read it)
  - `gas` + `n`
- **Entity rules** use `if.status` and `if.flammable`. Their effects
  are `status` (`{ "name": ticks }`), `clear` and `damageMul`.

## Earth shaping (`"effect": "terraform"`)

A spell with this effect raises or digs every square its shape touches.
Each square keeps a **height**: whole steps on top of its terrain's
`elevation`, within `sim.height` `min`..`max` (-3..4). Water flows by
elevation plus height.

```jsonc
{ "id": "move-earth", "name": "Move Earth", "level": 6, "shape": "burst", "radius": 3,
  "effect": "terraform",
  "raise": 2,           // or "dig": n, or "height": n. Exactly one of the three.
                        // raise and dig: a whole number of steps from 1, relative to
                        // each square (not the reaction verb "raise", which puts up a
                        // pillar terrain). height: raise each square to that height,
                        // within sim.height min..max, leaving higher squares alone, so
                        // "height": 3 (blockAt) always walls, even across a trench.
  "terrain": "rock",    // optional: what the squares become (not a blocking terrain)
  "taper": true }       // optional: a hill or crater, full height in the middle, 1 at the radius
```

- **Blocking terrain and machines** (bedrock, trees, gates, boilers)
  can't be shaped.
- **Digging** clears the cover and turns the terrain into its `dugTo`
  (grass is dug to dirt; raised rock to rubble) unless the spell names a
  `terrain`.
- **Raising** spills the square's water onto its neighbours, nearest
  first, so no water is lost.
- **A square raised `sim.height.blockAt` (3) steps or more is a wall:**
  it blocks walking, sight, gas and water like blocking terrain, and loses
  its cover and fire. Dig it back below that to open it. In the sandbox a
  wall won't rise under a creature or object.
- **A line from the caster** (`"shape": "line", "origin": "self"`, like
  Earthen Trench) starts under the caster's feet, so a trench cut from a
  pond's bank opens into the pond. Other line spells start one square out.
- An element on the spell, if any, is applied after the shaping.

## Units

- One square is 5 ft.
- Water is measured in units: `sim.water.unitsPerStep` (4) units make
  one step of depth. A pit is one step below the ground, so a full pond
  holds 4 units.
- Time is in ticks, `sim.ticksPerSecond` (6) a second.

## The parts that are code, not data

These are the cellular-automaton steps, parameterised by `sim.json`.
They live in `../sandbox/src/sim.ts`, and `Game.Core` will need its own
port of them:
- fire spreading and burning out;
- water flowing downhill (by elevation plus height);
- raising and digging squares (terraform spells);
- gas drifting and decaying;
- charge decaying;
- statuses ticking down.

A burning square spreads by **applying `fire` to its neighbours**. So
what spreading fire does to ice, water or oil next door is still decided
by the table above, not by the code.

## Licence note

Spell names and their basic behaviour come from SRD 5.2.1 (CC BY 4.0;
see the root README). Where a spell's physical behaviour goes beyond the
SRD text, the spell's `blurb` says so. For example, Ray of Frost
freezing water and Grease burning are this game's readings.
