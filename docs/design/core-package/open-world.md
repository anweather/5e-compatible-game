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

## Punchy as built (`chemistry-open-world`, 2026-11-13)

Plan step 2, on Punchy's `chemistry-open-world` (off `chemistry-lockstep`), against the core as it is on `dev`
(929cbc78). No core change was needed. Punchy reads the package from the main checkout, as before.

- **The region is gone.** The bridge's world is `World.OpenWorld(rules, 1)` through `LockstepWorld`. Solo, host and
  client run the same session (solo over the loopback), with no hello region, no `RegionSource` and no re-centring.
  The channels are `casts`, `combos`, `fills` and `chunks`, and `OpenWorldPlanner.Attach` runs on the host and in solo.
- **`IOpenGround` over Punchy** (`PunchyGround`, in `ChemistryBridge.Lockstep.cs`):
  - `Characters()` gives every `NetPlayer` on the server, or the one `PlayerTag` player in solo. Each has its layer and
    its cell, `ChunkKey.CellOf(x, 1)` and `CellOf(z, 1)`. Punchy never shifts its origin (its positions are absolute;
    the terrain streams round the camera), so the position is used as it is.
  - `Sample(key)` runs one Burst job over the chunk's 4,096 cell centres. It decides each cell's kind as the region
    did: grass where blades grow, water two steps down with 6 units, rock on steep slopes, dirt elsewhere.
  - The first fill waits until `TerrainSource` has stayed the same for 3 s, because the first one at start-up is a
    stand-in. If the terrain changes before any cast lands (and with no peers), the session starts again. After that,
    filled chunks keep the ground they were filled with.
- **Layers:** the surface is layer 0. A player more than 2 m under the surface, in a cave's air or the sunken temple's,
  is on layer 1. A layer-1 chunk's ground is the first cave floor under the surface: dirt, cave pools as water, and rock
  where there's no cave. Caves stacked over each other share layer 1 for now, and the first floor wins. Interiors
  aren't enterable spaces yet (`docs/agents/interiors.md` is a design note), so they get no layer.
- **The view** is a 256 × 256 window of 4 × 4 chunks round the local player, in its layer. It moves a chunk at a time as
  the player walks, and only the view moves. Each tick it reads every filled chunk in the window by world position
  (`World.At(x, ChunkKey.Row(layer, y))`), active or not, after `SetDrawn` for those chunks, so an inactive chunk keeps
  its look. A chunk not yet filled is blank. The kinds the shaders compare against come from each chunk's
  `GroundOf(key)`, not from local sampling, so a client samples only heights for its flames and puffs. Height edits
  (`TerrainEdits`) are the window's, on the surface only: they're re-laid from the cells when the window moves. In a
  cave the surface overlay is off, and flames and puffs sit on the cave floor.
- **Casts** are still session requests, with the row at `ChunkKey.Row(layer, y)`. A cast at a cell whose chunk isn't
  filled on the casting peer is **refused there** (logged, not sent): the player casts again a moment later. After a
  teleport into new ground the chunk was filled within half a second, so this hardly shows.
- **Tests without keys:** `-chem-script "<s>:<step>; ..."` (`ChemistryBridge.Script.cs`). The steps are demo, lake,
  cast, goto, hop, back, walk and quit. The sum line every 60 ticks adds the chunks filled, active and unpacked, how many
  were sampled and in how long, and Unity's reserved memory.
- **Size:** the bridge went from 2,203 lines to 2,467. That includes the 162-line test script; without it, 2,305. The
  view's chunk walk and the ground port replace the region's coding and re-centring.

### Results (Windows, Mono development player, one PC)

