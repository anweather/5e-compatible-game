# Glossary: the game's ubiquitous language

One word for each idea, used the same way in code, docs, commits and
conversation. When a word here names something, use it; when you need a
new idea, add its word here first. Where a word is listed under **Avoid**,
it either means something else or is retired.

Agreed with Andrew 2026-10-27. Older docs still use some retired words
(chiefly "square" for cell and "wake/sleep" for chunks); fix them when you
touch them.

## The world and its space

**Building row**
: One building as data (`rules/buildings.json`, `world/buildings.json`, a mod's `world` section, by
  id): its **joins**, **wall segments** and **openings**, a **fill** and a **style**.

**Join**
: A point a wall segment runs from or to, on the half-cell lattice (0.75 m), in half cells from the row's `at`.

**Wall segment**
: A wall between two joins, along an axis or at 45°, which takes the cells on its right.

**Opening**
: What's cut in a wall segment: a door, window or arch, a row of `rules/buildings.json`.

**Fill**
: What's left inside a building's walls: `hollow` (a floor) or `solid` (its wall's terrain).

**Style**
: A row of a building's defaults, and a recipe that lays walls from its seed when it has none.

**Site**
: The cells a building stands on, checked by every peer when it switches on.

**Pane**
: A window's glass: a `pane` cell in a building's wall (`world/villages.json`,
  a cabin's map), which blocks like the wall, doesn't burn, and shatters to an
  open gap under force. *Not* the walkable `glass` a strike fuses from sand.

**Cell**
: The unit of chemistry: 1.5 m (5 ft) square. It holds a terrain, water,
  a cover, fire and fuel, gas, charge, height and life (`Chem.Cell`).
  Everything the rules do, they do to cells. Player-facing text may say
  "square", but code, docs and talk say cell. *Avoid:* square (for this),
  tile, board (except the sandbox's board).

**Cell coordinates**
: A cell's integer `(x, y)` in the world: x grows east, y grows south
  (`World.At(x, y)`, `Chunks.ChunkOf`). They never move, whatever the view
  does. `TerrainGrid`'s `(u, v)`, with v growing north, are the
  **terrain coordinates**, not cell coordinates.

**Chunk**
: 64 × 64 cells (96 m square), the unit the world is stored, simulated,
  spawned and sent in (`Chem.Chunk`, `Chunks.Size`). Chunk coordinates
  `(cx, cy)` are `floor(cell / 64)`.

**Chunk store**
: The world's map from chunk coordinates to chunks
  (`World.Chunks`). Nothing outside it assumes the world has a size.

**Active chunk / inactive chunk**
: An active chunk is simulated every tick. An inactive chunk is held
  exactly as it is: nothing in it changes, and its active neighbours' fire,
  water and gas stop at its edge. A chunk **activates** and
  **deactivates** (a `ChunkSwitch`). *Avoid:* awake, asleep, wake, sleep,
  paused or frozen (for chunks; frozen means ice, asleep means a resting
  physics body).

**The world**
: `Chem.World`: the cells, the chunk store and everything that ticks.
  A map's folder, `world/maps/<id>/`, is the **world data**: where things stand
  (`things.json`), the villages, the themes and looks. Older docs write
  `world/things.json` for the map's file; it's in the map's folder now.

**Game folders**
: Where the game finds its files (`Chem.GameFolders`): `rules/`, the map's
  folder (`World`, under `world/maps/` beside it), the shipped mods in `rules/mods`, the **player's data folder**
  (what an update leaves alone) and the player's mods in it; `Repo` is the
  checkout's root above `rules/`, for docs and tools. A build reads
  the copies packed into its StreamingAssets; the Editor, the MCP server
  and the tests walk up to the repo's. The view reaches them as
  `ChemGrid.Folders`. With no player (the MCP server), a saved mod goes to
  `rules/mods`.

**The grid in play**
: `ChemGrid.Current`: the view's grid that woke last and isn't destroyed,
  found without searching the scene. Its narrow face shows the `World`,
  the `Rules` in force and `Request(EffectCommand)` (the `ChemEffects`
  door); code that draws on the grid takes the grid itself from
  `Current.Grid`. Null before the grid's Awake, after it's destroyed and
  outside Play mode (EditMode tests, builders); a world reset keeps it.

**The valley**
: The hand-made start of the world: 9 × 9 chunks, 864 m, with the yard,
  the lake, the river, the falls, Hollowmere and the coast. Shaped by
  `ValleyShape` and baked in the Editor.

**World square**
: A valley-sized piece of the bigger world: 9 × 9 chunks, 576 × 576 cells (`squareCells`). The world is a grid of them,
  the valley one (`docs/design/world-squares.md`). Always "world square" in full: "square" alone is a spell shape.

**World plan**
: The coarse world, decided for every world square at load on every peer from the seed (`Chem.WorldPlan`): each one's
  band, square template and turn, the features crossing its sides, its coasts, its landmarks and the roads between them.
  A pure function of the seed and its inputs, with a **plan hash**. Chunks are made from it.

**Band**
: A world square's height class (`world/bands.json`): sea, low, hills, high, peaks. Touching world squares differ by
  at most one band. *Not* the dragon's band of fire.

**Square template**
: A curated kind of world square (`world/squares/<id>.json`): its bands, anchor biome, landmark intents and where they
  may go, what each side offers (a road, river, range or canyon at one of 9 slots, a must or a may) and whether it may
  face the sea, how a feature may end in it, and whether it turns. A **filler** is a band's template that fits anything.

**World sketch** *(paper only so far)*
: The suggestion a world plan is fitted to: biome shapes drawn freely over the world (a lake as a blob, a range as a
  long triangle), blended at their edges, saved as a small JSON. Each world square reads what the sketch puts under it
  and gets the band and square template that fit it best, within the plan's rules; where a rule wins, the plan says
  which. "Biome" keeps its meaning from `world/biomes.json` (`docs/design/world-squares.md`, "Draw first").

**Landmark intent**
: What a world square asks for, never where it goes (`world/landmarks/<id>.json`, `Chem.LandmarkIntent`): "a village
  with a landmark", with a form per biome. The plan picks its world square; the form comes from the biome there.

**Must-have**
: A far landmark a world must hold (a world file's `generated.musts`, `PlanInputs.Musts`: "volcano"). The plan gives
  one world square of the map, in a seeded order among those that can take it, a square template whose village comes
  out as that landmark, and keeps its village whatever the town roll; where no square can, the plan says why
  (`WorldPlan.MustMisses`) rather than force one.

**Known chunk / generated chunk** *(the generate switch is planned)*
: The two states a chunk is in before it's active, by distance from a character (`sight` 4 and `know` 7 in `sim.json`
  `streaming`): a **known** chunk is decided, not built (its dominant biome and its land every 8th cell, 9 × 9, for the
  far view: `Chem.KnownChunk`, `ChunkGen.Know`, held round the characters by `Chem.KnownRing`, which stops one chunk past
  the coast); a **generated** chunk has its cells, land and things made from the world plan (`Chem.ChunkGen`, a pure
  function of the plan and the chunk's coordinates), and is held inactive.

**Far landmark / seenFrom**
: A landmark the view draws far off, past the known ring (`Chem.FarLandmarks`): a plan landmark, outcrop or waystation
  whose type `world/horizon.json` names. Its **seenFrom** is how far off it shows, a radius in chunks (not a square),
  at least `know` + 2.

**World file / generated map**
: A **generated map** is made from a **world file**, its `map.json` with a `generated` section: the seed, its size in
  world squares, the generator's version and the world squares pinned to another map's folder (the nine pins the
  valley). Its other files are a **bake** (`GeneratedMap.Bake`), written beside it and never committed; its map hash is
  over its inputs, not the bake.

**Meet**
: Where a generated map's land comes round to a pinned map's own (the valley's) outside its rim: over at least 90 cells,
  wider (up to 240) where the rim stands far above or below the land beside it, reading the rim as a mean along it so the
  land is smooth both ways (`ChunkGen.MeetPinned`).

**Massif / grain**
: High ground in a generated map shaped as mountains, not one cap (`ChunkGen.Massif`): land more than 60 m up is
  ranges, the saddles between them and shoulders on their slopes. The **grain** is the way a world's ranges run, a
  direction from its seed. *Not* a biome's own relief.

**Spawn point**
: Where a player may start in a generated map, one per world square below the peaks (`WorldPlan.Spawns`, in the plan
  hash): the valley's is its own map's spawn, every other one a seeded spot beside the world square's road near its inn,
  village or landmark, clear of rivers, lakes, the shore and landmark sites. `ChunkGen` levels and clears a few cells
  round it (its **clearing**), and the bake writes it as a `spawn` place. *Not* a **spawner** (raiders, ambient life).

**Ground pack**
: The drawn detail of a map, `ground/` in its folder: heights, paint, trees and grass per chunk, and the river's channel (`GroundPack`,
  `docs/design/maps-as-data.md`). The valley's is what the game draws (`MapTerrains`, phase 6): the bake that exported it is retired. A map without one is drawn from its land and cells.

**Laid row**
: A row of a map's `buildings.json` with walls and no `built` cells: its cells are laid over `cells.txt` at the start, on
  every peer, and it's drawn from its row. A tree under it is gone, and `cells.txt` and `things.json` don't change.

**Level** (of a row)
: A row whose walls stand from the highest ground under it and run down to the lowest, so their tops are one height over
  ground that isn't. The foundations are the walls' own stone.

**Tower, road** (of a row)
: A tower is a round part of a row: the cells within its radius of a join, drawn as a drum a wall walk passes through. A road is cells of a ground
  terrain laid through a row's joins, past its walls.

**Wall walk**
: The top of a level row's walls, where a wizard can stand: reached by a **stair**, a part on a wall's inner side. The core has no idea of it: the
  cells are blocking, and a wizard up there stands on the view's colliders.

**The Hold, a run**
: The game on the far side of the valley (`docs/design/the-hold.md`). A **run** starts on the **plate** and ends when the **keep** falls; the score is the
  wave reached. The **heart** is the cell raiders go for, a **spawner** a place raiders come from, and **keep health** what replaces lives. **Mended**: a castle's cells put back when a run ends.

**Adjective** (of a raider)
: A row that changes a raider (Armoured, Burning, Soaked): `kind~adjective` is an entity made from the two, so its name, `weak` and `resist` reach everything that reads a raider.

**Blow, batter**
: What a raider does to a wall in its way: a terrain takes `hold.strength` blows, then breaks. Not a spell's damage.

**Swing**
: A hand on a gate: a command that turns a terrain into its `swing.into`, with the cells of that terrain joined to it. A **timber gate** swings; a machine's `gate` doesn't.

**Land**
: The valley's baked ground under each cell, in water units: the hills
  the cells themselves don't hold (a cell's elevation and height are on
  top of it). Baked once into the map's `land.json` (Prototype > Bake Valley
  Land) so every peer has the same integers; only the water reads it, so
  water runs down a hillside (`World.SetLand`).

**Soak, damp**
: Thin water on open ground **soaks** in (and dries up) a unit at a time,
  at its terrain's rate (`soakEvery`); ground it soaked into is **damp**
  for a while (`Cell.DampUntil`) and soaks more slowly, as in rain. Deep
  water (lakes, the river, the sea, a filled pit) doesn't soak.

**Shore map**
: Over the sea and out past the terrain, the way to the nearest land and
  the distance to the shore (`ShoreMap`, worked out from the map's land
  when the scene starts, `MapLand`). The surf's push (`WaterCurrents`) and the
  waves (`Ocean`, the Ocean shader) both run along it, so near the island
  they go toward the island. A view of the land, never the chemistry (D4).

**Sea, basin**
: A map's land below its sea level (`map.json`) is water: the **sea** where
  it reaches the map's edge, a **basin** (a lake's, a stamped hollow's)
  where it's walled off (`SeaMask`). The view draws them at the map's water
  level, 0.1 m under the sea level (`Ocean`, `MapLand`).

**The yard**
: The old 64 × 64 play area, now meadow in the middle of the valley
  (`ChemistryYard.unity` keeps the name).

**Continent**
: What the world is to become: one large, bounded land laid out by a
  theme, the valley at its start (`WORLD_STREAMING.md`, part 3).
  *Avoid:* endless world.

## Time and simulation

**Tick**
: One step of the world's simulation, 6 a second (`sim.json`).
  `World.Tick` counts them; `World.Step()` runs one. *Avoid:* "step" for a
  tick in prose.

**Height step**
: One unit of a cell's `Height`: 0.5 m raised or dug.

**Carrying**
: What a whirlwind has taken on and sheds as it travels (`carries` in
  `spells.json`): a fire tornado, a waterspout, a sandstorm, a smoke column
  or a snow flurry. One at a time per whirlwind.

**Eye**
: The middle of a whirling spell and the ring round it (`eye` in
  `spells.json`, in cells: Whirlwind's is 1). What it carries sheds there,
  and a creature there is lifted.

**Lift**
: A whirling spell holding a creature up in its eye (`lift` in
  `spells.json`): `ticks` held, then flung `fling` cells the way it was cast
  and hurt `hurt` times `fallDamage`. The creature's status is `lifted`.

**Shove**
: A charged cast's knock-back of a creature (`force.push` cells for each
  charge level and one more; `World.CastShove`). The second goes half as
  far; after `sim.json` `shove.most` the creature **braces** and doesn't
  move. Each staggers it `shove.staggerTicks`. Thrown along the ground, it
  goes over a drop of `fall.cliffSteps` height steps and takes
  `fallDamage` for every `fall.stepsPerHurt` it fell.

**Flash**
: A gas going up all at once when fire meets it (`burn.flash`: dust). The
  whole joined cloud is used up: the dust explosion.

**Hardness**
: How hard a terrain is to dig (`dig.hardness`). A dig's **strength** (a
  spell's, a crater's, a pick's) must reach it; hardness 0 never digs.

**Tick batch**
: Everything that happens to the world at one tick, in order: chunk
  switches, a rule switch, casts, effects, defense, combos, the dragon's summons, room commands (`TickBatch`). The host
  orders them; every peer applies them the same.

**Command / intent**
: A **command** is an input to the core (`CastCommand`, `EffectCommand`,
  `ChunkSwitch`, `RuleChange`, `RoomCommand`), the only way anything changes the world.
  An **intent** is the network message asking the host for one
  (`CastIntent`, `EffectIntent`, `ChunkIntent`).

**Command channel**
: One kind of command on its way over the network (`CommandChannel`, `NetChem`): the host's
  pending requests, stamping them for a tick, a peer holding them until that tick's batch arrives,
  and the history a late joiner replays, with a compaction hook that may drop what a later command
  makes needless. Defense commands, combo input and room commands travel on one each; casts,
  effects, chunk switches, strikes and the dragon still have their own lists.

**Deterministic / lockstep**
: Every peer runs the same world from the same commands and gets the same
  cells (D3, D7). Integer state, the world's seeded RNG, ordered iteration.

**Clock / day**
: The time of day read from the tick (`World.ClockTick`, which Hasten
  Time runs ahead; `Sky.Minutes`). *Avoid:* reading time of day from
  `World.Tick`.

**Daylight / night / dawn / dusk**
: The light the sky gives by the clock (`World.Daylight`, `sim.json`
  `day`): bright by day, dim at dawn and dusk, dark at night, which runs
  from the end of dusk to the start of dawn (`World.IsNight`).

**Light level**
: The SRD's light on a cell: bright, dim or darkness (`LightLevel`,
  `World.LightAt`): the daylight, or a fire's light if brighter. Worked out
  when asked, never kept.

## Chemistry

**Rules**
: What exists and how it reacts: `rules/*.json`, loaded into `Chem.Rules`.
  The single source of truth for the C# core (the frozen sandbox has its
  own copy, `sandbox/rules-frozen/`).

