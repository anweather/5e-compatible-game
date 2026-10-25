# World streaming: from one valley to an endless world

The north star for how the world grows, and what part 1 (the chunked valley)
builds now. Agents: build toward this; don't build past part 1 without asking.

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

- **One authoritative server** simulates every active chunk: the host for
  small worlds, a dedicated server for a persistent one.
- **Interest by chunk:** each player follows only the chunks near them.
  Entering a chunk = a per-chunk late join (state snapshot at a tick).
  Players in the same chunks keep lockstep from shared inputs, so bandwidth
  stays tiny where players are together.
- **Safety net:** a per-chunk checksum. On mismatch (e.g. effects crossing a
  chunk edge that a client doesn't simulate) the server sends that chunk's
  changed cells and the client snaps.
- **Determinism stays:** replays, golden tests and cheap drift detection all
  rely on it, even where it's no longer the only thing keeping peers in sync.

Groups of players far apart simply follow different chunks on one server; a
player travelling between them hands over chunk by chunk.

## Unity specifics

- **Floating origin:** past ~5–10 km float precision jitters, so the world
  origin re-centres on the players. Cell coordinates are integers and never
  move.
- **Background work:** the chemistry core is engine-free C#, so chunk
  generation, saving and fast-forward run off the main thread.
- **Props from data:** props are spawned from chunk data (stable ids from
  chunk + index), not placed in the scene.
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

Not in part 1: streaming to disk, seed generation, floating origin, the
server/interest-management change, the fast-forward on wake.

See `docs/ARCHITECTURE.md` for the current code layout.
