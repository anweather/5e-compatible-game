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