**Terrain / cover / gas**
: The three kinds of material a cell has: what it is (grass, stone, log
  wall, river), what lies on it (oil, ash, snow), what hangs over it
  (steam, smoke, fog) (`materials.json`).

**Liquid**
: What a cell's water is (P1, `Rules.Liquids.cs`): `Cell.Water` is the
  amount and `Cell.Liquid` the **kind**, an index into the `liquids` table
  where 0 is water, built in. A mod's honey, paint or molten rock is a row
  there (`thick`, `heap`, `touch`, `hot`, `freezes`, `sets` ...): a hot one
  lights what it touches, then cools; one that **sets** hardens into a
  terrain where it lies (`Cell.Sets` counts down). Two kinds never share a cell. The
  index is the core's own; the wire, saves and the cell hash carry the id.
  "Water" in a rule still means kind 0.

**Element**
: What a spell or reaction brings to a cell: fire, cold, water, air,
  earth, lightning, thunder, grease, oil, fog, growth. *Not* the spell
  radial's top ring (that's a **group**).

**Focus** *(planned)*
: An item a wizard carries to cast through, such as a staff. Not built: the
  default wizard's hands are free, and `WizardOutfit.carriesStaff` keeps the old
  staff's code, unused, for it.

**Reaction**
: A rule that runs when an element arrives at a cell, in order, until
  one says `stop` (`World.React`, `reactions.json`).