| Check | What happened |
|---|---|
| Solo: Fireball on a meadow, walk 320 m away and back | It spread to 5,304 burnt in 60 s. The fire's chunks went inactive while the walk was away; on the way back the burnt ground was there, and the fire spread on from where it had stopped (14,672 burnt). No chunk was sampled twice and there was no rebuild |
| Solo: Sleet Storm on a lake | 85 cells of ice, still there after a 3 km walk out and back |
| Teleport 2 km east into ground never filled | A Fire Bolt there was taken 0.5 s after the teleport (its chunk was filled and active), and it burned |
| Two peers 2 km apart (`-host -place meadow`, a `-join 127.0.0.1` copy that hops to (1356, 1299)) | Each Fireball burned its own meadow (13,549 cells on the host's by tick 600). Then the host teleported to the client's meadow, where both areas met by key with no new fills, and back again. The checksums agreed 39 of 39 times over 6.3 minutes (ticks 180 to 2,460), `desync=False` on every line |
| A late joiner at tick 654, after both fires | It loaded 147 filled chunks from the snapshot and agreed 24 of 24 times |
| A cave (`-place cavepool`) | A Fireball in a cave pool on layer 1 boiled it (36 steam). On the surface above, 49 more chunks were filled on layer 0 and a Fireball burned the meadow. Back in the cave the view showed 0 burnt: the layers never touched |
| Punchy's play-mode tests | 35 of 35 passed (in the Editor, `test run --mode playmode`) |

**Memory (solo, a 5-minute walk 1.5 km out and back at 10 m/s):** 210 chunks filled, 63 unpacked at most, 581 ms of
sampling in all (about 2.8 ms a chunk). Unity's reserved memory went from 1,041 MB to 1,143 MB, and the GC heap stayed
at about 90-110 MB. The process was 2,038 MB working set and 2,888 MB private at the end, much of it Punchy's own
streaming. The chemistry's share is small: a packed chunk is its ground plus its changes.

**What behaves differently:**

- Nothing is lost when a player walks away or teleports. A chunk outside every reach goes inactive and stops (a fire
  there waits) until someone comes back.
- The first fill waits for the terrain to settle (3 s). The host's hitch on a teleport into new ground (sampling up to
  16 chunks a tick on the main thread) is gone: see "Fills off the main thread" below.
- The view is still a 256 × 256 window round the player. Burns further than 64-192 m away aren't drawn until the window
  reaches them, and height edits outside the window come off the ground until it comes back.
- A cast where nothing is filled yet is refused on the caster, not queued.

### Fills off the main thread (Punchy `chemistry-fill-jobs`, 2026-11-13)

No core change. A chunk cost 6-16 ms because of the work itself: about 3 µs a cell of Burst terrain sampling
(`SampleGround`, `SampleNormal`'s heights, `SampleSurface`), 12 ms a chunk on one thread, 2.6 ms when the main thread
waited for it across 15 workers, more when Punchy's own jobs held the workers. It was already one Burst job; the cost was
that the main thread waited for it. The same code managed is 330 ms, and making the `ChunkGround` is 0.1 ms.

- Each chunk's ground is now its own job, started as soon as a reach wants the chunk (the frame a character changes
  chunk, nearest first, 16 under way at most). The bridge does `OpenWorldPlanner.Attach`'s stamping itself and sets
  `FillsPerTick`, each tick, to the number of nearest unfilled chunks whose jobs are done, so the planner's own order
  (nearest, then by key) holds and the planner never samples. A chunk still waiting is not filled yet, as before.
- **Asked ahead:** the bridge's own teleports (the test script's goto, hop and back) start the jobs round the
  destination before the player moves. Punchy's other teleports (debug locations, `-place`) set the player's position
  directly, so the bridge sees those the frame after, as it sees a client's: the host never knows a client's
  destination before the client's player has moved (movement is client-authoritative).
- The jobs read Punchy's height edits (`TerrainEdits`), so the bridge waits for any under way before it writes the next
  edits buffer.

| Check (Windows, Mono development player, two Editors open) | Before → after |
|---|---|
| Job against main-thread sample, 300 chunks (150 on the cave layer, by a cave pool) | 0 cells differ |
| Solo, 5 teleports 1.4-2.9 km into new ground: the fills' main-thread work in the worst frame | 40-245 ms → 2.4-3.8 ms |
| The same: the worst whole frame | 60-423 ms → 45-430 ms: Punchy's own streaming after a teleport (a teleport back to filled ground costs the same) |
| Time from the teleport to the first cast taken | 0.35-0.54 s → 0.26-0.57 s (the first teleport after start-up: 1.4 → 1.2 s) |
| Two peers 2 km apart, the host to the client's meadow and back, then one teleport into new ground each | 19 of 19 sums agree over 3 minutes, `desync=False` |

What's left on the main thread is 2-4 ms in the frame a tick stamps 16 fills (making each `ChunkGround` and its bytes),
over the brief's 2 ms. Stamping fewer a tick, or a frame budget, would bring it down; that's the deferred next step. A
client's first cast after its own teleport into new ground took 2.2 s (the host learns where it went from replication).

## Spells in Punchy (Punchy `chemistry-spells` 8def43e0, 2026-11-14)

The brief `punchy-cell-spells`: every spell whose effect is only on cells, cast in Punchy's open world and drawn with
Punchy's own effects ("make sure we use Punchy effects"). No core or rules change. The bridge now casts 28 spells: the six
it had and the 22 below. None needed bodies or buildings, so none were left out.

