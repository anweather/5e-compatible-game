Needs: unity
Area: unity/Assets/Prototype/Scripts/Net/ (NetChem*.cs, CommandChannel use, new NgoLockstepTransport.cs), its EditMode tests, packages/com.anweather.chemistry-core/Runtime/ (only small fixes the adapter needs, each with a dotnet test), docs/design/core-package/multiplayer-ports.md (notes)
Branch: lockstep-ngo

# D19 step 2: the NGO adapter (NetChem on LockstepSession), Andrew 2026-11-13

Read first:
- `docs/adr/0019-lockstep-in-the-core-behind-a-transport-port.md`;
- `docs/design/core-package/multiplayer-ports.md`, especially "The API the adapters call" and "Notes for the NGO adapter (step 2)";
- the core's half, which has landed (8beeb13d): `packages/com.anweather.chemistry-core/Runtime/Lockstep*.cs`.

Today `Scripts/Net/NetChem.cs` (about 1,000 lines) and `NetChem.Room.cs` hold the lockstep logic: stamping, history, catch-up, hash windows and joiners. Step 2 turns NetChem into a thin adapter:

1. **`NgoLockstepTransport : ILockstepTransport`**:
   - Bytes go over NGO custom messages or RPCs: reliable, ordered, fragmented by the session up to `MaxMessageBytes`.
   - Connection and disconnection callbacks feed `session.PeerJoined` and `PeerLeft`.
   - Every receive feeds `session.Received`.
   - The client id maps to `PeerId`.
2. **The world port** wraps ChemGrid: World, Rules, Prepare (`PrepareBatch`), Rebuild (`ResetWorld`, with the room re-noted), and Snapshot / Load (`JoinSnapshot`).
3. **NetChem keeps its public surface**, so callers don't change: the cast and effect intents, room commands, dragon, strikes, course, chunk plan, reset and the probe checksum. Underneath, each goes through `session.Request` / `Stamp` / `StampReset` on the registered channels. Follow the notes in multiplayer-ports.md:
   - a cast's `From` travels beside the request, and `Taken` gives its tick for `AnnounceCastRpc` and the shove;
   - the chunk plan goes in `Stamping(tick)`;
   - the dragon is stamped once a tick;
   - reach checks stay in the adapter;
   - `ResetIsReplay` goes.
4. **Late join is now by snapshot**, and a detected desync is recovered by snapshot. Log `desync.Text` as today's log line does.
5. **Delete the logic that moved:** the history, catch-up and hash windows in NetChem. Delete `CommandChannel` too, once nothing uses it (keep its `.meta` out of the package if it goes).

## Must not

- Merge or push `dev`.
- Change gameplay rules.
- Touch Punchy.
- Bump the version.

The protocol (and NET CONFIG) changes on purpose. It ships later as one release on all three platforms.

## Done when

- The build has 0 errors and 0 new warnings.
- EditMode (`tools/editmode.sh`) is green apart from the known skips.
- `dotnet test core` and `dotnet format core --verify-no-changes` pass.
- jscpd stays under 2% (`tools/jscpd/run.sh unity/Assets/Prototype sandbox/src`).
- These probes pass: `tools/probe.sh smoke`, `online` and `airpeers`, plus any other two-player probe in the probe's list.
- A late joiner reaches the same checksum.
- The line count of Net/ before and after goes in the report.

## Report

Five lines:
1. The commit.
2. EditMode N/N, and the probes run.
3. Net/ lines before → after.
4. What behaves differently from before.
5. Any blocker.

After: the lead lands the branch. Then a two-machine test, the Mac hosting and the PC joining over Relay, with `players=2` and `desync=False`, before any release. Then step 3 (the NfE adapter in Punchy).
