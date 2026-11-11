Needs: unity
Area: unity/Assets/Prototype/Scripts/Net/NetChem.cs (and NetChem.Room.cs), ChemGrid.cs (binding a loaded World), a probe
Branch: snapshot-join-net

# Late join from a snapshot: the net layer

The core half is on dev (`JoinSnapshot`, docs/design/player-saves.md, step 6). A joiner can load the host's world as it
stood at tick T, and the load is checked against the host's checksum before any tick. It then replays only the
batches after T, instead of the whole history from tick 0 (or from the last reset).

Today a joiner asks for the past (`RequestHistoryRpc`) and the host sends it in six RPCs (`SendHistory`). The joiner
builds a `TickBatch.Timeline` from tick 1 and replays it (`SendHistoryRpc`, `CaughtUp`), so joining takes longer the
longer a session has run. Build the net layer for snapshots. Keep today's full replay as the fallback; don't remove it.

## What to build

1. **The host takes a snapshot when a peer asks to join.**
   - Take it between ticks, after the tick it has run (`hostTick`):
     `JoinSnapshot.Take(grid.World, grid.World.Source, rules)`, where `rules` is the `RuleSet` the host is running.
   - **Off the main frame:** taking it is 77 to 88 ms on the valley and 115 to 310 ms on the nine (Release,
     .NET 8). The core makes the save text between ticks, so either give the snapshot a frame of its own (the
     tick waits a frame), or measure first and move it to a worker if the frame budget can't take it.
     - A worker can't read the World while the tick runs (the sparse World's guard, `OwnThread`), and the save
       reads every chunk.
     - So a worker version needs either the tick paused for that frame, or a copy taken between ticks. The copy is
       the save itself, so in practice: pause one frame.
     - Measure in a player build on the nine and say which you chose.
   - **Then gzip it** (`System.IO.Compression.GZipStream`). That's 40 to 53 KB for the valley and 60 to 75 KB for
     the nine.
2. **The transport.** One snapshot is past one NGO message (the default MTU is about 1.4 KB, and an RPC carries far
   less than 64 KB).
   - Send it on a named message channel (`CustomMessagingManager`, `NetworkDelivery.ReliableFragmentedSequenced`),
     or split it yourself into numbered pieces with a total and a hash, put back together in order on the joiner.
   - Say which one you used, and what a 75 KB send costs the host's frame and the network on the PC.
   - Send the batches after T as today's live batches do. The joiner holds them until its snapshot has loaded.
3. **The joiner joins.**
   - It decompresses the snapshot, then calls
     `JoinSnapshot.Parse(text).Join(baseRules, source, ValleyChemistry.LandOf(world), budgets)`, where `baseRules`
     and `budgets` are what the room's rules are built from (`ChemGrid.StartRules`, `ModBudgets`).
   - **Bind the view to the loaded World as `ChemGrid.ResetWorld` does** (cells, things, stands, the drawn tier,
     `WorldReset` listeners), but from the loaded World, not `StartWorld`. That means a `ChemGrid` path that takes a
     World instead of building one. `ResetIsReplay` semantics stay.
   - Then run the batches from T + 1 with the usual checksums (`RollingHash`, `CheckHash`).
   - `replayedUpTo`, `knownUpTo` and `tickBase` start at T, not at the timeline's start.
   - Wizards and props come from the World's stands and things. `NetWizard` and `NetProp` must not need the history
     to place themselves.
4. **Falling back.** On a `SaveException` from `Parse` or `Join`:
   - log its message (it names the part: the map, the rules, the land, or which checksum part didn't come back);
   - ask for today's history (`RequestHistoryRpc`) and replay as now.

   A desync later is caught by the checksums as today.
5. **The host's history.** Once snapshots work, the host may keep the history only from the last snapshot it sent,
   and `StandKeeper` the strikes only from then. That's a later step: keep the full history in this one, so the
   fallback has something to send.

## The probe that proves it

A two-peer probe, like `ProbeOnline`:
- The host plays the valley for at least 600 ticks: casts, fire, water, a rule switch (a mod on and off), the
  dragon summoned and gone, a character crossing chunks.
- A virtual player joins at tick ~400 by snapshot.
- Report, from the host's side:
  - the snapshot's bytes raw and gzipped;
  - the host's worst frame while taking and sending it;
  - the joiner's time from asking to caught up;
  - its checksum at every 30th tick after joining, against the host's (all equal).
- A second run joins by full replay (snapshots switched off) and reports the same, for comparison.
- A third run joins with a deliberately wrong land (or a mod the host doesn't have) and shows the refusal message,
  then the fallback replay catching up.

## Done when

- The probe passes on the PC, all three runs.
- EditMode and the core tests pass (`dotnet test core`).
- No change in solo play.
- A short report: the numbers above, the transport chosen, what you did about the host's frame.

## Questions still open

1. **Every join, or only past some length of session?** A snapshot costs the host a hitch of ~100 to 300 ms. A replay
   of a short session may be quicker and has no hitch. Suggest a threshold from the probe's numbers.
2. **A guest's copy (question 3 in player-saves.md):** may a guest keep the snapshot as a save of the host's world?
   Not part of this brief: the joiner holds it in memory only.
3. **Rooms with mods the joiner hasn't got.** The snapshot carries the room's mods in its save (and the mods outside a
   room in its own list), so a joiner needs no mod files. Check that `ModBudgets` on the joiner checks them as the
   host's did, and say if a budget difference refuses a join.
