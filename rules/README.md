# Rule tables

The game's interaction rules as data. The web sandbox (`../sandbox/`)
runs on these files today, and the C# `Game.Core` (D3) is meant to load
the same files later, so **a rule change made here changes every
prototype**. Nothing in this folder is specific to the sandbox.

| File | What it holds |
|---|---|
| `materials.json` | Terrain (tags, flammability, fuel, what it burns, breaks, is dug or grows into, elevation), covers (oil, grease, ash, snow, thorns) and gases (steam, smoke) | A terrain with `spring` (units) refills to that much water each tick unless frozen: the valley's river. `soakEvery` / `soakEveryWet` (ticks): thin water on it soaks in and dries up a unit that often, on dry and on damp ground (see Water below). `waterTo`: `ice` (arctic ground: water on it freezes) or `steam` (lava: it boils off a unit at a time).
| `reactions.json` | **The chemistry table.** What each element does when it reaches a square (`cell`), and to a creature or object standing there (`entity`) |
| `spells.json` | The shared SRD spell kit: element, shape (point, line, square, burst, target, or cone: from the caster, widening a square each side per square out), power, damage, push force, earth shaping. `"effect"` is one of `levitate`, `carry`, `teleport`, `terraform`, `haste`, `weather` or `shapeshift`; all but `terraform` change no squares (the Unity game does them: `shapeshift` takes the shape of an animal within `range` for `duration` ticks, times the charge; the sandbox only logs it). `"delay"` lands it that many ticks after the cast, with a target mark on its cells until then. `"scatter"` (`cover`, `count`, `reach`) throws that cover, burning, round where it lands, and `"core"` is an element that lands on its middle last. `"force"` (`throw`: ticks the creature in each cell it reaches is thrown off its feet; `push`: cells it is knocked along the cast's way, if `maxMass` or lighter) is how a cast shoves (Geyser, Wave, Rolling Boulder). `"seeks"` (`range`, `if`: reaction conditions) strikes the nearest cell within `range` of where it is aimed that matches, instead of its shape, when there is one (Lightning to water or metal). `"needs"` (reaction conditions) lands it only on the cells that match, and with none it does nothing but log its `"fizzle"` line (Geyser on water). A line from the caster with `"travelEvery"` (not inward) rolls out as a wave (Wave): its front, `radius` cells to each side, lands its element and knocks things of `maxMass` or less a cell on, rolls a cell every `travelEvery` ticks to the line's length or a wall, then lands its `"leaves"` element there. With `"rolls": true` it rolls as one boulder instead (Rolling Boulder): each turn to the lowest open cell ahead, left or right that is no higher, striking it (a shatter hit, its `"trail"` gas, its damage and a knock over), until it is shut in, reaches water or its length, where its `leaves` lands. `"radial": false` keeps a spell off the Unity game's spell radial. A cast can be charged (held) 1 to 3: power and raise / dig times the charge, height 2 more per level, lines half again as long per level, a point, square or burst a square bigger per level (not earth shaping) |
| `critters.json` | The sandbox's critters (its emitters): what each emits, how often, and in what shape. `hours` (`[from, to]`, across midnight when from is the later) are when it's out; outside them it does nothing. Without, it's always out |
| `components.json` | What a thing can be made of or do, by name, each with what it means: `flammable`, `creature`, `conducts`, `floats`, `breakable`, `heavy`, `brittle`. Entities and items list theirs (`"components": ["flammable", "breakable"]`) and entity rules match on them, so one rule covers everything with a component. At most 64; `flammable` and `creature` are always there (the engine reads them) |
| `entities.json` | Things on the board: HP, mass, `components`, explodes. The old `"flammable": true` and `"creature": true` still load, as shorthand for those components. A `raider` row makes one a defense-field raider: `ticksPerSquare` (its pace), and the elements it's `weak` to (double damage) or `resist`s (half, rounded up) |
| `items.json` | Things to carry in the Unity yard (SRD items and cantrips): mass in kg, light (`bright` and `dim` in feet, `color`, a `cone` in degrees), `use` (`light`, `zoom`, `send`, `point`, `dig` with a whole `strength` from 1), `breaks` (impact speed in m/s and what it spills), `components`. The sandbox carries and checks them but has no items |
| `machines.json` | What each machine needs before it powers the gates: `steam` (a boiler builds pressure to `build`, up to `max`, losing a unit every `decayEvery` ticks), `mass` (a plate pressed by `mass` or more), or `flow`, `charge` or `wind` (it holds `hold` ticks after the hit). Any one powered opens every gate, in both engines (World.Machines.cs) |
| `groups.json` | The Unity game's spell radial: its elements by id (`name`, `order` around the ring from the top, `color` as `#rrggbb`). Each spell in `spells.json` names one with `"group"`; one with none goes under the last. UI only, but mods can regroup or add elements |
| `combos.json` | Casting by elements (`docs/SPELL_COMBOS.md`): the four `elements`, the `window` in ticks for a second one, `chargeTicks` (the cast time each level of charge past the first adds to a held cast), `moveFactor` (walking speed while the cast meter drains, for the view), `singles` by element and `pairs` by first then second element, each with `name`, `spell`, `shape`, `castTicks` and optional `held` (the spell it casts instead when held to full charge) and `todo` (the spell is a stand-in for one not made yet). A single's `castTicks` must be at least the window. `dpad` lists the spells with no element, in cycling order: `name`, `castTicks`, and `spell` (without one the slot shows but can't be cast). Mods deep-merge over it; lists replace whole |
| `sim.json` | The numbers behind the parts written in code: fire spread, water flow units, height range, gas decay, push and slide, damage, the length of a day and the weather. `defense`: the defense field's waves (`lives`, `waves`, `first` raiders and `growth` per wave, `hpPerWave`, the `warmup`, `spawnEvery` and `betweenWaves` ticks, the raider `kinds` in turn) and how raiders find their way (`margin` squares round the path, path costs `wallCost` for a raised wall they batter down a step every `batterTicks`, `roughCost` and `roughTicks` for deep water from `deepWater` units or a climb of more than a step). `breath`: a swimmer's held breath in `seconds`, `refillSeconds` to fill it from empty with the head out, and drowning's hits once it's gone, `drownDice` d `drownSides` every `drownEvery` seconds (read by the Unity view only, `Breath.cs`) |
| `mods/` | Example live mods (`docs/MODDING.md`), not part of the base rules. Each is `{"title", "idea", "patch"}`, which the sandbox's Experiments panel and the C# `ModPatch.Parse` both accept |