**Electric charge**
: A cell's `Charge` (0 to 3, decaying), which lightning leaves and
  conductors pass on. Say "charge" alone only when it's obviously this.

**Machine / gate**
: A machine is a terrain that something powers (a boiler by steam, a
  plate by weight, a wheel by flowing water, a coil by charge, a windmill
  by wind; `machines.json`). Any machine powered opens every **gate**
  (`World.Machines`).

**Puzzle**
: A block of cells laid into the valley when the world is built, from
  `world/puzzles.json` (`WorldPuzzles`): the meadow coil is a pond, a
  coil on its edge and a gate on a path. Solved through the rules, never
  by a script of its own.

**Veil (thin fog)**
: Fog weather lays fog gas here and there, in patches on low ground near
  water (`weather.fog`, `World.Veil`), a unit a cell. That's too thin to
  hide anything on its own, but a line of sight through a gas's `hidesAfter`
  cells of it is hidden (`World.LineOfSight`). The patches drift a little
  on the stretch's breeze and thin away when the stretch ends.

**Flame**
: One of the few flames on a burning cell (`FlameBank`, `Prototype/Flame`): a
  quad playing the Particle Pack's flame flipbooks, by the fire's level, in the
  look of what burns (`FireLook`), scattered about the cell at its own size,
  speed and time, drawn on the GPU. View only: what burns is the core's.

**Live cell / settled chunk**
: A cell is **live** while something in it is still changing on its own:
  fire burning, water flowing, gas drifting, charge decaying, something
  ripening or withering. A chunk is **settled** when none of its cells
  have been live for `sim.json streaming.settleTicks` (12 ticks, two seconds).

## Spells and casting

**Spell**
: An element, a shape and a power (`spells.json`, `SpellDef`).

**Shape**
: Where a spell lands: point, line, square, burst, cone and so on.
  ("Square" survives only here and in "village square".)

**Cast**
: A wizard using a spell at a cell and direction (`CastCommand`).

**Action**
: What a spell or a trigger (and later a world edit) does: `cast` (a spell at a cell) or `place`
  (`docs/design/events-and-placing.md`).

**Trigger / event**
: A **trigger** is a `rules/triggers.json` row (`TriggerDef`, `Rules.Triggers.cs`) that reacts to an **event** with
  one or two actions, fired inside the command that raised the event (`World.Triggers.cs`). The base rules have none;
  a mod adds them (`add_trigger`). The events are a **step**, a golf ball **holed**, a **called** named act, and a
  **tick** (a shower).

**Step**
: A player's stand moving to another cell: a `Stand` command whose cell differs from the one the core has, not the
  first stand and not while downed. The event a trigger on `step` reacts to.

**Shower**
: A trigger on `tick`: as a tick begins, every `every` ticks (12 at least, 2 seconds), its actions fall round each
  standing player it's for, until its mod is reverted ("constant meteors"). Capped by that floor and the room's
  `triggers.maxPerTick`.

