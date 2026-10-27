# Glossary: the game's ubiquitous language

One word for each idea, used the same way in code, docs, commits and
conversation. When a word here names something, use it; when you need a
new idea, add its word here first. Where a word is listed under **Avoid**,
it either means something else or is retired.

Agreed with Andrew 2026-10-27. Older docs still use some retired words
(chiefly "square" for cell and "wake/sleep" for chunks); fix them when you
touch them.

## The world and its space

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

**Tick batch**
: Everything that happens to the world at one tick, in order: chunk
  switches, a rule switch, casts, effects, defense (`TickBatch`). The host
  orders them; every peer applies them the same.

**Command / intent**
: A **command** is an input to the core (`CastCommand`, `EffectCommand`,
  `ChunkSwitch`, `RuleChange`), the only way anything changes the world.
  An **intent** is the network message asking the host for one
  (`CastIntent`, `EffectIntent`, `ChunkIntent`).

**Deterministic / lockstep**
: Every peer runs the same world from the same commands and gets the same
  cells (D3, D7). Integer state, the world's seeded RNG, ordered iteration.

**Clock / day**
: The time of day read from the tick (`World.ClockTick`, which Hasten
  Time runs ahead; `Sky.Minutes`). *Avoid:* reading time of day from
  `World.Tick`.

## Chemistry

**Rules**
: What exists and how it reacts: `rules/*.json`, loaded into `Chem.Rules`.
  The single source of truth for both engines.

**Terrain / cover / gas**
: The three kinds of material a cell has: what it is (grass, stone, log
  wall, river), what lies on it (oil, ash, snow), what hangs over it
  (steam, smoke, fog) (`materials.json`).

**Element**
: What a spell or reaction brings to a cell: fire, cold, water, air,
  earth, lightning, thunder, grease, oil, fog, growth. *Not* the spell
  radial's top ring (that's a **group**).

**Reaction**
: A rule that runs when an element arrives at a cell, in order, until
  one says `stop` (`World.React`, `reactions.json`).

**Electric charge**
: A cell's `Charge` (0 to 3, decaying), which lightning leaves and
  conductors pass on. Say "charge" alone only when it's obviously this.

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
: How long a cast was held, 1 to 3 (`CastCommand.Charge`).

**Group**
: A ring-1 slice of the spell radial (fire, water, storm, earth, arcane,
  nature, time), at most three spells each (`groups.json`, `GroupDef`).
  Players see them as elements; we say group.

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

**Chunk switch**
: The command that activates or deactivates a chunk, stamped into tick
  batches online so every peer switches the same chunk at the same tick
  (`ChunkSwitch`, `ChunkIntent`).

**Fast-forward** *(planned)*
: Running an activating chunk a bounded number of ticks to catch up on
  time it spent inactive.

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

**Checksum / hash / fingerprint / golden**
: The **checksum** is what peers compare every 30 ticks (cells, rules,
  things, defense). A **cell hash** (`CellHash`, `ChunkHash`) is FNV-1a
  over cells; a **fingerprint** hashes the rules or the things; a
  **golden** is a hash a test pins. `WorldHash` is unrelated (the world
  generator's coordinate hashing).

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