## Elements

`fire`, `cold`, `water`, `air`, `earth`, `lightning`, `grease`, `oil`,
`thunder`, `fog` (Fog Cloud: lays the `fog` gas), `growth` (Plant Growth) and
`thorns` (Spike Growth). A spell, a critter, spreading fire or a conducting charge
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
  - `melts` (the cover melts: its material has `melt.water`)
  - `gas` (`"any"` or an id)
  - `terrainTag`, `notTerrainTag`
  - `conductive`
  - `power` (the least power the element came with: a charged cast's power times its charge)
  - `chance` (applies this often, 0 to 1; rolled last on the world's generator, so it only turns when the rest holds)
- **Effects:**
  - `ignite` (an intensity, or `"cover"`), `extinguish`
  - `boil`, `melt`, `freeze`, `condense`
  - `addWater` (a number, or `"power"` for the caster's power)
  - `setCover`, `spreadCover`
  - `break`, `setTerrain`, `raise`
  - `shatter` (a hit on the cell's ice or terrain: it cracks, or if cracked shatters)
  - `scour` (a sand blast: a cover with the `scourable` tag from its material's `scour {}` is stripped; else the terrain becomes its `scour.leaves`; else raised ground whose material has `scour.wears` is worn down a step)
  - `pushCover` (a floating cover moves a cell along the element's way onto open water with no cover)
  - `bake` (the terrain becomes its material's `bake.into`), `meltCover` (the cover becomes its `melt.into` and gives `melt.water`)
  - `dissolve` (`"cover"` or `"terrain"`: eaten away into its material's `dissolve.leaves`, when the rule's element is its `dissolve.by`; a cover can leave none)
  - `grow` (a non-blocking terrain the square becomes, with `ticks: [min, max]` and `wave`; see "Growing" below)
  - `conduct`
  - `fan`, `pushGas`, `clearGas`
  - `mark` (machines read it)
  - `gas` + `n`
- **Entity rules** use `if.status`, and `if.has` / `if.lacks` (a component
  name or a list of them: the entity must have every one, and none it
  lacks). Match on components, never on entity ids, so a new thing joins
  every rule its components name. The old `if.flammable` (`true` is has
  flammable, `false` lacks it) still loads. `if.verb` (`burn`, `freeze`
  or `soak`) holds when the thing's material gives a status by that verb
  (`materials.json` `burn.status` and so on). Their effects are `status`
  (`{ "name": ticks }`), `statusOf` (a verb: the status it gives, burning,
  frozen or wet, for the material's ticks of it), `clear` and `damageMul`.
  A creature's burning, frozen and wet come from its material this way
  (flesh burns, freezes and soaks; bone doesn't burn; chitin doesn't freeze).

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
  "strength": 1,        // optional: a dig's strength, a whole number from 1 (default 1)
  "taper": true,       // optional: a hill or crater, full height in the middle, 1 at the radius
  "stepping": 1,        // optional, with height: cells holding open water rise only this high (stepping stones)
  "ramp": true }        // optional, with height on a line: a step higher each cell, up to the height
```

- **Blocking terrain and machines** (bedrock, trees, gates, boilers)
  can't be shaped.
- **Hardness:** a dig needs a `strength` of at least the terrain's
  `dig.hardness` (1 when absent), and hardness 0 never digs. Lower Earth
  and Earthen Trench (strength 1) move soil, sand, mud and rubble; Fireball
  (strength 2) breaks frozen ground, bridges and glass; Stone Shape and a pick
  (`items.json` `"use": "dig"`, strength 3) dig rock; metal, bedrock, walls
  and machines never dig. Each refusal is logged once a tick with its
  reason: "The raised rock is too hard for Lower Earth", "The metal plate
  can't be dug". Raising isn't affected.
- **Uprooting:** a terrain with `uprootsTo` (a tree topples into
  `deadfall`; a cabin's log wall caves in to rubble) turns into it as soon
  as any shaping touches it, its `uprootLog` logged once a tick, and the
  ground is then shaped as usual. `uprootsTo` must be a known, non-blocking
  terrain. A fallen tree is flammable ground that burns long and hot.
- **Digging** clears the cover and turns the terrain into its `dugTo`
  (grass is dug to dirt; raised rock to rubble) unless the spell names a
  `terrain`.
- **Whirling spells** (`"inward": true` with `pullEvery`: Whirlwind): every
  `pullEvery` ticks one draws gas and light creatures in and runs fire round
  its rings. Every `travelEvery` ticks it moves a square, with the wind or
  else the way it was cast. `carries` lists what it can take on, strongest
  first: `{ "id", "name", "if", "sheds", "log" }`. When the squares round its
  eye match a row's `if` (the reaction conditions), it carries that, and one
  row replaces any after it. It then applies the element `sheds` around its
  eye each turn, and over its whole radius at `dropPower` where it dies out.
- **Raising** spills the square's water onto its neighbours, nearest
  first, so no water is lost.
- **A square raised `sim.height.blockAt` (3) steps or more is a wall:**
  it blocks walking, sight, gas and water like blocking terrain, and loses
  its cover and fire. Dig it back below that to open it. In the sandbox a
  wall won't rise under a creature or object.
- **A line from the caster** (`"shape": "line", "origin": "self"`, like
  Earthen Trench) starts under the caster's feet, so a trench cut from a
  pond's bank opens into the pond. Other line spells start one square out.
- **Lines and cones can point any way**, not only the four ways of the
  grid: a cast carries an aim (a whole-number vector; the game takes it
  from the crosshair, the sandbox from the clicked square). Within about 5
  degrees of a grid way it snaps straight. A slanted line steps along the
  aim with each square sharing a side with the one before (so water runs
  along a slanted trench and a slanted wall has no gaps); its length is
  measured as the crow flies. A slanted cone takes every square within 45
  degrees of the aim. The element still pushes along the nearest grid way.
- An element on the spell, if any, is applied after the shaping.

## Time and weather (`"effect": "haste"`, `"effect": "weather"`)

Spells that change the sky for everyone rather than squares (they touch
none; the cast is still a command, so every peer, and a late joiner
replaying the casts, has the same clock and weather):

- **`haste`** (Hasten Time): the day clock runs `rate` times as fast for
  `duration` ticks, half again as long per charge level past a tap. Cast
  again while it runs and the clock settles back to its own pace, keeping
  the hours gained.
- **`weather`** (Control Weather): each cast holds the next weather after the
  one in force, in the cycle clear, rain, snow, storm (from cloudy, windy or
  fog it clears first), for `duration` ticks (half again per charge level),
  ramping in over `weather.rampMinutes`; then the seed's weather resumes.
  Held weather works even in the calm start.

## Gases (`materials.json` `gases`)

`scald` (damage a creature in it takes), `decay` (its own chance per tick a
unit decays, instead of `sim.gas.decayChance`: fog lingers) and
`blocksSightAt` (from this many units a square of it hides what's behind it:
line of sight, so Misty Step can't blink past thick fog). `burn.flash`
(`{ "fire", "push", "log" }`) makes it go up all at once when fire meets it,
from an element or a burning square it drifts over: the whole cloud joined to
that square is used up, each square burns for a moment at `fire`, the fire
reaches what's there through the rules, and things are shoved `push` hard
(dust). Fire burns fog off
(`fire-fog`, first of the fire rules so it always runs), wind and Gust of
Wind blow it like any gas, and Thunderwave scatters it.

## Growing and lasting (`Cell.life`)

Each square keeps a **life**: ticks until what grows or lasts there
ripens (0 for nothing). When it runs out, a cover with `lasts` withers away
(its `witherLog` logged), then a terrain with `growsTo` becomes it (its
`growLog` logged); a blocking one (a tree) pushes the square's water out
and, in the sandbox, waits while something stands there. A burning square
waits a tick at a time.

- **Terrain `growsTo`, `growLog`:** green shoots (`sprouts`) grow into
  grass, a `sapling` into a tree.
- **Effect `grow`** sets the terrain (not on a raised wall) and its life:
  a roll within `ticks: [min, max]` (default 1), plus `wave` ticks for
  each square (Chebyshev) from where the spell landed, so Plant Growth's
  grass ripens outward from the middle.
- **Cover `lasts`:** the life a cover starts with when laid (`setCover`,
  `spreadCover`): Spike Growth's thorns last 360 ticks, a minute. A cover
  can also say what it does to a creature walking through it: `damage`
  (`[dice, sides]` a square) and `speed` (1 unhindered; thorns 0.5). The
  sandbox hurts and snags creatures that step in (a step takes 1/speed as
  long); the Unity game slows and hurts the wizard (`SurfaceEffects`).
- **Spell `radiusPerCharge`:** a burst's radius grows by this many more
  squares for each level of charge past a tap, on top of the square every
  burst gains per level (`ChargedGrowth`).

The `growth` element's rules (Plant Growth) read tags: `rootstock` (a stump
sprouts a sapling), `soil` under `ash` (burnt ground: green shoots, or with
power 2 a chance of a sapling), `turf` (with power 3 a small chance of a
sapling in the meadow). A blaze (`fire` 2 or more) stops it; embers are
smothered.

## Craters (`"crater"`)

Any spell (Fireball), and an entity's `explodes` (the oil barrel), can blow
a crater out of the ground where it goes off, before its element or cover
lands: `"crater": { "radius": 1, "depth": 2, "strength": 2 }` digs `depth` steps in the
middle, tapering toward `radius` squares out (Chebyshev distance), like a
tapered dig. `depth` is a whole number from 1; `radius` a whole number
from 0 (default 1); `strength` a whole number from 1 (default 1), against
the ground's `hardness`. Dug squares lose their cover and turn to their `dugTo`,
as with any digging, so the middle of a crater doesn't burn.

## Units

- One square is 5 ft.
- Water is measured in units: `sim.water.unitsPerStep` (4) units make
  one step of depth. A pit is one step below the ground, so a full pond
  holds 4 units.
- A thing in `entities.json` or `items.json` names its `material` (a terrain id) and takes what it gives: `burn` makes it `flammable`, `break` `breakable`, `conduct` `conducts`, `shatter` `brittle` (`Rules.MaterialComponents`, the sandbox's `MATERIAL_COMPONENTS`). Its `components` list only the rest (`creature`, `heavy`).
- `materials.json` spells its terrain and covers by **verb groups** (`docs/MATERIALS.md`; the flat keys below still read the same, for mods):
  `"burn": { "catch", "fuel", "heat", "leaves", "log", "look" }` (cover: `catch`,
  `fuel`, `intensity`, `look`), `"break": { "leaves" }`, `"dig": { "hardness",
  "leaves" }`, `"soak": { "dry", "wet" }`, `"freeze": {}`, `"boil": {}`,
  `"conduct": {}`, `"grow": { "into", "log" }`, `"uproot": { "leaves", "log" }`.
  Having `burn`, `break` or `conduct` gives the `flammable`, `breakable` or
  `conductive` tag. The loaders read them as the flat keys
  (`MaterialGroups.cs`, `sandbox/src/materials.ts`); either spelling works.
- Thin water soaks in (`World.Soak`): every `sim.water.soakPass` (8)
  ticks, water no deeper than `sim.water.thin` (2) units, with no
  neighbour deeper either (so not a lake's shore, the river, the sea or a
  filled pit), loses a unit every `soakEvery` ticks of its terrain, or
  `soakEveryWet` when the ground is damp: raining, or damp for
  `sim.water.dampTicks` (600) after water soaked in there. A terrain
  without them never soaks; `soakPass` 0 turns soaking off.
- Time is in ticks, `sim.ticksPerSecond` (6) a second.
- A day lasts `sim.day.lengthMinutes` (24) real minutes, so a game hour
  is a real minute; the world starts at `sim.day.startHour` (9:00).

## Active chunks (`sim.streaming`)

The world is simulated only around the characters (`docs/WORLD_STREAMING.md`;
C# only, the sandbox has no chunks). Every chunk within `sim.streaming.reach`
(2) chunks of a character's chunk is active; a chunk deactivates once no
character is within `sim.streaming.keep` (3), which must be at least the
reach. Distances are in chunks, the larger of the two axes. A chunk is
settled once nothing in it has been live (fire, gas, charge, a timer, water
moving) for `sim.streaming.settleTicks` (12) ticks. A chunk out of every
keep distance that isn't settled lingers, and live cells on its edge
activate the inactive chunk across it; at most `lingerChunks` (16) linger at
once, the nearest first, and none longer than `lingerTicks` (1800 ticks, five
minutes). While rain falls, water moving doesn't count as live. A chunk the
caps let go while live runs the ticks it missed when it next activates, up to
`catchUpTicks` (720, two minutes).

## Day and weather (`sim.day`, `sim.weather`)

`day.dawn` and `day.dusk` are `[from, to]` hours of dim light. Between them
the daylight is bright, and from the end of dusk to the start of dawn it is
dark: that is the night. The light on a cell (the SRD's bright, dim or
darkness; `World.LightAt`, `night.ts`) is the daylight, or a burning
cell's light if brighter: bright out to `day.fireLight.bright` feet and dim
for `dim` feet beyond, by grid distance. It is worked out when asked, from
the clock (`World.ClockTick`) and the cells, so it is the same everywhere
and never kept.

The time of day and the weather are worked out from the tick and the
world's seed alone (`sandbox/src/sky.ts`, `Sky.cs`), so every player has
the same sky. Weather comes in stretches of `periodMinutes` real minutes,
each ramping in and out over `rampMinutes`; the first `calmPeriods` are
clear, then each stretch is picked by `weights` (`clear`, `cloudy`,
`windy`, `fog`, `rain`, `storm`, `snow`; leave one out or set it to 0 and it never
comes). What it does to squares, at full strength:

- **rain** (and storms): each tick a fire may burn down a step
  (`rain.douseChance`), fire spreads `rain.dampen` less, and every
  `rain.fillEvery` ticks each square may get a unit of water
  (`rain.fillChance`), so hollows and trenches fill. Without rain, every
  `rain.dryEvery` ticks a square two units deep or less may lose one
  (`rain.dryChance`): puddles dry, ponds stay.
- **snow**: holds fire back half as much as rain does, and every
  `snow.freezeEvery` ticks the water on a square two units deep or less may
  freeze (`snow.freezeChance`): puddles and trenches ice over, ponds and
  the lake don't. On the same beat open, dry, unburnt ground may take a
  `snow` cover (`snow.coverChance`), and a snowy square with something
  blocking beside it (on its upwind side in a wind: a wall, a tree, a rock)
  may drift into a `snowbank` (`snow.bankChance`), which stands the ground
  half a metre higher. Fire melts them (`fire-melt` in
  `reactions.json`, before `fire-flammable`, so snowy grass won't burn
  until the snow is gone). With neither rain nor snow, every
  `rain.dryEvery` ticks such shallow ice may thaw again
  (`snow.thawChance`), a cold spell's too, a bank may sink back to snow
  and snow may melt into a puddle. **At night** snow freezes water
  `day.nightFreeze` times as fast, and nothing thaws or melts till morning.
- **windy** and **storm**: a prevailing wind that fire and gas follow like
  Gust of Wind's (which wins while it blows).
- **storm**: every `storm.strikeEvery` ticks, lightning may strike a random
  square (`storm.strikeChance`), applying `lightning` like Shocking Grasp.
- **meteor showers**: a clear stretch (after the calm) is a shower with
  chance `meteors.showerChance`, ramping in and out within it. Every
  `meteors.every` ticks a meteor may appear over a random square
  (`meteors.chance`), and `meteors.fallTicks` later it lands as the
  `meteor` spell (its crater, its fire) with "A meteor strikes". One that
  lands where no one is near (a paused chunk) leaves no mark.
- **cloudy** and **fog** change only how the world looks.

## The parts that are code, not data

These are the cellular-automaton steps, parameterised by `sim.json`.
They live in `../sandbox/src/sim.ts`, and `Game.Core` will need its own
port of them:
- fire spreading and burning out;
- water flowing downhill (by elevation plus height);
- raising and digging squares (terraform spells);
- gas drifting and decaying;
- charge decaying;
- statuses ticking down;
- the weather's rain, drying and lightning.

A burning square spreads by **applying `fire` to its neighbours**. So
what spreading fire does to ice, water or oil next door is still decided
by the table above, not by the code.

## Licence note

Spell names and their basic behaviour come from SRD 5.2.1 (CC BY 4.0;
see the root README). Where a spell's physical behaviour goes beyond the
SRD text, the spell's `blurb` says so. For example, Ray of Frost
freezing water and Grease burning are this game's readings.