**Casting.** The pad's radial (hold d-pad up) is the core's `SpellRadial`, with the groups as its top ring and each
group's spells out round it (as D16's hands). Letting go casts the spell picked at the aim; with a trigger held, it
attunes that hand to the group's element instead. On the keyboard, comma and period step through the spells and M casts
the one shown. Casts are still session requests, and a landed cast's burst is drawn from its cells (`World.SpellCells`)
on the tick every peer runs it.

| Spell | Effect used | Punchy asset or new |
|---|---|---|
| Burning Hands, Flame Wave | a cone of flame rolling out from the hands | the standard fire (`FireSystem`) |
| Meteor | embers thrown up, a smoke column, a ring of dust, a flash | fumarole embers and smoke (`LandmarkPlume`), breath puffs, `LightBudget` |
| Falling Star | sparks over the mark, a falling star | `LandmarkPlume` sparks, the standard fire |
| Scalding Steam | steam | hot springs' steam (`LandmarkPlume`), the skunk's gas puffs |
| Lava Pool | molten ground, bubbles, embers, a glow | hot springs' bubbles and fumarole embers (`LandmarkPlume`), a fire light; the ground itself is **new** (overlay: a dark crust with churning glowing seams) |
| Lava meeting water | rock and steam | hot springs' steam; the rock is **new** (overlay: dark basalt, standing out of the water) |
| Ray of Frost | a beam | the arcing spires' bolt (`ArcBolt`), a pale straight copy |
| Flood | spray where the water lands | the waterfall's landing spray (`LandmarkPlume`) |
| Geyser | a spout | the geyser landmark's spout, steam and bubbles (`LandmarkPlume`) |
| Iceberg | ice | the existing ice overlay, frost puffs |
| Lightning Bolt, Shocking Grasp | a bolt to the nearest charged cell, arcs through every charged cell (water) | `ArcBolt`; the water's flicker is **new** (overlay) |
| Thunder Strike | sparks over the mark, a column of bolts | `LandmarkPlume`, `ArcBolt` |
| Rain Cloud | a dark cloud, rain | breath puffs, the weather's rain streaks and veils (`WeatherSystem.PrecipitationShader`) |
| Dust Storm | dust | breath puffs, the weather's dust veils |
| Sand Blast | sand flying along the line | breath puffs, dust veils; the sand left is **new** (overlay) |
| Mud | wet earth | **new** (overlay; grass flattened), brown splashes |
| Acid Splash | green bubbles and sparks | `LandmarkPlume` bubbles and sparks, tinted |
| Plant Growth | green sparkles, lush ground | the fairy ring's sparkles (`LandmarkPlume`); the ground and taller grass are **new** (overlay) |
| Spike Growth | thorns | **new** (overlay: grass tall and bramble-dark) |
| Grease | a slick | **new** (overlay: an amber film with a sheen) |
| Fog Cloud | fog | the skunk's gas puffs, the weather's veils, white |

What's new is only the overlay's two more maps (`_PunchyChemMap3`: lava, mud, a slick, rock; `_PunchyChemMap4`: growth,
thorns, sand, charge) and the lines that read them in the terrain, grass and water shaders. Every other look is a Punchy
material or system driven with new settings: our own buffer of `LandmarkEffectSystem.Emitter`s on its plume material (a
one-shot is a plume whose period starts now), `FloraSystem.BoltMesh` with `ArcBolt`, particle systems on the weather's
precipitation shader. Looks come from the cells and from a cast's cells; placement within a cell is a hash of the cell.

| Check (Windows, Mono development player, 1280 x 720) | Result |
|---|---|
| All 28 spells cast solo (`-chem-script "12:all"`) | all landed, 0 exceptions |
| Reactions seen | lava in water set into rock with steam; grease caught fire (231 slick cells lit in the big scene); lightning ran through Create Water's sheet (80 water cells charged at once); a rain cloud put out 34 burning cells; thorns caught fire (78); acid ate grease (16); shoots came up through ash |
| Two peers (`-host`, `-join 127.0.0.1`), each casting a mix every 9 s for 3 minutes | 19 of 19 sums agree (ticks 180-1,260), `desync=False`, 0 exceptions |
| The big scene (grease over a 16 x 16 m meadow, flooded, a fog, a Meteor into it), 10 s from the Meteor, uncapped | frame 11.8 ms on average (p95 16.3) with `-chem-plain`, 12.3 ms (p95 20.5) with the spells' effects; the bridge 0.78 → 1.12 ms a frame |
| Punchy's play-mode tests | 35 of 35 passed |

Demo clip: `unity/Builds/demo-videos/punchy-cell-spells.mp4` (25 s, 720p) and `.png`: lava into water, a grease fire,
lightning through flooded ground, a rain cloud on a grass fire, a Meteor into fog.
