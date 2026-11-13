Needs: core
Area: packages/com.anweather.chemistry-core/Runtime/ (new OpenWorld*.cs / ChunkKey.cs, the fill command and channel, Lockstep* only where the open world's port and fill plug in), unity/Assets/Prototype/Chemistry/Tests/ (new OpenWorld*Tests.cs), docs/design/core-package/open-world.md (a "Core as built" section), docs/GLOSSARY.md (entries only)
Branch: open-world-core

# The open world, the core's half (D20, Andrew 2026-11-13)

Read first: `docs/adr/0020-the-open-world.md` and `docs/design/core-package/open-world.md` (the design and plan step 1).
Background: D17 (`docs/adr/0017-the-sparse-world.md`, packed chunks), D19 and its as-built notes
(`docs/design/core-package/multiplayer-ports.md`, `Runtime/Lockstep*.cs`), and the core's
`packages/com.anweather.chemistry-core/CLAUDE.md`. Punchy's step-3 bridge is the first user (a 256 × 256 region today;
`LockstepWorld` is its world port).

Build the open world beside the bounded one:

1. **`ChunkKey(Layer, X, Y)`**: ints, negative allowed, floor division for cells to chunks.
2. **An open `World`:** chunks in a table keyed by `ChunkKey`, a chunk existing once filled. Walks in sorted key order
   (step, hash, snapshot, plan). A neighbour chunk not filled acts as a wall, as a map's edge does. Pick the cheapest
   shape that keeps bounded worlds' hot paths as fast: measure a bounded world's step before and after, under dotnet.
3. **`FillChunk(key, ground)`**, a command on its own lockstep channel, applied on its tick. The ground is what the
   host game sampled: a cell's kind, plus what Punchy's bridge seeds today (see `RegionSource` in Punchy's
   `Assets/Game/Scripts/ChemistryBridge/ChemistryBridge.Lockstep.cs`, read-only), packed small.
4. **The port, `IOpenGround`** (`Characters()` with layer and cell, `Sample(key)`), asked on the host and in solo
   only. The session's plan works out reaches by layer, fills one ring ahead of every active chunk, and stamps the
   fills and the active/inactive switches on the tick.
5. **Snapshots and hashes** over filled chunks in key order. A joiner gets them, then fills by tick.

## Tests (dotnet)

- Two peers far apart (chunks ~1,000 apart) each burn their own meadow: both stay in sync, and the burnt ground is
  still there after its reach leaves and comes back.
- A teleport: the reach jumps, chunks are filled at the destination, and the chemistry runs there within a few ticks.
- Two areas grow toward each other until they meet: fire crosses the join.
- A joiner mid-fire reaches the same hash.
- A cave layer (layer 1) under a burning meadow at the same x, y stays untouched.
- Negative coordinates and a chunk at (0, −1).
- The bounded worlds' goldens (`CellHash` in `TerrainTests` / `ChemistryTests`) are unchanged.

## Must not

- Touch Punchy (it's the PC's job after this) or the game's `unity/Assets/Prototype/Scripts/`.
- Change rules or the bounded worlds' behaviour.
- Merge or push `dev`. Push your branch.

## Done when

- `dotnet build core -c Release -warnaserror`, `dotnet test core` and `dotnet format core --verify-no-changes` pass.
- jscpd stays under 2%.
- A bounded world's step costs the same, before and after (numbers in the report).
- A "Core as built" section is in `open-world.md`, with the API the Punchy bridge calls.

## Report

Five lines: the commit; tests N/N; a bounded step before → after; the API in one line; any blocker.

After: the PC compiles it in our Editor (EditMode), the lead lands it, then the Punchy half (plan step 2).
