# The open world (D20, proposed 2026-11-13)

Andrew, 2026-11-13: "pick a coordinate system and then dynamically grow the chemistry grid as people explore … I
don't want to hardcode the size of Punchy's world … minimize the coupling." He added: assume caves now and sky islands
later, layers of terrain at the same x, z.

## Where we are

- **Our game:** a bounded `World` (a chunk table sized by the map, `ChunkCells.byChunk`). Each character's reach keeps
  chunks active, and D17 packs the rest.
- **Punchy, D19 step 3** (`chemistry-lockstep` dcbfaa7d): one 256 × 256 region, filled by the host and sent in the
  hello. The chunks inside it follow every player's reach, but the region doesn't grow: past it there's no chemistry.
  In solo it's rebuilt round the player past 96 m, and what burned is lost.

## The idea

| Piece | What it is |
|---|---|
| **Key** | `ChunkKey(Layer, X, Y)`: ints, negative allowed. `Layer` is the host game's number for a sheet of terrain: 0 the surface, then caves, sky islands, and so on. |
| **Cell** | The host game's world position over its cell size (1 m in Punchy's bridge): `floor(x / size)`, `floor(z / size)` (z is the core's y). Floor division for negatives, and so for the chunk: `cx = cellX >> 6`. |
| **Open world** | A `World` with no bounds. Its chunks live in a table keyed by `ChunkKey`, and a chunk exists once it's filled. Every walk over chunks (step, hash, snapshot, plan) goes in sorted key order, never in dictionary order. |
| **Fill** | A command: `FillChunk(key, ground)`. `ground` is the chunk's cells as the host game sampled them: kind (grass, water, rock, nothing) and anything else the bridge has today, such as a height step relative to the game's ground. It's applied on its tick like any command, so every peer makes the same chunk. |
| **Edge of what's filled** | A cell whose neighbour chunk isn't filled sees a wall: fire and water stop there, as at a map's edge today. The plan fills one ring past every active chunk, so the active chunks' neighbours exist. |
| **Reach** | As now: a character's reach is the chunks kept active around it, now also by layer. A character in a cave keeps cave chunks active. |
| **Teleport** | The reach jumps. Chunks are filled at the destination on the next tick or two, and the chemistry there starts when they land. Walking back, chunks meet by key. |
| **Snapshot** | Filled chunks only, in key order, packed. A joiner then gets new fills through ticks. |
| **Hash** | Over the filled chunks in key order. A chunk that isn't filled isn't hashed. |

### The host game's half (the port)

The world port for an open world asks the host game two things, both on the host (and in solo) only:

```csharp
public interface IOpenGround
{
    // Where each character is: its layer and its cell. The plan turns these into reaches.
    IEnumerable<(string who, int layer, int x, int y)> Characters();
    // What the ground is in a chunk never filled before; the session stamps it as a fill.
    ChunkGround Sample(ChunkKey key);
}
```

That's all the coupling there is. Punchy's size, biomes, realms and towns never cross it. When Zachary grows the world,
the chemistry follows players into it.

### Layers

- Each layer is its own sheet. Cells in different layers never touch for now.
- A host game decides what a layer is and which layer a character is on. Punchy's caves today are probably layer 1;
  the bridge checks how its caves and interiors are placed.
- **Later:** links between layers (a hole in a cave roof, a waterfall off a sky island, a portal) as a cell in one
  layer handing matter to a cell in another. Not in this job.

### Heights

The core's heights stay small steps relative to the host game's ground (the Punchy MVP lays deltas over
`SampleHeight`), so a fill carries no absolute height. A layer far above another needs nothing special.

## Plan

1. **The core (u2, `open-world-core`):**
   - `ChunkKey`;
   - the open world (a keyed chunk table, sorted walks, a filled edge acting as a wall);
   - the `FillChunk` command and its channel;
   - the plan (reach by layer, one ring of fill ahead);
   - snapshots and hashes over filled chunks.

   Tests under `dotnet test`: two peers far apart each burning their own meadow, a teleport, two areas growing until
   they meet, a joiner, and a cave layer under a burning meadow at the same x, y that stays untouched. Bounded
   worlds' goldens unchanged.
2. **Punchy (the PC, after 1):**
   - the bridge drops the region and becomes `IOpenGround` over `TerrainSource` and the players' positions;
   - solo uses the same path, so no more rebuilds round the player;
   - check whether Punchy ever shifts its origin, and use the absolute position.

   Checks: two copies on the PC far apart, a teleport, then the Mac and PC over Relay.

## Open questions

- **Memory over a long session:** filled chunks are never dropped yet. Packed, a chunk is a few KB, so thousands fit.
  Evicting far chunks to a save comes later.
- **Fill size:** 4,096 cells a chunk. A kind and a small height step packed and run-length encoded is ~1-4 KB, split
  by the session. A teleport into new ground fills ~25-50 chunks over a few ticks.
- **Our game moving to the open world too:** possible later (the bounded map would become a fill source), not planned.

## Core as built (`open-world-core`, 2026-11-13)

Plan step 1, in the package's `Runtime/` with no Unity. Bounded worlds are unchanged: their goldens and their step's
cost are the same (below).

| File | What it is |
|---|---|
| `ChunkKey.cs` | `ChunkKey(Layer, X, Y)`: floor division (`OfCell`, `CellOf` for a position over a cell size), key order (layer, row, column), `DistanceTo` (`int.MaxValue` across layers), and the layers' rows (`Row`, `OfRow`, `Chunk`, `KeyOfChunk`) |
| `OpenWorldGround.cs` | `ChunkGround` (a chunk's terrain, water and height step per cell; `Bytes` / `FromBytes`), `ChunkFill` (the command) and `FilledGround` (the open world's source) |
| `OpenWorld.cs` | `World.OpenWorld(rules, seed)`, `FillChunk`, `IsFilled` / `IsActive(key)` / `GroundOf` / `Filled` / `ActiveKeys`, and the open world's hash and save |
| `OpenWorldPlan.cs` | the port `IOpenGround`, `OpenWorldPlanner` (the plan, `Attach` to a session) and `OpenPlan` |
| `TickBatch.cs` | `Fills`, applied first in `Prepare`, before the chunk switches |
| `LockstepChannels.cs` | `Fills()`, the host's channel: the key, then the ground's bytes |

### The shape: one `World`, layers as runs of rows

An open world is a `World` like a bounded one. It has the same chunks, the same step and the same packing (D17); its
store is keyed by chunk coordinates and starts empty. That was the cheapest shape: the session, the hash window, the
snapshot and every system run unchanged, and a bounded world pays one extra test of a bool where it already tested
`WrapX`.

- **Layers** share the one sheet of rows: layer `L`'s row `y` is `y + L × 2²⁶` (`ChunkKey.Row`). A layer reaches about
  ±33,500 km of 1 m cells each way (`ChunkKey.MostChunks`, 524,287 chunks), and there are layers −15 to 15. A fill past
  that is refused, so the rows between two layers are never filled and cells in different layers never meet. The
  sheet's row-major order is the keys' order.
- **On the world** means "in a filled chunk" (`InBounds`). A neighbour chunk not filled is null to its neighbour's
  links, so fire, water and gas stop there, as at a map's edge.
- **A fill** makes the chunk inactive and packed with no changes against its ground. The plan activates it, so a
  chunk filled beside an active one changes nothing until it activates (an inactive chunk is a wall too).
- **No cell index:** an open world has no width, so `Idx` and `CellAt(index)` throw. The three flood fills that
  used `Idx` for their seen sets (a conduct, a gas flash, a melt's spill) now use `CellKey`, which gives the same sets
  and order on a bounded world.
- **The hash** walks the filled chunks in key order. Each gives its coordinates, its ground's fingerprint (FNV-1a 64 of
  its bytes) and its cells that differ from its ground, read where they lie and never unpacked. A hash window is
  `RollingHash.Every` ticks whatever the size, sliced by chunks.
- **Saves and snapshots** carry `open`: every filled chunk's ground in key order (base64 bytes), then the chunks'
  changes as any save does. `World.Load` makes an open world from them, so `LockstepWorld` and `JoinSnapshot` work
  as they are.
- **Weather:** a strike or a meteor falls on a random filled chunk (a bounded world's draws are as they were).
- **Not for an open world:** whatever needs a bounded map (land, buildings, machines' gate links, the hold, the
  dragon, golf, the course, defense, raiders). It runs the chemistry, casts, hands and strikes. Every spell casts in
  one (`EverySpellCastsInAnOpenWorld`).

### A bounded world's step, before and after

`TickStageBench` (the valley, 65 active chunks, 300 ticks after 600), Release under dotnet 8 on u2. Four rounds run
alternately against `dev` (da042fe7), in ms a tick:

| | clear | storm | fog |
|---|---|---|---|
| before (`dev`) | 4.04 | 9.36 | 7.24 |
| after | 3.94 | 9.30 | 7.06 |

The cells' hashes and the generator's state come out the same. The first try was about 5% slower: `Idx` threw on an
open world inline, and a method that throws isn't inlined. The throw is out of line now (`NoIndexAt`).

### The plan

`OpenWorldPlanner.Attach` hooks the host's `Stamping`. At each tick it stamps:

- **Fills:** every chunk within reach + 1 of a character, in the character's layer, that isn't filled or coming.
  The nearest go first, at most `FillsPerTick` (16) a tick, each `IOpenGround.Sample(key)` asked once.
- **Switches on:** a chunk within reach that is filled, with its ring filled too.
- **Switches off:** an active chunk further than keep from every character of its layer.

Reach and keep are `sim.json`'s streaming (2 and 3). The host's world runs a tick behind its stamps, so what's
stamped counts as done until its tick has run. A teleport fills its 49 chunks in 4 ticks, and its own chunk is
active on the first.

### The API the Punchy bridge calls

```csharp
// Every peer, the same channels in the same order:
var port = new LockstepWorld(rules, () => World.OpenWorld(rules, seed));
var session = new LockstepSession(transport, port, localPeer);
var casts = session.Register(LockstepChannels.Casts(PlayerName));
session.Register(LockstepChannels.Combos(PlayerName, devBuild));
var fills = session.Register(LockstepChannels.Fills());
var chunks = session.Register(LockstepChannels.Chunks());
// The host (and solo) plans from the game; a client never samples:
if (session.IsHost) OpenWorldPlanner.Attach(session, ground, fills, chunks);

sealed class PunchyGround : IOpenGround
{
    // Each player's layer and cell: ChunkKey.CellOf(pos.x, 1), ChunkKey.CellOf(pos.z, 1), absolute (no origin shift).
    public IEnumerable<(string who, int layer, int x, int y)> Characters() { ... }
    // A chunk's ground from TerrainSource: cell (lx, ly) of key is (key.X * 64 + lx, key.Y * 64 + ly).
    public ChunkGround Sample(ChunkKey key) { var g = new ChunkGround("dirt"); /* g.Set(lx, ly, "grass", water, 0) */ return g; }
}

// Casts and reads name a layer's row:
session.Request(casts, new CastCommand(spell, x, ChunkKey.Row(layer, y), dir, charge));
var cell = port.World.At(x, ChunkKey.Row(layer, y));             // null where nothing is filled
var (cx, cy) = ChunkKey.OfCell(layer, x, y).Chunk;               // port.World.ChunkCells(cx, cy) for a view's pass
```

The region and its `RegionSource` go: there's nothing in the hello, the snapshot carries the grounds, and solo is the
same path through the loopback (`AddHost(solo: true)`).

### Tests

`OpenWorldTests` (Simulation) and `OpenWorldKeyTests` (Unit):

- two peers 1,000 chunks apart each burn their own meadow, in step, and the burnt ground is kept while the reach is
  away and is still there when it comes back (sampled once);
- a teleport 500 chunks east and 300 north: its chunk is active within 3 ticks and a fire bolt burns there;
- two areas grow toward each other, and fire crosses the seam between a chunk filled from one side and one filled
  from the other;
- a joiner mid-fire gets every filled chunk and the host's hash;
- a cave (layer 1) under a burning meadow at the same x, y stays as it was filled;
- the chunk at (0, −1), negative cells, a fill twice, and the filled edge;
- every spell casting in an open world, which then saves and loads;
- keys, rows, order, a ground's bytes and the fill channel.

### Open

- **Holes:** a ground has no "nothing" cell yet (a cell with no ground at all). A chunk is filled whole.
- **Memory:** filled chunks are never dropped (D20's reopen-if). A packed chunk is its changes plus its ground (~4 KB
  decoded).
- **Snapshot size:** the grounds go in the snapshot (~1-4 KB each before base64). A long session's join grows with
  what's been explored.