**Place / placed thing**
: A spell's `place` field: a thing, an animal, a terrain or a cover put on cells picked by `Placement.Pick`
  from the world's seed. A **placed thing** is core state (`World.Placed`, in `ThingsNow`) and lapses after `lasts`.

**Pick**
: Choosing cells over an area the same on every peer (`Placement.Pick`): each open cell scored by `WorldHash`
  of the seed, a salt and the cell, best first, kept `spacing` cells apart. No draw from the world's generator.

**Lay / scatter**
: **Lay** is a mod's world edit setting cells (`world.lay`, `World.LayCells`): fresh at the switch, taken back by a
  revert. **Scatter** is the MCP tool that picks those cells over an area by `Pick` and writes them into a draft.

**Summon**
: An animal a spell's `place` brings out (`World.Summoned`): the core says which species, on which cell and with
  which Individual; each peer's `ValleyFauna` then lets it live as ambient life, on its own.

**Cast charge**
: How long a cast was held, 1 to 3 (`CastCommand.Charge`, counted from the meter by `World.HeldCharge`).

**Beat**
: A stage of a cast by elements on the wizard's body: first element, second element, wind-up (an uncharged cast only), slam, charge,
  release, recovery, or a fizzle when it's dropped (`Beat`, `CastBeats`; poses and timings in
  `Settings/CastFeel.asset`). View only, worked out from the core's cast state on every peer.

**Voice**
: One sound playing, from the pool of sources in `SoundVoices`. A loop must be held each frame or it lets go.

**Hit-stop**
: A few frames in which the local wizard's Animator and beats and the camera stand still, on the slam and the
  release (`CastBeats.HitStop`, `CastImpact`). View only and on the caster's own screen: game time, ticks and
  physics run on, and other peers never see it. *Avoid:* "freeze" (a word of the chemistry), "hitch".

**Downed**
: A wizard at 0 health, or caught by the dragon's breath: on the ground with no controls for `sim.json`
`downed.seconds`, then back at the yard with full health (`Downed`, `WizardDown`), or on its last checkpoint
inside the Gauntlet. Not the world reset. In the core, a raider the breath catches has the `downed` status for
that long, and doesn't march.

**Unarmed strike / punch**
: The SRD 5.2.1 unarmed strike, in the core (`StrikeCommand`, `World.Strike.cs`, sim.json `strike`): a wizard
  punches along its aim; the nearest wizard or raider within `reach` cells in front takes an **attack roll**
  (d20 from the world's generator + Strength modifier + proficiency bonus, against its **AC**, entities.json
  `str` and `ac`) and on a hit 1 + Strength modifier bludgeoning damage and a **knock-back** of `knockBack`
  cells. Log: "Ana punches Bo: 1 bludgeoning damage", "Ana's punch misses Bo". Say "strike" alone only where
  it can't mean lightning; the view's shove of props is still "the punch" (`ItemHolder.Punch`).

**Shunt**
: A short move through the character controller to a point, for a strike's knock-back (`Knockback.Shunt`), so
  what stands in the way stops it.

**Stand**
: Where the core knows a wizard is and its health as its peer last said (`WizardStand`, a `StrikeCommand.Stand`),
  so strikes resolve on the core's cells. Health stays the wizard's own (`Vitals`); the stand only mirrors it.

**Sub menu**
: A page of the Esc menu that the main page lists: Game, Multiplayer,
  Settings, Claude or Developer. A part of the game adds its entries to one, so the main
  page stays short. Not a **group**, which is the radial's.

**Group**
: A ring-1 slice of the spell radial (fire, water, storm, earth, arcane,
  nature, time), at most three spells each (`groups.json`, `GroupDef`).
  Players see them as elements; we say group.

**Spell list**
: What Tab opens on the keyboard, in place of the radial: every spell by
  element, then all spells, then the ones not on the wheel (`SpellList`).

**Force**
: What a spell does to what it hits, apart from its element (`spells.json`
  `force`): **push** knocks it along, **throw** throws it off its feet (the
  golf ball: up and along). Geyser, Wave, Rolling Boulder, Gust of Wind and
  Raise Earth have it.

**Aim marker**
: What shows on the ground where a cast will land (`AimMarker.cs`), one
  mesh of ribbons laid over the ground and water. Its kind comes from the
  spell's shape: a **lane** (an arrow along a line), a **wedge** (a cone), a
  **ring** (a burst that strikes at once, or a spot; grows with the charge),
  **rising** (an outline with a short fence: earth shaping, Geyser), an
  **area** (dashes marching round a patch that settles and stays, like
  Spike Growth) or a **lock** (brackets round one target).

**Spell golf**
: One hole played with spells (`World.Golf.cs`, `golf.ts`, BACKLOG §24).
  The **ball** (core state, not a thing) starts on the **tee**; a cast
  whose force moves it is a **stroke**; it drops in on the **hole** cell.
  Lost in water or out of the course's bounds, it goes back on the tee for
  a stroke's penalty. The course is `world/golf.json`; an act can place a ball and a cup elsewhere.

**Spell zone**
: A block of cells that allows only some elements (a spell is allowed when a
  combo of only those elements casts it) or some spells by id
  (`SpellZone`, `World.Course.cs`, `course.ts`). A cast landing in it with
  anything else **fizzles**: "The lava pit's charm smothers your Fireball".
  World data, the same on every peer, and in the checksum.

**The Gauntlet**
: The obstacle course on the lake's south shore (`world/gauntlet.json`,
  BACKLOG §25): a **gate**, **booths** in a row (each a spell zone with a
  **checkpoint** cell) and a **bell**. The gate starts a player's **run**
  (its clock), a booth further on is the checkpoint reached, the bell stops
  the clock and keeps the **best time**. Downed inside the course on a run,
  a wizard gets up on its last checkpoint, not the yard (`CourseRespawn`).

**Look** (of a material, spell, element or thing)
: Its `"look"` group in `materials.json`, `spells.json` or `elements.json`, or on
  a mod's new `entities` or `items` row (family `creature`: `species`, colour, glow
  and scale, for a kind with no baked prefab):
  the family that draws it and that family's parameters (colour, height, glow,
  roughness, flow). An element's, and a cover's or gas's until a family of
  theirs is drawn, is **bare**: no family, only a colour. An element's is the
  colour of what it makes in the world, whichever spell or reaction made it.
  The view's only: never in a tick, the cell hash or the rules
  fingerprint. *Not* a world theme's look (`world/looks.json`, a biome a theme
  may paint), nor a fire's `burn.look`.

