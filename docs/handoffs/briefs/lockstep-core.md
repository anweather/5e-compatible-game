Needs: core
Area: packages/com.anweather.chemistry-core/Runtime/ (new Lockstep*.cs files, CommandChannel.cs moved in), unity/Assets/Prototype/Chemistry/Tests/ (new Lockstep*Tests.cs), core/Game.Core/Game.Core.csproj (only if a glob must change), docs/design/core-package/multiplayer-ports.md (notes), docs/GLOSSARY.md (new entries only)
Branch: lockstep-core

# Lockstep in the core, the core's half (D19, Andrew 2026-11-12)

Read first: `docs/adr/0019-lockstep-in-the-core-behind-a-transport-port.md` and
`docs/design/core-package/multiplayer-ports.md` (the design: ports, `LockstepSession`, wire codec, snapshot join,
loopback, plan step 1). Background: D7 (`docs/adr/0007-*`), the core's CLAUDE.md
(`packages/com.anweather.chemistry-core/CLAUDE.md`), and today's game-side logic you are porting:
`unity/Assets/Prototype/Scripts/Net/NetChem.cs`, `NetChem.Room.cs`, `CommandChannel.cs`.

The core is now a Unity package (`packages/com.anweather.chemistry-core/Runtime/`, tag chemistry-core-0.1.0); our
game and a second game (Punchy, Netcode for Entities) both use it. Today ~600-700 lines of pure lockstep logic sit in
the view's NGO layer. This job builds the transport-free half in the core. **Don't change the game's Net/ code or any
Unity file**: the NGO adapter (step 2) is the PC's job after this lands, so NetChem keeps working unchanged meanwhile.

## Build (namespace `Chem.Lockstep` or `Chem`, C# 9, netstandard2.1, no Unity, deterministic)

1. **Move `CommandChannel.cs`** from `unity/Assets/Prototype/Scripts/Net/` into the package Runtime **only if** the
   game keeps compiling against it unchanged (same namespace/type); otherwise copy its logic into the core and leave
   the game's file for step 2 (say which you did). Keep the `.meta` GUID if moved.
2. **Ports**: `ILockstepTransport` (Role Solo/Host/Client; `MaxMessageBytes`; `SendToHost`, `SendTo(PeerId)`,
   `Broadcast` of bytes; the session exposes `Received(peer, bytes)`, `PeerJoined`, `PeerLeft` for the adapter to
   call). `PeerId` value type plus a name.
3. **Wire codec**: deterministic binary encode/decode for: hello (protocol version, rules fingerprint, map id, start
   settings), intents (cast, effect, room, and the game's named channels: combos, strikes, defense, dragon, course as
   they exist in `TickBatch`), tick batches, hash windows (prefer a fixed 64-bit hash over the string), snapshot
   pieces. Split messages over `MaxMessageBytes` and reassemble in order. No floats in command payloads (today's
   `CastIntent.From` Vector3 is view-only: leave it out of the command, note it for the adapter).
4. **`LockstepSession`**: host role (take intents, check `RoomPolicy`, stamp to `hostTick + 1 + clamp(delay,0,30)`,
   build and broadcast each tick's batch, keep history for resets, hash window every `RollingHash.Every` ticks,
   serve joiners); client role (queue with CommandChannel, `Advance` only through known batches, fill gaps as today,
   `StepSome(over)` budget, compare hashes, raise `Desynced`); solo through the loopback. Pacing input:
   `Update(double elapsedSeconds, Func<bool> over)`; only the host's elapsed time paces ticks (ticks per second from
   the rules, as NetChem does). Events: TickApplied, CaughtUp, Desynced(what differs), Refused(command, why).
   Game command channels pluggable (register by name), so a game without dragons registers only casts.
5. **Late join and desync recovery by `JoinSnapshot`** (core, today unused by the game): the host sends a snapshot in
   pieces, then live batches; a client that desyncs asks for (or is sent) a fresh snapshot and carries on.
6. **Loopback transport** (in the core): N peers in one process, with optional latency/reordering-free delivery
   delays per peer, for solo play and tests.

## Tests (under `dotnet test core`, new files in Chemistry/Tests/)

Two peers via loopback stay hash-equal for 600 ticks with casts from both; a joiner at tick 300 by snapshot reaches
the same hash; an injected desync (corrupt one cell on a client) is detected within one window and recovered by
snapshot; input delay (a cast stamped 3 ticks ahead lands on the same tick on both); a reset; a refused room command;
messages split at a small `MaxMessageBytes` (e.g. 256) reassemble. Use the existing helpers (`ChemistryTests.R`, small
worlds). Mirror NetChem's behaviour; where you change it (e.g. snapshot instead of history), say so.

## Gates and landing

`dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes`, jscpd
(`npx --yes jscpd@4.0.5 unity/Assets/Prototype sandbox/src packages`). New core C# needs a Unity compile before it
lands: push the branch and stop (land.sh here will say so); the PC compiles and lands it. Report: the commit, test
counts, what moved, what changed from NetChem's behaviour, and the API the NGO and NfE adapters will call (a short
sketch in multiplayer-ports.md).

After: the PC does step 2 (the NGO adapter: NetChem on `LockstepSession`); the Punchy NfE adapter is step 3.
