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
| The next ring | Loaded and drawn, simulation paused, props asleep |
| Further out | Coarse terrain only (distant hills) |
| Beyond | Not loaded; changes saved to disk |

Fire reaching the edge of the simulated area wakes the next chunk, up to a cap
on simulated chunks. A chunk that wakes after a long pause fast-forwards a
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
server/interest-management change, the fast-forward on wake. Part 2 is in
`docs/BACKLOG.md` §11.

## Part 2, first slice: the whole valley, waking around the players (built 2026-10-26)

Asked for after the 0.1.1 playtest (P1: "I want to be able to go explore
with friends").

- **Cells everywhere:** the chemistry covers every chunk terrain, 576×576
  cells (`TerrainGrid.Valley()`), the yard where it was (grid cell 279,
  315). `world/things.json`'s chunks moved by (3, 4) with it, so ids are
  now like `4,5#2`.
- **Rings:** each player's chunk and the ring around it are simulated; a
  chunk sleeps once no player is within 2 chunks (`ChunkStreaming`). Solo
  the grid applies the plan before each tick; online the host stamps it
  into its tick batches and history (`ChunkIntent`), so every machine
  pauses the same chunks at the same tick. A sleeping chunk is frozen,
  spells cast into it do nothing, and its neighbours' fire and water stop
  at its edge.
- **View:** the grid draws, and the fire lights scan, only active chunks;
  every cell is drawn once when a world starts (so sleeping chunks' trees
  and walls have their blockers). Chunk terrains are copied only when the
  cells first change them (`ValleyGround`).
- **The edge of the land** holds you: walls 3 m inside the last terrain,
  marked by rocks.
- **Checked:** probe `explore` (Fire Bolt on a hillside 300 m from the
  yard spreads; the yard sleeps unchanged and wakes on return), `online`
  (checksums equal with the switches travelling), `ChunkStreamingTests`
  (the plan, and a replay matching the live run).

Still open: fire reaching a sleeping chunk doesn't wake it, a chunk that
wakes doesn't fast-forward, props in sleeping chunks stay spawned (and
awake to physics), the netcode checksum still hashes every cell, and
players far apart are still all simulated by the host (interest by chunk).

See `docs/ARCHITECTURE.md` for the current code layout.
