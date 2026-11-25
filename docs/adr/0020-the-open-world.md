# D20. The open world: chunks keyed by layer and place, made as players reach them (2026-11-13)

**Status:** Proposed (Andrew agreed the shape, 2026-11-13)

- **Context:** In D19 step 3, Punchy's chemistry is one 256 × 256 cell region that the host fills from its terrain.
  - A player who walks out of it has no chemistry.
  - In solo the region is rebuilt round the player, which loses what burned.

  Our game's `World` has fixed bounds (a dense chunk table sized by the map), and Punchy's world keeps changing size
  as Zachary builds it. Andrew wants each player's own reactions wherever they are, shared where reaches overlap. He
  also wants teleports to just work, and the least coupling to Punchy. He expects layered terrain at the same x, z:
  caves now, sky islands later.
- **Decision:** the core gets an **open world** beside today's bounded one.
  - **Addresses:** a chunk's key is **(layer, cx, cy)**: plain ints, negative allowed, with no bounds and no origin
    to agree. A **layer** is a sheet of terrain with its own cells (the surface 0, a cave layer, a sky layer). The
    host game numbers its layers.
  - **Coordinates:** the host game's own world position over its cell size (1 m in Punchy's bridge; our game's
    cells are 1.5 m), so cell = floor(x / size), floor(z / size) and chunk = floor(cell / 64).
  - **Growth:** a chunk exists only once it's **filled**. When a player's reach needs a chunk that hasn't been filled,
    the host asks its game what the ground is there and stamps a **fill** command on a tick. Every peer makes the
    same chunk on the same tick, so no float sampling has to agree across machines.
  - **Active and inactive:** chunks follow each player's reach. Chunks outside every reach are inactive and packed,
    as in D17. Nothing is lost when a player walks away.
  - **Teleports:** a teleport is a reach that jumps. Its chunks are filled where it lands, and areas meet by their
    keys, with no merge step.
  - **Joins and saves:** a snapshot carries only the filled chunks, in key order.
  - **Layers:** layers don't touch each other yet. Links between layers (a hole into a cave, water off a sky island)
    are later work.
  - **Our game is unchanged:** its maps stay bounded worlds.
- **Why:**
  - It needs nothing from Punchy but two answers: where each player is (layer and metres), and what the ground is at
    a cell.
  - Punchy can grow or change its world without our code knowing its size.
  - Teleports, caves and sky islands fall out of the key, not special cases.
  - Joiners get only what exists.
- **Reopen if:**
  - layers must exchange matter often enough that separate sheets cost more than one 3D world;
  - filled chunks pile up past memory (then evict far ones to a save);
  - a host game can't give the host a ground answer without its own floats disagreeing between peers. The fill
    command guards against this already, since only the host samples.
- **Sources:** `docs/design/core-package/open-world.md`; D17 (`0017-the-sparse-world.md`), D19
  (`0019-lockstep-in-the-core-behind-a-transport-port.md`, whose Punchy "shared region" this replaces).
