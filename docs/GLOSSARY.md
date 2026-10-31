# Glossary: the game's ubiquitous language

One word for each idea, used the same way in code, docs, commits and
conversation. When a word here names something, use it; when you need a
new idea, add its word here first. Where a word is listed under **Avoid**,
it either means something else or is retired.

Agreed with Andrew 2026-10-27. Older docs still use some retired words
(chiefly "square" for cell and "wake/sleep" for chunks); fix them when you
touch them.

## The world and its space

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
  `world/` (the folder) is the **world data**: where things stand
  (`things.json`), the villages, the themes and looks.

**The valley**
: The hand-made start of the world: 9 × 9 chunks, 864 m, with the yard,
  the lake, the river, the falls, Hollowmere and the coast. Shaped by
  `ValleyShape` and baked in the Editor.

**Land**
: The valley's baked ground under each cell, in water units: the hills
  the cells themselves don't hold (a cell's elevation and height are on
  top of it). Baked once into `world/land.json` (Prototype > Bake Valley
  Land) so every peer has the same integers; only the water reads it, so
  water runs down a hillside (`World.SetLand`).

**Soak, damp**
: Thin water on open ground **soaks** in (and dries up) a unit at a time,
  at its terrain's rate (`soakEvery`); ground it soaked into is **damp**
  for a while (`Cell.DampUntil`) and soaks more slowly, as in rain. Deep
  water (lakes, the river, the sea, a filled pit) doesn't soak.

**Shore map**
: Over the sea and out past the terrain, the way to the nearest land and
  the distance to the shore (`ShoreMap`, baked by `ValleyBuilder` into
  `Generated/ShoreMap.asset`). The surf's push (`WaterCurrents`) and the
  waves (`Ocean`, the Ocean shader) both run along it, so near the island
  they go toward the island. A view of the land, never the chemistry (D4).

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

**Group**
: A ring-1 slice of the spell radial (fire, water, storm, earth, arcane,
  nature, time), at most three spells each (`groups.json`, `GroupDef`).
  Players see them as elements; we say group.

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
  a stroke's penalty. The course is `world/golf.json`.

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

## Things in the world

**Thing**
: Anything listed in `world/things.json`: a prop or an item, with a
  stable **thing id** (`ThingId`: its chunk and its place in that chunk's
  list, `4,5#2`). Append to a chunk's list, never insert.

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

**Character** *(proposed, for the chunk work)*
: Anything whose presence keeps the world around it active: today every
  wizard; later perhaps raiders or villagers that matter to play.

**Raider**
: A core entity in the defense field's waves (`Raider`, BACKLOG §17).

**Creature / fauna**
: Animals and folk drawn in the view (`ValleyFauna`, Punchy's creatures).
  View-only, never in the rules.

**Ambient life**
: View-only life that makes the world feel lived in: gulls, fish, crabs,
  fireflies, villagers (`AmbientLife`, `AmbientCritters`, D12).

*Retired:* **critter**. It meant four things (the sandbox's emitters, the
  collectible world-changers of `TRAVERSAL.md`, raiders, and ambient
  life). Say which one.

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

**Mod / active mod / switch on, revert**
: A **mod** is a JSON patch over `rules/` (`docs/MODDING.md`). A room's
  **active mods** are the ones on, in order, each with its title, the peer
  who added it and the tick (`RoomMod`). A `RoomCommand` **switches one
  on** (MCP: `apply`) or **reverts** it (takes it off; the rest are
  composed again in order). Every peer checks a mod itself
  (`ModValidator`) before switching it on; one that fails is **refused**
  everywhere, with a sentence. *Avoid:* "install", "enable".

**Checksum / hash / fingerprint / golden**
: The **checksum** is what peers compare every 30 ticks (cells, rules,
  things, defense). A **cell hash** (`CellHash`, `ChunkHash`) is FNV-1a
  over cells; a **fingerprint** hashes the rules or the things; a
  **golden** is a hash a test pins. `WorldHash` is unrelated (the world
  generator's coordinate hashing).

## The Dragon

**Dragon**
: The world reset as an event (BACKLOG §26, `World.Dragon.cs`, `sandbox/src/dragon.ts`). Summoned by a
  `DragonCommand` (the Reset, the host alone online), or by itself past the changed-cell budget. Its path
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
  `dragon.budget`, the warning starts.

**Sheltered**
: Out of the dragon's breath though under it (`World.ShelteredFromBreath`): in water at least
  `dragon.shelterWater` units deep, not frozen, or with a blocking cell of `dragon.wallTerrains`
  (a stone wall, Wall of Stone's raised rock) within `dragon.wallReach` cells straight toward where the
  dragon flies from.

## World generation

**Theme / blueprint**
: A **theme** is JSON goals for the land (`world/themes/`); solved for a
  seed it's a **blueprint** of zones, mountains, lakes and rivers (D11).

**Theme look**
: Which of Punchy's lands paints a place (`world/looks.json`). Not a
  spell's visuals and not a wizard's appearance.

**Recipe / seed / builder**
: Generated content (D12): a recipe in data, a seed, and a builder that
  makes it from shapes (villages, buildings, props, creatures).

## Ambiguities settled

| Word | Means | Not |
|---|---|---|
| square | a spell shape; a village square | a cell |
| step | a height step (0.5 m) | a tick |
| element | a chemistry element | a radial group |
| charge | a cell's electric charge | a cast charge (say "cast charge") |
| life | a cell's `Life` timer | ambient life; the defense field's lives |
| look | a theme look | spell visuals; a wizard's appearance |
| frozen | ice (`Cell.Frozen`) | an inactive chunk |
| asleep | a resting physics body | an inactive chunk |
| machine | an in-world machine | a peer |
| grid | `ChemGrid` (the view's grid) | "grid tactics" (D5) |
