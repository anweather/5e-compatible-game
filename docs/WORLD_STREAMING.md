# World streaming: from one valley to an endless world

The north star for how the world grows, and what part 1 (the chunked valley)
builds now. Agents: build toward this; part 2's first slice is built (below), ask before
going further.

## The unit: a chunk

A chunk is 64×64 cells. Cells are 1.5 m, so a chunk is 96 m square. Everything
about the world is stored, loaded, simulated and replicated per chunk:

| Layer | Per chunk |
|---|---|
| Ground | One Unity `Terrain` (heightmap 257 at full detail = 4 samples per cell; 65 when coarse) |
| Cells | The chemistry state of its 64×64 cells (terrain, cover, water, fire, gas, charge, height) |
| Things | Its props and items, spawned from data with stable ids |
| Storage | Only what players changed; an untouched chunk regenerates from the world seed |

Cells have world-wide integer coordinates: `chunk = floor(cell / 64)`,
`local = cell mod 64`. Nothing outside the chunk store assumes the world has a
fixed size.

## Rings around each player

| Ring | State |
|---|---|
| The player's chunk and its 8 neighbours | Loaded, full detail, simulated (chemistry and physics) |
| The next ring | Loaded and drawn, inactive, props asleep |
| Further out | Coarse terrain only (distant hills) |
| Beyond | Not loaded; changes saved to disk |

Fire reaching the edge of the active chunks activates the next chunk, up to a cap
on active chunks. A chunk that activates after a long time inactive fast-forwards a
bounded number of ticks (fires burn out, water settles). That's believable,
not exact.

## Players in different places

Today every peer simulates the whole world in lockstep from shared inputs
(NetChem). That stops scaling once players are far apart. The target:

- **One authoritative simulation** of every active chunk. Today that's a
  player hosting (D7). The north star is one dedicated server for the
  shared endless world; whether and when to run one is still open.
- **Interest by chunk:** each player follows only the chunks near them.
  Entering a chunk = a per-chunk late join (state snapshot at a tick).
  Players in the same chunks keep lockstep from shared inputs, so bandwidth
  stays tiny where players are together.