**Mix** (of materials)
: A material the room derives when two liquid or cover rows of one `mix` group
  meet, with no reaction row: red and blue paint make `blue-paint+red-paint`.
  Held in the room's mix list (`Room.Mixes`). See `docs/RULES_MCP.md` "Mix".

**Mix** (of a look)
: Several colours with weights in place of a look's one colour (`"mix"`), each
  cell picking one by a hash of where it is (`LookScopes.CellHash`), never the
  world's RNG: fire in three colours, the same patchwork on every peer.

**Scoped look**
: A colour or a mix that holds only round a place, a cell or a player
  (`"scoped"`: `where` and `color` or `mix`), on an element, cover, gas or
  terrain. Where several cover a cell they mix like paint (**colour
  mixing**, `Colors.Mix`: blue and yellow give green), its edge blends over three cells,
  and elsewhere the plain look holds. Worked out when drawn
  (`CellLook.At`), not stored on the cell, so a fire leaving the scope changes
  back. A spell's own colour still wins (J-052). *Not* a theme's look.

**Look by player**
: A scoped look with `by` in place of `where`: a colour or mix for what one
  player's casts make (`"by": 2`, a slot), or one colour per player (`"by":
  "each"` with a mix). Strongest of all: it wins over a spell's own colour, a
  `where` and the plain look. A **slot** is a player's number in join order (0
  the host), kept while they stay.

**Look family**
: A way the view can draw a material, by name, from data alone: `block`,
  `plant`, `debris`, `plate`... listed in `rules/looks.json`. A new material
  names one, so it draws with no code and no asset. `drawn: false` means named
  but not yet read from data, and a mod can't use it.

**Form**
: The variant a look family draws: `plant` has `bush`, `tree`, `sapling` and
  `stump`.

**Effect family**
: The same for a cast's show (`blast`, `bolt`, `beam`, `motes`...): prefabs and
  how the show scales with the spell's numbers. One show per family, in
  `SpellVisuals.asset`, built from `rules/looks.json`. A family's **own colour**
  (`own` there) is the colour its effects are drawn in.

**Look tint**
: How far a spell's effects are turned round the colour wheel, from their own
  colour to the colour of the spell's look (`LookTint`): the orange blast of a
  blue Fireball turned to blue, its white heart still white and its smoke still
  grey. A look of nearly the same hue, or a pale one, turns nothing, and the
  effect is drawn exactly as authored. Only the view has it: nothing in the
  cells knows what colour a fire is. *Not* a **theme look**, nor the plain
  tints of `BurstFx` and `FireLook`, which colour by what burns.

**Chosen colour**
: A spell's own colour, given by a mod: its `look.color` moved from the base
  rules' to one that isn't pale. Its effects, and the fire it lights, keep it
  whatever its element's colour is (J-052). A spell with no chosen colour
  follows its element, the one its `element` names, once a mod moves that
  (`SpellVisuals.Entry.ChoseColour`). `set_look` on an element's plain colour
  moves its spells' with it.

## Things in the world

**Thing**
: Anything listed in `world/things.json`: a prop or an item, with a
  stable **thing id** (`ThingId`: its chunk and its place in that chunk's
  list, `4,5#2`). Append to a chunk's list, never insert.

**Dressed thing**
: A thing of a kind a mod added, which has no prefab of its own: a generic
  prefab (`dressed-prop`, `dressed-item`) wearing the body of a fauna species
  from its look (`ThingBodies`).

**Prop**
: A thing you push and break but don't carry: a crate, barrel, boulder,
  the court's ball (`PhysicsProp`, `PropState`, `NetProp`).
  **Generated props** (palms, benches, loungers) are scenery built from a
  seed (`GeneratedProp`), not things.

**Item**
: A thing you carry: a lantern, a spyglass, sending stones
  (`items.json`, `Carryable`, `NetItem`).

**Entity**
: The rules' word for anything standing on cells that reactions can act
  on: wizards, creatures, raiders and props (`entities.json`, `EntityDef`).

**Component**
: What an entity or item is made of or does, as data: `flammable`,
  `creature`, `conducts`, `floats`, `breakable`, `heavy`, `brittle`
  (`components.json`, `ComponentSet`). Entity rules match on components
  (`if.has`, `if.lacks`), never on ids. The `creature` component means
  alive in the rules (moves, takes statuses, is hurt); it isn't the view's
  creatures below.

**Wizard**
: A player's character (the solo wizard, the `NetWizard` prefab).

**Player stats**
: A player's **speed**, **size** and **jump** (each times the wizard's own) and **jumps** (how many before landing:
  2 is the double jump), as rules: `sim.json` `player` for everyone, `player.by.<slot>` for one player (the slot of
  a look by player), within `budgets.json`. Changed by `set_player`; the core keeps them for the view, which moves
  and draws its own wizard by them (`PlayerStats.cs`).

**Character** *(proposed, for the chunk work)*
: Anything whose presence keeps the world around it active: today every
  wizard; later perhaps raiders or villagers that matter to play.

