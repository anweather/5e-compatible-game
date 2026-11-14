Needs: unity (Punchy's Editor)
Area: `C:/Users/Andrew/Development/Punchy-chemistry` (Assets/Game/Scripts/ChemistryBridge/); in our repo only small core fixes, each with a dotnet test, and a "Punchy as built" section in docs/design/core-package/open-world.md
Branch: Punchy `chemistry-open-world` off `chemistry-lockstep` (pushed to Skeleman/Punchy, J-092); core fixes, if any, on our `open-world-fixes` off `origin/dev`

# D20 plan step 2: Punchy's chemistry as an open world, Andrew 2026-11-13

Read first:
- `docs/adr/0020-the-open-world.md`;
- `docs/design/core-package/open-world.md`, all of it, and especially "Core as built" (the API: `World.OpenWorld`,
  `IOpenGround`, `OpenWorldPlanner.Attach`, `LockstepChannels.Fills()`, `ChunkGround`);
- D19's Punchy notes in `docs/design/core-package/multiplayer-ports.md` ("Step 3 as built");
- Punchy: the bridge (`ChemistryBridge*.cs`, especially `ChemistryBridge.Lockstep.cs` and its `RegionSource`), its
  README, `WORLD.md`, and `docs/agents/world.md` and `interiors.md` (how caves and interiors are placed).

The core with the open world is on our `dev` at 929cbc78 (landed), and Punchy reads it from the main checkout,
`C:/Users/Andrew/Development/5e-compatible-game`, which is on it. Don't switch that checkout's branch.

## What Andrew wants

Each player has their own chemistry wherever they are, shared where reaches overlap. The grid grows as people
explore, with no size of Punchy's world in our code. Teleports just work. Caves now, and later sky islands, are
**layers** at the same x, z.

## The job

1. **Drop the region.** The bridge's world becomes `World.OpenWorld`. No 256 × 256 window, no hello region, and no
   re-centring in solo: solo, host and client all run the open world through the session (loopback in solo).
2. **`IOpenGround` over Punchy** (host and solo only):
   - `Characters()`: every player (the host's own and each NetPlayer on the server), with its layer and its cell from
     its absolute world position (1 m cells, floor). Check whether Punchy ever shifts its origin; if it does, use the
     absolute position.
   - `Sample(key)`: the chunk's ground from `TerrainSource`, as `RegionSource` decides each cell's kind today.
3. **Layers:** the surface is layer 0. If Punchy's caves or interiors are spaces at the same x, z (or reached by a
   teleport into another space), give them their own layer number and put a character in a cave on that layer. If
   Punchy has no such space yet, say so and keep layer 0 only; the number is still passed through.
4. **The view** (burnt grass, ice, flames, steam, craters and height edits) reads cells by world position across
   every filled, active chunk, not one window. Inactive chunks keep their look.
5. **Casts** keep going as session requests. A cast where no chunk is filled yet is refused or waits a tick: say
   which.

## Must not

- Touch our game's `unity/`, merge or push our `dev`, or change rules.
- Change Punchy's netcode for players, items or sky beyond the bridge.
- Push Punchy's branch until it passes (then push `chemistry-open-world` to Skeleman/Punchy, J-092).

## Done when

- Punchy compiles with 0 errors and 0 new warnings; its tests as before (Landmarks_V3 and
  IslandCache_LoadedEqualsFresh already fail on chemistry-mvp).
- **Solo:** Fireball on a meadow spreads; walk ~300 m away and back, and the burnt ground is still there (no rebuild).
  Frost freezes a lake.
- **Two peers on one PC far apart:** a `-host` build and a `-join 127.0.0.1` copy, with each player taken to a
  different place (more than 1 km apart: Punchy's `-place <name>` or a teleport). Each casts a Fireball on its own
  meadow, both fires burn, then one player teleports or walks to the other. Checksums agree throughout
  (`desync=False`), 3 minutes at least.
- **A late joiner** after both fires have burned gets both areas and the same checksum.
- **A teleport** into ground never filled has chemistry within a second or two.
- **A cave layer**, if Punchy has caves: a cast in a cave under a burnt meadow is in its own layer.
- Memory: filled chunks and the process's memory after 5 minutes of walking, in the report.
- A "Punchy as built" section in `open-world.md` (commit it on our `open-world-fixes`, a docs-only branch if there are
  no fixes).

Not in this job: the Mac and PC over Relay (next, with the Mac).

## Report

Five lines:
1. The Punchy commit (pushed?) and our branch's commit.
2. Tests, and the results for solo, far apart, late joiner, teleport and cave.
3. The bridge's line count before → after.
4. What behaves differently, with memory numbers.
5. Any blocker or call for Andrew.

After: the lead lands `open-world-fixes`, then the Mac and PC over Relay with both players far apart.
