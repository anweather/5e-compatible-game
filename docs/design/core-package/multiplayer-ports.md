# Multiplayer as ports and adapters (D19, accepted 2026-11-12)

Andrew, 2026-11-12: "The real fun is gonna be playing with friends." The chemistry should keep in sync between
players in our game and in Punchy alike. The transport is Netcode for GameObjects (NGO, ours) or Netcode for Entities
(NfE, Punchy).

## What's true today

- **The core is already deterministic, and our game already plays in lockstep** (D7). Peers exchange only commands,
  tick batches, hash windows, and the history a joiner needs. Tonight's 0.1.13 join test (Mac host, PC client,
  desync=False) is this.
- **The lockstep logic lives in the view's NGO layer, not in the core.** Of `Net/NetChem.cs`, `NetChem.Room.cs` and
  `CommandChannel.cs` (~1,280 lines), about **600-700 lines are pure lockstep logic**:
  - the host clock and tick stamping;
  - the history lists;
  - `Advance` and catch-up;
  - the hash window and its compare;
  - the joiner queue;
  - reset truncation.

  They're wrapped in `NetworkBehaviour` RPCs and `INetworkSerializable` structs. `CommandChannel` already says "no
  Unity in here".
- **The core already has the pieces underneath:**
  - `TickBatch` (with `Timeline`), `CastCommand`, `RuleChange` and `RoomCommand`/`RoomPolicy`;
  - `RollingHash`, `StandKeeper` and the hash helpers;
  - `World.StepSome(over)`;
  - and `JoinSnapshot`: a snapshot late-join format that is tested but unused, because the game replays full history.
- **Punchy (NfE 6.5)** gives us what we need:
  - a listen-server host (server and client worlds in one process), Relay join codes, up to 8 players;
  - client-authoritative movement with no prediction rollback;
  - reliable, ordered `IRpcCommand`s that managed code can already send (`Items.cs` does: `SendToHost`, `Broadcast`, a
    receive loop, a `sentTo` set for late joiners).

  RPCs aren't fragmented, so big payloads need splitting into pieces of about 1 KB.

## The design

### In the core (package, no Unity): `Chem.Lockstep`

**Ports**, the interfaces a host game implements:

```csharp
public interface ILockstepTransport          // reliable, ordered, bytes only
{
    LockstepRole Role { get; }               // Solo, Host, Client
    int MaxMessageBytes { get; }             // the core splits anything bigger
    void SendToHost(ReadOnlySpan<byte> message);
    void SendTo(PeerId peer, ReadOnlySpan<byte> message);
    void Broadcast(ReadOnlySpan<byte> message);
    // The adapter calls back into the session: Received(peer, bytes), PeerJoined(peer), PeerLeft(peer).
}
```

**Driving inputs:** the host game calls `session.Update(elapsedSeconds, over)` once a frame.
- Only the host's clock paces ticks. `elapsedSeconds` replaces `Time.deltaTime`.
- `over` is the existing `StepSome` work budget.

**Identity:** `PeerId` (an int the adapter maps to its own connection id) plus a display name.

**The application core:** `LockstepSession`, one per process, built from today's NetChem logic.
- **Host role:**
  - takes intents;
  - stamps them to `hostTick + 1 + delay`, checking them with `RoomPolicy`;
  - builds each tick's batch, broadcasts it and keeps history;
  - serves joiners;
  - sends a hash window every `RollingHash.Every` ticks.
- **Client role:**
  - queues batches with `CommandChannel`;
  - `Advance`s only through batches it holds;
  - checks the hash windows;
  - raises `Desynced`.
- **Solo:** a loopback transport. One code path for solo and online, so Punchy solo uses it too.
- **Events out:** `TickApplied`, `CaughtUp`, `Desynced(what)`, `Refused(command, why)`.
- **A wire codec:** a deterministic binary encoding of intents, batches, hash windows and snapshot pieces, with a
  version byte and the rules fingerprint in a hello. It replaces the `INetworkSerializable`/`FixedString` intent
  structs. It's plain C#, so every transport carries the same bytes.
- **Late join by snapshot.** The host sends `JoinSnapshot` (the world save, the mods and a checksum) in pieces, then
  live batches. That replaces replaying the whole history: it's bounded and fast, and it's the only option that works
  for Punchy, whose region is filled from its terrain. The same path gives us **desync recovery**: on a mismatch, the
  host resends a snapshot (today a desync is only logged).
- **Game commands stay pluggable.** Our golf, dragon, defense, combos and strikes ride as named command channels
  registered with the session (they already exist as `TickBatch.Add*`). Punchy registers only casts.

### Adapters (outside the core)

| Adapter | Where | Size (est.) | Shape |
|---|---|---|---|
| **NGO** | our game, `Scripts/Net/` | ~200 lines | one `NetworkBehaviour` with two RPCs carrying `byte[]` (to the server, to clients); the map and start metadata go into the hello instead of `NetworkVariable`s. NetChem shrinks to this plus view glue (cast animation, knockback, EventLog) |
| **NfE** | Punchy, the bridge folder | ~250 lines | `ChemMsgRpc : IRpcCommand` with a `FixedBytes` piece (~1 KB) and a sequence number; a MonoBehaviour pump in the `Items.cs` style (`NetWorlds.Server` / `NetWorlds.Client`); the session runs once per process, and the host's own client ignores the echo. It hooks `GameBootstrap.WorldsReplacing` to rebuild on host, join or solo |
| **Loopback** | the core | ~60 lines | solo, plus every two-peer and late-join test under `dotnet test` |

### Punchy also needs

- **A shared region instead of a window per player.** The core's chunks follow each character's reach (the core
  already has chunk activation and reach).
- **The host fills the region** from `TerrainSource` and ships it in the snapshot, so float sampling never has to
  agree across machines.
- **Casts are sent as an RPC** of cell, spell and charge, never as an input-component field: Punchy's own rule says a
  one-frame press is lost on a slow host.

## Plan (strangler: our game keeps working at every step)

1. **The core's half (no Unity; u1 or u2):**
   - move `CommandChannel` into the package;
   - add the wire codec, `LockstepSession`, the loopback transport and snapshot pieces;
   - port NetChem's logic and tests: two peers in sync, a joiner by snapshot, a desync detected then recovered, input
     delay, reset.

   All of it runs under `dotnet test`, with no Editor, which is a big gain on today's probe-only online tests.
2. **The NGO adapter (PC):** NetChem becomes the adapter. Verify with `probe.sh online`, `airpeers` and the Mac-PC
   join. The protocol changes (NET CONFIG changes), so it ships as one release on all three platforms.
3. **The NfE adapter and the shared region (Punchy branch):** two peers on one PC (Punchy's `-join` on a second copy),
   then PC and Mac over Relay.
4. **Later:** adaptive input delay, a fixed 64-bit hash in place of strings, and recovering a desync by snapshot in
   the open.

## Open questions

- **The float risk:** the sim uses doubles in spread chances, weather and dryness. Mono on Windows and macOS agree
  (tested). Punchy's player may be IL2CPP; compare a Mono peer with an IL2CPP peer early (step 3).
- **Bandwidth:** a join snapshot of a 256 × 256 region is a few hundred KB; at ~1 KB per RPC that's a few hundred RPCs
  over a few seconds. Fine for the experiment; compress later.
- **Mixing a game with another:** out of scope. Our game and Punchy never join each other.
