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

## Step 1 as built (`lockstep-core`, 2026-11-13)

All of it is in the package's `Runtime/`, namespace `Chem.Lockstep`, with no Unity and no floats on the wire:

| File | What it is |
|---|---|
| `LockstepPorts.cs` | `LockstepRole`, `PeerId`, `ILockstepTransport`, `ILockstepWorld`, and `LockstepWorld` (the world port for a world the core builds alone: tests, Punchy) |
| `LockstepWire.cs` | `LockstepWriter` / `LockstepReader` (varints, UTF-8 strings with null apart from empty, fixed 64-bit hashes), `LockstepFrames` (splits past `MaxMessageBytes`, joins pieces by message number), `Fnv1a64` |
| `LockstepMessages.cs` | the message kinds; `LockstepHello`, `LockstepDesync`, `LockstepRefusal`, `LockstepTaken` |
| `LockstepChannels.cs` | `LockstepChannel<T>` and the standard channels, TickBatch's lists: casts, effects, chunks, defense, combos, dragon, course, strikes, room |
| `LockstepSession*.cs` | the session: shared part, host, client |
| `LockstepLoopback.cs` | N peers in one process, a delay in pumps per peer |
| `CommandChannel.cs` | **moved** from `Scripts/Net/` with its `.meta` (same GUID, still the global namespace), so NetChem compiles unchanged. The session doesn't use it (below); it goes when step 2 retires NetChem |

Tests: `Chemistry/Tests/LockstepWireTests.cs` (Unit) and `LockstepTests.cs` (Simulation): two peers equal for 600 ticks
with casts from both, a joiner by snapshot at tick 300, a painted cell caught and put right by a snapshot, a cast 3
ticks ahead (and one past the most) on the same tick everywhere, an online reset (and a joiner after it), refused
commands, everything split at 256 bytes, solo, and a peer with other channels told why it can't join.

### What changed from NetChem

- **One message a tick.** The batch carries every channel's commands, so nothing arrives before its batch and
  `CommandChannel`'s waiting and delivering aren't needed. The host runs its own batch from the bytes it broadcast.
- **No history.** A joiner gets a snapshot (`JoinSnapshot`) taken between two ticks, then the batches the host had
  stamped past it (it holds the ones its own world hasn't run), then the live ones. So `StandKeeper`'s compaction, the
  reset's history truncation, the room's start and the history RPCs all go; a joiner no longer waits for a stamped
  reset to arrive, only for a moment between ticks.
- **Hash windows** go as their own message: 8 parts (`JoinSnapshot.PartNames`), each FNV-1a 64 of NetChem's string,
  so `Desynced` names the parts that differ. Painting a cell shows in `cells` and `dragon` (its audit counts changes).
- **Desync recovery**: the client asks for a fresh snapshot (`RecoverDesyncs`, on by default) and plays on from it.
- **Any channel may take an input delay** (0 to 30, as casts did).
- **Refusals go to everyone on every channel** (NetChem told everyone only of room refusals; it dropped a released
  build's moment quietly). A client asking on a host-only channel (effects, chunks, dragon) is refused at once.
- **A find** (`RoomChange.Found`) is the host's to stamp (`Stamp`); one asked for is refused.
- **The hello** replaces the `hostMap` / `hostStart` NetworkVariables, and the client checks the protocol, the base
  rules' fingerprint and the channel names before it asks to join.

### The API the adapters call

```csharp
// Once, as the network starts (host, client or solo):
var session = new LockstepSession(transport, world, localPeer);   // world: ChemGrid's port (NGO) or LockstepWorld (NfE)
var casts = session.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));   // same channels, same order, everywhere
// ours also: Effects, Chunks, Defense, Combos(name, devBuild), Dragon, Course, Strikes, Room(LockstepChannels.PeerName)
session.MapId = ChemGrid.MapId; session.Start["spawn"] = SpawnPoints.StartIn ?? "";   // host: the hello
session.HelloReceived += hello => { /* client: on another map, load the host's, then session.Join() (AutoJoin = false) */ };

// The transport's callbacks:
session.PeerJoined(peer, name);          // host: a connection came (sends the hello)
session.PeerLeft(peer);                  // then: session.Stamp(strikes, StrikeCommand.Leave(PlayerName(peer)))
session.Received(peer, bytes);           // every message, either way

// Every frame:
session.Update(Time.deltaTime, over);    // only the host's elapsed paces ticks; over is the step budget

// Game input:
session.Request(casts, command, delayTicks);  // anyone; the host names the player and may refuse
session.Stamp(effects, effect);               // host only, as is: effects, a Leave, a find (RoomCommand.Found)
session.StampReset(ResetCountdownTicks);      // host only

// Events: TickApplied(batch) (shoves, NoteTickDone, the probe's checksum: session.Checksum()), CaughtUp(tick),
// Desynced(desync) (desync.Text for the log), Refused(refusal) (grid.NoteRefused for the room channel),
// ResetComing(tick), Joined(tick), Failed(why), and on the host Taken(taken) and Stamping(tick).
```

Notes for the NGO adapter (step 2):

- **The world port** wraps ChemGrid: `World`, `Rules` (CurrentRules), `Prepare` (`PrepareBatch`), `Rebuild`
  (`ResetWorld`, with the room re-noted), `Snapshot` / `Load` (`JoinSnapshot` over the map's source and land).
- **A cast's `From`** (a Vector3, the centre of its shove) is view-only and stays out of the command: send it beside the
  request, and the host's `Taken` gives the tick it lands on, for `AnnounceCastRpc` and the shove after `TickApplied`.
- **The chunk plan** goes in `Stamping(tick)`: plan, and `Stamp(chunks, ...)` each switch; it lands on that tick. On a
  reset the plan starts again from the start's reach, as `StampResetRules` did.
- **The dragon** is `Stamp(dragon, new DragonCommand())`, once a tick (`OnePerTick`).
- **Strikes and swings** keep their reach checks in the adapter, then `Stamp(effects, ...)`.
- `ResetIsReplay` goes: nothing is replayed any more.
- NET CONFIG changes: one release on all three platforms, as planned.