- **Safety net:** a per-chunk checksum. On mismatch (e.g. effects crossing a
  chunk edge that a client doesn't simulate) the host sends that chunk's
  changed cells and the client snaps.
- **Determinism stays:** replays, golden tests and cheap drift detection all
  rely on it, even where it's no longer the only thing keeping peers in sync.

Groups of players far apart simply follow different chunks on one host (or,
later, one dedicated server); a player travelling between them hands over
chunk by chunk.

## Unity specifics

- **Floating origin:** past ~5–10 km float precision jitters, so the world
  origin re-centres on the players. Cell coordinates are integers and never
  move.
- **Background work:** the chemistry core is engine-free C#, so chunk
  generation, saving and fast-forward run off the main thread.
- **Props from data:** props are spawned from chunk data (stable ids from
  chunk + index), not placed in the scene. Built: see "Props and items from
  data" below.
- **Regional rules (idea):** mods scoped to a region ("Wildfire in this
  valley", a damp biome) on top of the global rule set.

## Part 1: the chunked valley (now)

Goal: the yard stops being a tiled board. The valley becomes a 9×9 grid of
chunk terrains (864 m), and the old yard is just meadow with natural features
(pond hollow, oil slick, gully and plank bridge, real bushes, props on the
grass).

- **Terrain:** the valley is built as 9×9 chunk `Terrain`s with neighbours
  set. Chunks near the play area use the 257 heightmap; the rest use 65.
  Same layers, trees and detail on all, with no visible seams.
- **Cells on terrain:** cell `Height` and pits shape the chunk heightmap
  (`CellHeightfield`: baked base + cell offset, eased). Terrain types and
  covers paint the alphamap (`CellPaint`). One water mesh covers the pond,
  trenches and puddles (`CellWaterMesh`). Invisible blockers stand on blocking
  cells (`CellBlockers`).
- **Removed:** tile blocks (`GridTileBuilder`), per-cell water and cover
  cubes, the terrain hole under the yard, and the "stand on yard" special cases.
- **Chemistry core:** chunked cell storage with world-wide coordinates, plus an
  active-chunk set (all chunks active for now; the switch exists and is
  tested). Per-chunk hashes next to the world `CellHash`. Behaviour, goldens and
  netcode are unchanged.
- **Props:** spawned from data with stable ids, not hand-placed in the scene.
  **Done** (`feat/props-from-data`, below).

### Props and items from data (done)

- **The data:** `world/things.json` (repo root, next to `rules/`). Per
  chunk, a list of things: `kind` (an id in `rules/entities.json` or
  `rules/items.json`), `cell` (inside the chunk, 0 to 63; the world cell is
  chunk × 64 + cell), optional `rot` (degrees about the vertical), optional
  `link` (a pair of sending stones) and a free `note`. It is in `world/`, not
  `rules/`, because rules say what exists and how it reacts (mods patch
  them, the sandbox loads them) and this says where things stand in one
  world; per-chunk files can split out of it when chunks stream.
- **Stable ids:** `Chem.ThingId` is the chunk and the thing's index in that
  chunk's list, packed into 64 bits (`"4,5#2"` in logs). Worked out from
  the file, never a runtime counter, so host, clients and late joiners name
  every thing the same way. Add new things at the end of a chunk's list, or
  ids after it shift.
- **Core:** `WorldThings.cs` reads the file (`InChunk`, `Find`, `At`,
  `LinkedTo`), checks kinds and links against the rules, and has a
  `Fingerprint`, which is the third part of the netcode checksum (cells /
  rules / things). `EffectCommand.Thing` carries the id of the prop whose
  blast, embers or spill it is. Tests: `ThingTests.cs`.
- **Unity:** `ThingSpawner` spawns the active chunks' things from prefabs
  (one per kind, built into `Generated/Things/` by `PropBuilder` and
  `ItemBuilder`, registered through `Generated/ThingNetworkPrefabs.asset`),
  at rest on the ground or at their waterline. Online the host spawns them
  as NetworkObjects with the id in `NetProp` (so late joiners get it too);
  a client drops its solo copies and adopts the host's by id. A broken
  thing is despawned and destroyed, and remembered as gone.
  `SpawnChunk` and `UnloadChunk` are the per-chunk API; nothing unloads
  chunks yet.
- **Checked:** the yard plays as before (same 18 things on the same cells;
  probe `smoke` checks `things_spawned`), and `probe online` checks
  `thing_ids_match` (the same ids and kinds on host and client).

### Still open in part 1

- Water easing: reshaped ground eases to its new height, the water
  surface doesn't.
- The pond reads blocky from overhead, and a raised wall's crags are
  primitive shapes.
- Online on the chunked valley has run with 2 players: on one machine
  (`docs/MULTIPLAYER.md` §8) and Mac↔PC over Relay (`docs/QUALITY.md`,
  "Two-machine online check"), not yet with 3 or 4.

Not in part 1: streaming to disk, seed generation, floating origin, the
server/interest-management change, the fast-forward on activating. Part 2 is in
`docs/BACKLOG.md` §11.

## Part 2, first slice: the whole valley, active around the players (built 2026-10-26)

Asked for after the 0.1.1 playtest (P1: "I want to be able to go explore
with friends").

- **Cells everywhere:** the chemistry covers every chunk terrain, 576×576
  cells (`TerrainGrid.Valley()`), the yard where it was (grid cell 279,
  315). `world/things.json`'s chunks moved by (3, 4) with it, so ids are
  now like `4,5#2`.
- **Rings:** each player's chunk and the ring around it are simulated; a
  chunk deactivates once no player is within 2 chunks (`ChunkStreaming`). Solo
  the grid applies the plan before each tick; online the host stamps it
  into its tick batches and history (`ChunkIntent`), so every machine
  switches the same chunks at the same tick. An inactive chunk is held as it is,
  spells cast into it do nothing, and its neighbours' fire and water stop
  at its edge.
- **View:** the grid draws, and the fire lights scan, only active chunks;
  every cell is drawn once when a world starts (so inactive chunks' trees
  and walls have their blockers). Chunk terrains are copied only when the
  cells first change them (`ValleyGround`).
- **The edge of the land** holds you: walls 3 m inside the last terrain,
  marked by rocks.
- **Checked:** probe `explore` (Fire Bolt on a hillside 300 m from the
  yard spreads; the yard stays unchanged while inactive and activates on return), `online`
  (checksums equal with the switches travelling), `ChunkStreamingTests`
  (the plan, and a replay matching the live run).

Still open: fire reaching an inactive chunk doesn't activate it, a chunk that
activates doesn't fast-forward, props in inactive chunks stay spawned (and
awake to physics), the netcode checksum still hashes every cell, and
players far apart are still all simulated by the host (interest by chunk).

## Part 2, second slice (agreed 2026-10-27): reach, the active region, and what's still burning

Agreed with Andrew (see "Decided" below); being built in the slices listed. The words are the glossary's
(`docs/GLOSSARY.md`): a chunk is **active** or **inactive**, a character's
**reach** is the chunks kept active around it, a **live** cell is one where
something is still changing on its own.

### What we have and where it hurts

- Each wizard activates the 3×3 chunks round the one it stands in, and a
  chunk deactivates once no wizard is within 2 chunks (`ChunkStreaming`).
  Online the host stamps the switches into its tick batches.
- An inactive chunk is a wall: nothing in it changes, and fire, water and
  gas beside it stop at its edge.
- So a fire you start and walk away from **freezes mid-flame** the moment
  its chunk deactivates, and is still burning, unchanged, when you come
  back an hour later. Fire is fast (a chance in two each tick to catch each
  neighbour): a fire started near the edge of the active chunks reaches the
  wall within seconds and stops dead in a straight line.

Measured in the Editor (the valley as it is, one machine): an active chunk
costs 0.02 ms a tick on dry land, about 0.2 ms over the lake and 0.3 to 0.4
ms over the sea, because water is worked every tick even at rest. 3×3
chunks round the yard cost 0.65 ms a tick, 5×5 cost 1.7 ms, and the whole
valley (81 chunks, much of it sea) 14.7 ms.

### 1. Reach: how far is far enough

A character's reach is every chunk within **R** chunks of the chunk it
stands in (a square, 2R+1 across). Today R is 1 and every wizard is a
character. It has to cover three things:

- **What the character can touch.** The longest spell reaches 12 cells
  (18 m) and a blast a few more, so this is the smallest need.
- **What the character can watch.** A fire a hundred metres off should keep
  burning while you watch it. That's a chunk beyond where you stand.
- **Room to run.** Something started at the edge of your reach needs space
  before it meets the wall. Fire covers a chunk in about ten seconds.

**Proposal:** R = 2 (5×5 chunks, at least 128 m of active world in every
direction), and a chunk deactivates once no character is within R + 1 (as
today, so walking along a chunk edge doesn't flicker it). That costs about
1.7 ms a tick per lone character over land, more over water. R, and how
far past it a chunk stays, go in `sim.json` (`streaming`) so mods and
slower machines can change them.

Characters are every wizard now. Later anything that should keep the world
round it running (a raider wave in the defense field, villagers that
matter) can be a character, with its own, smaller R.

### 2. Overlapping reaches: one active region

Every tick there is one **active region**: the union of all characters'
reaches. Where reaches overlap, the chunks are simply active once; nothing
is simulated twice and there's no seam between two players' areas. Two
groups of players far apart give two separate islands of active chunks, and
the cost is their sum.

- **Merging and splitting:** as two players walk towards each other their
  reaches join; as they part, the chunks between them deactivate once
  neither is within R + 1, the same rule as for one player.
- **Order:** the plan is worked out from all characters at once and sorted
  by chunk (as `ChunkStreaming.Plan` already does), so it doesn't depend on
  who is listed first. Online the host plans it and stamps it into tick
  batches, so every peer activates and deactivates the same chunks at the
  same tick. That doesn't change.
- **Later, with interest by chunk** (each peer simulating only what's near
  it): overlap is where it gets hard, because two peers then simulate the
  same chunks and must agree. That's for when interest by chunk is built;
  with today's lockstep, overlap costs nothing.

### 3. When the reach moves off something still reacting

The choice is what happens to a chunk that leaves every reach while
something in it is live: a fire burning, water flowing, gas drifting,
charge fading, shoots growing. Four ways, which combine:

| | What happens | For | Against |
|---|---|---|---|
| **A. Hold it** (today) | It deactivates at once; the fire waits mid-flame | Simplest; costs nothing | You come back to a fire that's still burning a day later |
| **B. Let it run out** | It stays active, outside any reach, until it's **settled** (nothing live in it), then deactivates. Activity crossing into an inactive neighbour activates that one too | The world keeps burning while you walk away; a wildfire really spreads | Unbounded without caps: a forest fire could activate half the valley |
| **C. Catch up when you return** | When an inactive chunk activates, it first runs the ticks it missed, up to a cap, before play goes on | Bounded cost; fires you left have burned out when you return | A chunk caught up alone treats its inactive neighbours as walls; a burst of work on the tick it activates |
| **D. Resolve it by rule** | On deactivating, live cells jump to how they'd end: a fire burns its fuel to the burnt terrain, water levels out | Cheap and instant | A second, approximate version of every rule, to keep in step with the real one and the sandbox |

**Proposal: B with caps, then C for whatever the caps left behind.** D only
if B and C prove too slow.

- **Linger.** A chunk that leaves every reach stays active while it isn't
  settled. A chunk is **settled** when no cell in it has been live for a
  few ticks (`settleTicks`, about 2 s), so a fire that has burned out, or
  water that has stopped moving, lets it go. The core already knows most of
  this as it ticks (burning cells, gas, charge, `Life`, and water flow
  marks), so it only has to remember, per chunk, the last tick anything in
  it was live.
- **Spread.** When fire, water or gas in an active chunk would cross into
  an inactive one, that chunk activates too and lingers. This is
  part 2's plan for fire reaching the edge of the active chunks.
- **Caps.** At most `lingerChunks` chunks (say 16) linger at once, and none
  for longer than `lingerTicks` (say 5 minutes). When the cap is full, the
  lingering chunks furthest from any character deactivate first. Rain,
  which touches every cell outdoors, would otherwise keep everything live:
  rain alone doesn't count as live.
- **Catch up.** A chunk that deactivated while still live remembers the
  tick it stopped. When it next activates, it first runs the ticks it
  missed, up to `catchUpTicks` (say 2 minutes of game time), with the
  weather of each of those ticks (D10 makes that a function of the tick).
  So a fire you abandoned has burned out when you come back. Neighbours
  that are still inactive are walls during the catch-up, as today.
- **Determinism.** "Live", "settled", the caps and the catch-up are all
  worked out in the core from the cells, the same on every peer. The
  host's plan (and so the tick batches) carries the activations and
  deactivations. The catch-up is a command in the batch ("chunk 6,7
  activates and catches up 340 ticks"), so every peer runs it at the same
  tick and a late joiner's replay matches.
- **Props.** Physics props in inactive chunks go to sleep (Rigidbody sleep,
  and the spawner keeps them but stops them) and wake when their chunk
  activates. That's part 2's other leftover, and it fits here.

### What this needs (slices)

1. **Reach and the active region** (built 2026-10-27). R and the deactivate distance in
   `sim.json`; the plan works from all characters (no change in
   behaviour at R = 1); the tests and the `explore` probe at R = 2.
2. **Live and settled** (built 2026-10-27). The core keeps, per chunk, the last tick
   anything was live (`World.LastLive`, `IsSettled`, after `streaming.settleTicks`);
   a test for each kind of live cell (`LiveChunkTests`). Rain only counts once its water moves.
3. **Linger and spread** (built 2026-10-27, `ChunkPlanner`). The plan keeps unsettled chunks active within
   the caps, and activity reaching an inactive chunk activates it; replay
   tests online; probe: walk away from a fire and watch it burn on, then
   out (the probe checks it burns on; `LingerTests` that it burns out and lets go).
4. **Catch up.** The catch-up command, run in the core over one chunk;
   a test that a chunk caught up matches one that was never deactivated
   (when its neighbours were quiet); probe: leave, come back, it has
   burned out.
5. **Props sleep with their chunk.**

The golden hashes don't move: with everything active (the tests' worlds),
none of this does anything.

6. **Still water costs nothing.** A chunk whose water hasn't moved for a
   few ticks skips the water pass until something disturbs it (a cast, an
   effect, a neighbour's flow). Behaviour unchanged, in the core and the
   sandbox together, with a golden.

### Decided (Andrew, 2026-10-27)

- **R = 2:** every character keeps the 5×5 chunks round it active.
- **Run out, then catch up:** chunks still live when they leave every reach
  linger until settled, within the caps; what the caps cut off catches up
  its missed ticks when it next activates.
- **Still water costs nothing** (slice 6).
- **Characters are wizards only** for now: raiders and the defense field
  wait while no wizard is near.

## The land around the valley (built 2026-10-27)

The valley floor (the yard, the cabins, the lake, the river and the falls)
is still shaped by hand (`ValleyShape`), climbing gently north into the
foothills. Past it, over a 90 m blend, the land is generated, in two layers
(D11):

- **Layout: a world theme.** `world/themes/home-valley.json` says what goes
  where (mountains far north with a tarn in their foothills, a brook down
  the west, woods to the sides, fields to the south, groves and meadows
  between). The PocketTactics theme engine, ported to the core
  (`ThemeSchema`, `ThemeRegistry`, `ThemeBlueprint`, `ThemeField`, on
  `WorldHash`, `WorldNoise`, `ThemeGeometry`), solves it for a seed and
  says, for any cell, which look the theme paints there and whether a lake
  or river fills it. Exact maths only, pinned against PocketTactics' own
  TypeScript (`WorldGenTests`) and by a golden (`ThemeTests`).
- **Look: Punchy's biome terrain.** `world/looks.json` maps each look to
  one of Punchy's lands (grassland, forest, mountains, fells), which give
  the height, the terrain layers and how thick the forest grows
  (`ThirdParty/Punchy/Terrain/Editor/BiomeTerrain.cs`, `ValleySurroundings`);
  where the theme says nothing, Punchy's own random regions fill in. The
  theme's lakes and rivers are carved in and baked as cells.

All of it is baked in the editor with the rest of the valley (terrains and
the cell map), so every machine loads the same world. **Prototype > Preview
World Theme** draws the map to `Generated/ThemePreview.png`.

Next: roads, settlements and goal scoring from the theme engine; ids for
things that survive regeneration; then chunks generated from the seed at
runtime, and a bigger world.

## Part 3 (plan): generating chunks as players reach them

Agreed with Andrew 2026-10-27. Not built yet; build in the slices below.

**The world:** one **large, bounded continent**, not an endless one. The
world theme (D11) lays it out: the hand-made valley at the start, then
mountains, lakes, rivers and later roads and towns round it, fading at its
edges into a coast or impassable mountains. Its size is a setting
(several km across); nothing assumes the 9x9 chunks of today.

**Each chunk is a pure function of the world's seed.** The first time a
player comes near a chunk it is generated, the same on every machine; an
untouched chunk is never stored, only regenerated. What players change is
kept as a diff against the generated chunk (below).

**Two generators, one shape.**
- **The core decides the land: cells and their height.** `ChunkGen`
  (Chem, exact maths only, D11) gives a chunk's 64x64 cells from the seed
  and the theme's blueprint: the terrain (grass, brush, trees, rock, water),
  and the **height of the ground in whole steps**. Mountains are real: a
  cell high on a mountain is rock or soil several steps up, so digging into
  one (Move Earth, Fireball's crater) reaches real rock, and water runs
  down real slopes. The big shapes (mountains, ridges, hills, the valleys
  rivers run in) use exact noise like the theme engine's, so they are the
  same on every machine.
- **The view draws it: Punchy adds detail and colour.** Each chunk's Unity
  terrain is built at runtime from the core's heights, smoothed, with
  Punchy's noise adding only small surface detail on top, and Punchy's
  surface rules painting it (rock on slopes, snow up high). Trees and grass
  follow the cells. Float maths is fine here because nothing shared depends
  on it, and chunk seams match because every value is a function of world
  position. Built on a worker thread so travelling doesn't hitch.

**When chunks are made and dropped (lockstep).** `ChunkStreaming`
already plans which chunks are active, inactive or far, and online the
host stamps the plan into its tick batches. It also says when a chunk is
first generated, so every machine generates it at the same tick, and a
late joiner replaying from tick 0 does too. Nothing extra is sent: the
seed is enough. Far chunks nobody has changed are simply dropped.

**What is saved: diffs.** A changed chunk keeps only what differs from its
generated self (cells, things broken or moved), stamped with the generator
and theme versions so a new generator never misapplies an old diff
(PocketTactics' diffs). In a shared online world the host machine keeps
the save, as a single-player game would; whether a shared world should
later live on a server is open.

**Things.** Generated props and items get ids from their chunk, kind and
spot hashed together, so the same tree or barrel always has the same id;
the hand-placed ones in `world/things.json` keep theirs.

**The valley stays hand-made:** a fixed landmark at the start whose chunks
come from the baked map, as today; the generator fills round it.

**Slices:**
1. `ChunkGen` in the core: cells and heights from the theme's
   blueprint and exact noise, with ids that survive regeneration; goldens
   on the PC and the Mac.
2. Generate on demand: chunks made as players come near, the generate step
   in the host's tick batches, a replay test.
3. Runtime terrain: each chunk's Unity terrain, trees and grass built from
   the core's heights with Punchy's detail and paint, off the main thread.
4. Saves: diffs on disk, far chunks dropped.
5. The full continent: a bigger theme core, and a floating origin past
   5-10 km.

See `docs/ARCHITECTURE.md` for the current code layout.