**Worker**
: One of the folk with a job (a map's `jobs.json`): stops it goes round, walked by the quickest way on foot. View only.

**Raider**
: A core entity in the defense field's waves (`Raider`, BACKLOG §17).

**The Hold**
: The wave game on the hill west of Hollowmere (`docs/design/the-hold.md`, #152): raiders climb to a castle in endless waves. Not built yet.

**Adjective**
: A row that changes a raider in the Hold: Armoured, Burning, Soaked. Its numbers, weaknesses, and a status or cover it leaves.

**Boss**
: A raider a wave brings every so many waves: a kind with the boss adjective's bigger row, its own name and health bar (`rules/hold.json` `boss`).

**Wave theme**
: A row of `rules/hold.json` `themes` that leans a Hold wave on one or two adjectives, so no one hand set wins everything. Not a world's **theme**.

**Wave warning**
: What the core says of the next Hold wave while it waits: its number, theme, boss and size, long enough ahead to reach the keep and attune. Not the dragon's **warning**.

**Barricade**
: A small wooden block on the Hold's road, appearing at the start of a wave. Raiders break through it, and it burns.

**Upgrade**
: A row of `rules/hold.json` `upgrades` a wizard picks between waves, changing a number it already has (a cooldown, a gesture's damage or range, its health). It ends with the run.

**Unlock**
: A row of `rules/hold.json` `unlocks`, in order: a gesture, the second hand, an element or a kit spell a wizard keeps between runs. What's unlocked comes from the wizard's **points**.

**Points**
: What a player earns by waves survived, added up across runs, and all their save holds of the Hold (J-038). They travel with the player, not the world.

**Moment**
: A canned setup for playtesting the Hold: a rules row naming a wave, hand sets and the keep's state, to jump straight to in dev builds and the Editor.

**Spawner**
: A row naming where a Hold's raiders enter, which kinds and how fast.

**Hand**
: One of a wizard's two slots in the Hold: an **element** of the combo table, or nothing. Its **gesture** says what comes out.

**Gesture**
: What a hand's buttons do in the Hold: thrust (trigger, short range), throw (bumper, long range), sweep (both, area). The same gesture on both hands makes something new; different gestures each do their own. Not `CastGestures`, the view's animation of a cast.

**Tap**
: A hand's button let go within `hands.tapTicks` of its press: it goes off at once, at charge 1, and rests `tapCooldown`. Held longer, the button charges instead.

**Range**
: How far from the wizard a hand's spell can land, in cells: short or long, by its gesture (`rules/hold.json`).

**Attune**
: To change a hand's element, in the castle keep's attuning room only.

**Attuning room**
: The room of the keep where a hand is attuned, named by two of the row's joins in the map's `hold.json`.

**Merge**
: Two hands' spells becoming one: both hands charging the same gesture, with a row in the hand table for their two elements. Their icons slam into the pair's. D16, `docs/design/unified-casting.md`; not built yet.

**Hand spell**
: A spell in the table of `rules/hold.json` `hands`: by what a hand holds and its gesture, or by what both hold when both make the same gesture. An empty hand's row is an action instead (a punch, a throw, a shove).

**Cooldown**
: The ticks after a hand spell goes off before that hand casts again: tiny after a **tap**, longer after a charge; a two-hand spell locks both, for longer. A row may have its own, longer one: Meteor is big and rare.

**Kit**
: The spells every wizard has in the Hold, on the D-pad, whatever the hands hold.

**Creature / fauna**
: Animals and folk drawn in the view (`ValleyFauna`, Punchy's creatures).
  View-only, never in the rules.

**Mill about**
: What a frightened animal does when it's **cornered**, with nowhere left to run
  (a wall, water or an edge on every side, or the end of its leash): a few steps
  at a time, an eye on the danger, until a way out opens or the danger goes
  (`CreatureBrain.Mill`). *Not* standing frozen or shaking on the spot.

**Ambient life**
: View-only life that makes the world feel lived in: gulls, fish, crabs,
  fireflies, villagers (`AmbientLife`, `AmbientCritters`, D12).

*Retired:* **critter**. It meant four things (the sandbox's emitters, the
  collectible world-changers of `TRAVERSAL.md`, raiders, and ambient
  life). Say which one.
  A "critter spawner" is a **spawner**.

**Building** *(proposed, `docs/design/world-tools.md`)*
: A cabin or a village building: wall cells in the core, a mesh in the view.
  Its **building id** is `cabin#2` or the village and its place in the plan
  (`Hollowmere#4`).

**Place**
: A named landmark with its cells (the lake, the falls, Hollowmere), in
  `world/places.json` (`WorldPlaces`), so "the house by the lake" can be
  found. Most are **generated** from the world files (villages, courses,
  puzzles, `land.json`'s water); the rest are written by hand. Read-only: mods
  can't change them. The MCP's `places()` lists them and `here()` says which
  one the asking player is in and what's near.

**Graft** and **donor**
: A **graft** is a species row built from other species' slots by `from` (a centaur, a faun); each
  species it takes slots from is a **donor**. The core flattens it into plain slots when it's read
  (`FaunaGraft.cs`, `docs/design/bodies-and-surfaces.md` §2).

**Individual**
: One animal's fixed number, set when it comes out: it picks that animal's lean, arm swing, bounce, pace,
  hairstyle and hair colour (`CreatureState.Individual`). Local to each peer, like the animal.

**Slot**
: A part on a species: a row in its `parts`, by a name of the species' own, saying which
  shape from `rules/parts.json` goes where, and what it hangs from (`Fauna.cs`).

**Spawner**
: A species row's `spawn` in `rules/fauna.json`: how thick it is in the wilds, and whether only
  near water. Ambient life, so view only: each peer rolls its own animals
  (`docs/design/world-tools.md`, part 2).

**Species**
: A kind of animal or folk: a row in `rules/fauna.json`, the view's, built into one
  `CreatureBody` that all its animals share. A **variant** is a row that takes another's
  body and gives its own colours and size.

**Stretch**
: The length of a wall segment on one cell, the part that falls when that cell goes (`RowMesher`, `RowRuin`).

**Row house**
: A building drawn from its row (`RowHouses`, `RowMesher`), because a mod added
  or edited it. What the world was made with is drawn by the valley instead.

## Chunk streaming

**Reach**
: The chunks around a character that are kept active: within a few
  chunks of where it stands. Reaches overlap; their union is the
  **active region**.

**Active region**
: Every chunk that's active this tick: the union of all characters'
  reaches, plus whatever the chunk work decides to keep active beyond it
  (`WORLD_STREAMING.md`).

**Reach and keep distances**
: `sim.json` `streaming`: a chunk activates when a character is within
  `reach` (2) chunks and deactivates once none is within `keep` (3), so
  walking along a chunk edge doesn't flicker it (`ChunkStreaming`).

**Linger**
: A chunk **lingers** when it stays active out of every keep distance
  because it isn't settled yet (a fire left burning). Live cells on an
  active chunk's edge activate the inactive chunk across it, which
  lingers too: that's **spread**. Capped by `streaming.lingerChunks`
  (16, nearest first) and `lingerTicks` (1800, five minutes); a chunk the
  caps let go while live is **cut off**, and stays inactive until a
  character's reach comes back to it (`ChunkPlanner`). Rain alone
  doesn't keep a chunk live.

**Chunk switch**
: The command that activates or deactivates a chunk, stamped into tick
  batches online so every peer switches the same chunk at the same tick
  (`ChunkSwitch`, `ChunkIntent`).

**Catch up** (also: fast-forward)
: A chunk that deactivated while still live (the linger caps let it go)
  runs the ticks it missed when it next activates, alone, with walls for
  neighbours and each tick's own weather, up to `streaming.catchUpTicks`
  (720, two minutes) (`World.CatchUp`). The chunk switch is the command:
  every peer catches it up the same.

**Generated chunk / diff** *(planned)*
: A chunk made from the seed the first time anyone comes near
  (`ChunkGen`), and what's saved of it: only what players changed.

## Multiplayer

**Peer**
: Any player's copy of the game in a session. *Avoid:* "machine" for a
  peer (machines are the boilers and wheels of `machines.json`).

**Host / client**
: The host is the peer that orders tick batches, owns the clock and keeps
  the props (D7). Every other peer is a client. "Server" means only a
  future dedicated server.

**Late join**
: A peer joining a running session: today it replays every tick batch
  from tick 0 (`Timeline`); per-chunk snapshots are planned.

**Room**
: A session as its peers share it: one world, its host, its change mode
  and the mods on in it (`World.Room`, `World.Room.cs`). Solo play is a
  room of one. Made with the world and saved with it.

**Change mode**
: How a room takes live rule changes, chosen when it's made (RULES_MCP.md):
  **Open** (anyone's change goes in), **Host only** (only the host's) or
  **Locked** (none: the rules stay as the room started). Written "Open
  room", "Host-only room", "Locked room" in the log.

**World edit** *(proposed, `docs/design/world-tools.md`)*
: One change to world data in a mod's `world` section: set a field of a
  building or thing by id, or add a thing (appended to its chunk's list, so
  ids don't move). Look fields are the view's, out of the fingerprint.

**Connect token** *(proposed)*
: A secret the game writes into the MCP config it registers with a
  player's Claude; a local client without it is refused.

**Mod / active mod / switch on, revert**
: A **mod** is a JSON patch over `rules/` (`docs/MODDING.md`). A room's
  **active mods** are the ones on, in order, each with its title, the peer
  who added it and the tick (`RoomMod`). A `RoomCommand` **switches one
  on** (MCP: `apply`) or **reverts** it (takes it off; the rest are
  composed again in order). Every peer checks a mod itself
  (`ModValidator`) before switching it on; one that fails is **refused**
  everywhere, with a sentence. *Avoid:* "install", "enable".

**Temporary material / lapse / retired**
: A **temporary material** is a terrain, cover or gas a mod adds: it lives
  while its mod is on. Another mod may name it while that mod is on, and is
  then its **dependent**: it goes when its owner goes (#86). The same holds for
  anything a mod adds, a style or other building table's row, a building row, a
  species, a part, a trigger (`ModAdds`, #88). It **lapses** when its
  row's `lapse.after` ticks have passed since the switch, which takes its mod
  off; lapsed or reverted, every cell of it is **swept** at that tick (into its
  `lapse.into`, or nothing). The room's **retired** lists remember what went
  and whose it was (`Retired`, `RetiredRows`; `World.Lapse.cs`, `docs/RULES_MCP.md`). *Avoid:* "expire", "despawn".

**Shipped mod / player's mod**
: A **shipped mod** is in `rules/mods/`, and comes with the build. A **player's
  mod** is one they saved, in their data folder beside `notes.md`
  (`claude-terminal/mods`, `GameFolders.Mods`). The player's wins an id both have. Either reaches a
  room the same way, as its id and JSON (`docs/MODDING.md`, "Where mods live").

**Checksum / hash / fingerprint / golden**
: The **checksum** is what peers compare every 30 ticks (cells, rules,
  things, defense). A **cell hash** (`CellHash`, `ChunkHash`) is FNV-1a
  over cells; a **fingerprint** hashes the rules or the things; a
  **golden** is a hash a test pins. `WorldHash` is unrelated (the world
  generator's coordinate hashing).

**Game mode / free play**
: A **game mode** is a snapshot: copies of a set of mods in their order, and maybe a map, packaged and shared the way a mod is. In a
  mode you edit only that mode's copies. **Free play** is where you load any mods and snapshot the setup into a mode. Not built
  (backlog 28). Never "sandbox", which is the frozen TS prototype.

## The Dragon

**Dragon**
: The world reset as an event (BACKLOG §26, `World.Dragon.cs`, `sandbox/src/dragon.ts`). Summoned by a
  `DragonCommand` (the Reset, the host alone online), or by itself past the changed-cell budget, if a mod sets one. Its path
  comes from the seed and the flight's number, never from the world's random numbers. How big it's drawn is
  `dragon.scale` (against a 70 m wingspan): the view's alone, and the breath reaches no further for it.

**Flight**
: One visit of the dragon, in three phases (`DragonPhase`): the **warning**, then the **passes**, then
  **leaving**. `World.DragonFlights` counts them.

**Warning**
: The ticks before the dive (`dragon.warningTicks`, about 30 s), for the view to darken the sky. As it
  ends, the dive snuffs every fire in the world, or a wildfire would walk back over reset chunks.

**Pass**
: One long sweep of the dragon along a **strip** of chunks (`dragon.breathChunks` across, `dragon.overlapChunks`
  of them swept again by the next pass), there and back across the valley. Between passes it wheels
  round (`dragon.turnTicks`).

**Stream** (of fire)
: The fire falling from the dragon's mouth to its wall, drawn as **drops** (`DragonBreath`, Prototype/Flame):
  GPU particles with no state, on only while the breath is out. What has left the mouth flies on when it stops.
  It's the show; what the breath does is `World.BreathBox`.

**Barrel roll**
: A whole turn of the dragon about its length on the way round between passes, on about two turns in three, by
  the flight's hash so every peer sees the same (`DragonFlight`). View only.

**Dragon's breath**
: The band of cells under the fire on a pass, `dragon.depth` cells long and a strip wide, moving
  `dragon.speed` cells a tick (`World.BreathBox`). Say the whole name: *breath* alone is a swimmer's
  (`Breath.cs`).

**Leaving**
: What's left of a flight after the last pass (`dragon.leavingTicks`): it lands, roars and goes.

**Perch**
: Where the dragon lands as it leaves: the highest ground in the valley (`DragonView`). View only.

**Menace**
: How far the dragon has darkened the sky, 0 to 1 (`SkyLight.Menace`, from `DragonFlight.Darkness`). View only:
  it comes from the flight, so it's the same on every machine.

**The Reset / rebuild**
: The **Reset** (R, View on a pad) calls the dragon. Held for 3 s it's a **rebuild**, the valley rebuilt at once
  with no dragon (`EventLog.RebuildYard`): the way out of a world gone wrong.

**Starting state**
: The world as built, kept by `World.KeepStart` before the first tick: a palette of the distinct cells
  and each cell's index in it, with the ball as built. A world with none has no dragon.

**Fresh**
: A chunk the dragon's breath has reached: back in its starting state, active or not (`ChunkFreshened`).

**Changed cells**
: Cells that differ from the starting state in what lasts: ground, height and cover. What goes by itself
  doesn't count: fire, gas, charge, timers, water, ice, snow, mud that dries. **The audit** keeps the
  count (`World.ChangedCount`): each tick it compares one active chunk with the starting state. Past
  `dragon.budget`, when a mod sets one, the warning starts (0, the default, is never: J-073).

**Sheltered**
: Out of the dragon's breath though under it (`World.ShelteredFromBreath`): in water at least
  `dragon.shelterWater` units deep, not frozen, or with a blocking cell of `dragon.wallTerrains`
  (a stone wall, Wall of Stone's raised rock) within `dragon.wallReach` cells straight toward where the
  dragon flies from.

## World generation

**Theme / blueprint**
: A **theme** is JSON goals for the land (`world/themes/`); solved for a
  seed it's a **blueprint** of zones, mountains, lakes and rivers (D11).

**Named act**
: A one-off defined once and fired again by name at a place: a trigger with `on: "called"` in a mod, its rows what `act` takes, fired by `call_act` as one firing (`docs/design/world-acts.md`).

**Land stamp**
: One integer change to land: a centre, radius, profile, rise and seed. How `act` moves the ground, and how a map will be
  shaped (`World.Land.cs`, `docs/design/maps-as-data.md`). A mountain is a stamp, never a tool of its own. Its **profile** is its shape; a **canyon** and a **cliff** are the two with a **face**, a steep wall of rock that wanders along a line, or along a **path** of points so it bends. What's on a face (`LandRocks`) is only drawn. A **map stamp** is one of a map's own, in its `stamps.json` (`MapStamps`): laid when the world is made, and no undo or reset takes it.

**Map**
: A whole world to play on, a folder under `world/maps/`. Its **map hash** is over every file there (a generated map's, over its inputs) (`MapIdentity`): the host turns away a peer without its id and hash. One build still draws one, the valley; loading another is future work (`docs/design/second-map-2026-11-02.md`). *Not* the
  **cell map** in the map's `cells.txt`, one character a cell, nor a **shore map**.

**Theme look**
: Which of Punchy's lands paints a place (`world/looks.json`). Not a
  spell's visuals and not a wizard's appearance.

**Recipe / seed / builder**
: Generated content (D12): a recipe in data, a seed, and a builder that
  makes it from shapes (villages, buildings, props, creatures).

## Input

**Pad log**
: The `Input device ...` lines in Player.log (`PadLog`): one for each input
  device at start, on each change, and when the pad in use moves. Each says
  whether the device is a `Gamepad` or a `Joystick` and what `Gamepad.current`
  is, because the game reads only those: a controller that arrives as a
  `Joystick` reaches no `<Gamepad>` binding.

**Hold to talk**
: Holding H, or both sticks in on a pad: the chat box opens and fills with
  the player's words, and letting go sends them to Claude (`GameVoice`).

**Recogniser**
: What turns speech into text. The game knows only the port, `ISpeech`;
  each recogniser is an adapter behind it, picked in `SpeechChoice` (J-061).

**Transcriber**
: A recogniser's engine that turns a whole recording into text after the
  hold, on its own thread (`ITranscriber`): Whisper's kind, as Windows
  dictation hears as you talk. `TranscribedSpeech` is the adapter that
  records, hands the samples over and waits no longer than its **speech
  timeout**; past it the hold ends with a line and the next press works.
  The **stub transcriber** (`StubTranscriber`) stands in for a model with
  fixed text after a set delay; only an Editor or a development build can
  pick it.

**Speech audio**
: Where a transcriber's recording comes from (`ISpeechAudio`): the default
  microphone or a WAV file, always handed over as 16 kHz mono floats.

**Speech check**
: `-speech-check`: one recorded hold through the chosen transcriber, with
  the text and its timings and the frame time while transcribing
  (`SpeechCheck`, `docs/design/speech-check.md`). Not `-voice-check`, which
  checks Windows dictation.

## Claude in the chat

**chatbench**
: Fixed prompts through the chat's arguments with no game, so it stops at
  `applied` (`core/Chat.Bench`). Cheap to run on any machine.

**chatprompts**
: The probe that sends a fixed set of prompts through the running game's chat,
  so a record has a real "in the world". A stand-in answers unless it's given
  `--real-claude true`, which spends real prompts (J-064).

**Act**
: Something that happens once, now, for everyone, with no mod: `act` lays ground, places a
  thing, brings out an animal or casts a spell (`World.Acts.cs`). A mod is a rule that keeps
  holding; an act is an event. `docs/design/world-acts.md`.

**Chat profile**
: How the game reaches Claude, picked in Settings > Claude setup (`ChatProfile`,
  `ChatSetup`): the player's API key, or their own Claude Code by path with its
  environment variables. Each is a row, so another harness is one more.

**Harness**
: What answers a chat message, behind `IChatHarness`: Claude Code by way of
  `ClaudeChat`, or the **API loop** with a player's own key. A **chat profile**
  picks between them.

**Chat records**
: `chat-log-<date>.jsonl`, one line per prompt, with the prompt in it, in
  `chat-records/` under the player's data folder, so a bug report's `Player.log`
  doesn't send them. A test's stay beside its own log.

**Claude folder**
: The chat's folder (`Chem.ClaudeFolder`): `claude-terminal` in the player's
  data folder, where the chat and the terminal run claude, with its
  `.mcp.json`, `CLAUDE.md`, `notes.md` and the player's mods. A probe points
  it at a folder of its own (`Test`).

**Notes**
: `notes.md`, in the chat's folder: what carries over between messages, as each
  is a new process. A model writes them. The game puts them ahead of the player's
  message, quoted line by line, as a record and not instructions.

**Model unavailable**
: When the model the chat asked for isn't on the account, or doesn't exist: the
  message fails with no second run, a red line names the model and says where to
  change it, and the record's `why` says so (#64). There's no fallback to another model.

**API loop**
: The game's own tool loop over the Claude Messages API (`ApiLoop`): it connects
  to the game's MCP server as a client and offers its tools and `Instructions`
  unchanged. It runs only tools named in `ApiLoop.Allowed`, plus `add_note`, which
  adds one line to `notes.md`. Only `chatprompts --harness api` reaches it.

**Reply**
: One answer from the model, one `message.id` in the stream, however many tool
  calls it holds. Not a **turn**, which is what `num_turns` counts: tool calls + 1.

**Opening tokens**
: What a prompt reads or writes before Claude's first reply, about 14,000 to
  16,500: the tools' definitions in the cached block, then what every prompt
  writes again (`docs/telemetry/chat-context-2026-11-02.md`). *Not* an
  **opening**, which is a door or window in a wall.

**Cold prompt**
: One with nothing cached to read, so it writes all of Claude's starting
  context. A **warm** one reads most of it.

## Ambiguities settled

| Word | Means | Not |
|---|---|---|
| square | a spell shape; a village square | a cell |
| step | a height step (0.5 m) | a tick |
| strike | the unarmed strike (a punch) | a lightning strike (say "lightning strike") |
| element | a chemistry element | a radial group |
| charge | a cell's electric charge | a cast charge (say "cast charge") |
| life | a cell's `Life` timer | ambient life; the defense field's lives |
| look | a theme look | spell visuals; a wizard's appearance |
| frozen | ice (`Cell.Frozen`) | an inactive chunk |
| asleep | a resting physics body | an inactive chunk |
| machine | an in-world machine | a peer |
| grid | `ChemGrid` (the view's grid) | "grid tactics" (D5) |
