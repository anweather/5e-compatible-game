Needs: unity (Punchy's Editor)
Area: `C:/Users/Andrew/Development/Punchy-chemistry` (Assets/Game/Scripts/ChemistryBridge/ and a new NfE adapter beside it; Net/GameBootstrap.cs only for the hook it needs); in our repo only small core fixes the adapter needs, each with a dotnet test; docs/design/core-package/multiplayer-ports.md (a "Step 3 as built" section)
Branch: Punchy `chemistry-lockstep` off `chemistry-mvp` (local, never pushed); core fixes, if any, on our `lockstep-nfe` off `origin/dev`

# D19 step 3: the NfE adapter and the shared region in Punchy, Andrew 2026-11-13

Read first:
- `docs/adr/0019-lockstep-in-the-core-behind-a-transport-port.md`;
- `docs/design/core-package/multiplayer-ports.md`: the NfE row of "Adapters", "Punchy also needs", "The API the adapters call", and "Step 2 as built" (the NGO adapter is the worked example: `unity/Assets/Prototype/Scripts/Net/NgoLockstepTransport.cs`, `NetChem.cs`);
- the core: `packages/com.anweather.chemistry-core/Runtime/Lockstep*.cs` (`LockstepWorld` is the world port for a world the core builds alone);
- Punchy: its `CLAUDE.md`, `docs/agents/multiplayer.md`, `Assets/Game/Scripts/Net/GameBootstrap.cs`, `Assets/Game/Scripts/Items/Items.cs` (how managed code sends RPCs today), and the bridge (`ChemistryBridge*.cs`).

Punchy reads the core by `file:` path from `C:/Users/Andrew/Development/5e-compatible-game/packages/com.anweather.chemistry-core`, which is on `dev` (D19 steps 1 and 2 are in). Don't switch that checkout's branch. If the adapter needs a core fix, make it in a worktree of our repo on `lockstep-nfe`, with a dotnet test, and point Punchy's manifest at that worktree's package while you test (say so in the report).

## The job

1. **`NfeLockstepTransport : ILockstepTransport`** in Punchy's bridge folder (~250 lines):
   - `ChemMsgRpc : IRpcCommand` carrying a `FixedBytes` piece (~1 KB) and a sequence number; `MaxMessageBytes` matches, the session splits anything bigger.
   - A MonoBehaviour pump in the `Items.cs` style (`NetWorlds.Server` / `NetWorlds.Client`): send to host, to one peer, to everyone; receive in a loop; hand what arrives to `session.Received` from the pump's Update, never inside a system.
   - Peer joined and left from NfE's connections, mapped to `PeerId`. One session a process; the host's own client ignores the echo.
   - Hook `GameBootstrap.WorldsReplacing` to rebuild the session on host, join or solo. **Solo runs through the core's loopback**, so there's one code path.
2. **The shared region.** Today's bridge keeps a window per player; make it one region the host fills from `TerrainSource` and ships in the join snapshot, so a client never samples floats to build its cells. The core's chunks follow each character's reach.
3. **Casts go as a request on the casts channel** (cell, spell, charge), never as an input-component field (Punchy's rule: a one-frame press is lost on a slow host). The host stamps; every peer applies on the tick. Effects (flames, steam, ice, craters and height edits) follow from the cells as in solo.
4. **Desync:** log `desync.Text`; recovery by snapshot comes from the core.

## Must not

- Touch our game's `unity/` or push anything in Punchy (it stays local).
- Merge or push our `dev`; change rules.
- Change Punchy's own netcode for players, items or sky beyond the hook.

## Done when

- Punchy compiles with 0 errors and 0 new warnings; its existing tests pass (`docs/agents/testing.md`).
- Solo plays as before: Fireball on a meadow spreads, Frost freezes the lake (`-chem-demo` for a smoke run).
- **Two peers on one PC:** a `-host` player build and a second copy with `-join 127.0.0.1`. A cast on the host and a cast on the client both burn on both screens; checksums agree for 3 minutes (`desync=False` in both logs).
- **A late joiner** that joins while fire is spreading reaches the same checksum.
- **Mono against IL2CPP** (the float risk in the ADR): if the player build is IL2CPP, run one peer in the Editor (Mono) against the player and say whether they agree. If it's Mono, say so.
- dotnet test core and dotnet format pass for any core change.
- A "Step 3 as built" section in `multiplayer-ports.md`, in our docs (commit it on `lockstep-nfe`).

Not in this job: PC and Mac over Relay (the next step, with Andrew).

## Report

Five lines:
1. The Punchy commit (and our `lockstep-nfe` commit, if any).
2. Tests, and the two-peer, late-join and Mono/IL2CPP results.
3. Adapter line count.
4. What behaves differently from solo.
5. Any blocker.

After: the lead merges `lockstep-nfe` (if any) into dev; then PC and Mac over Relay.
