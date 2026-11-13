# A mod switched on stalls a frame (#155)

Status: design and a bench, nothing changed in the game. #155: switching a mod on in play stalls one frame for 115 to
182 ms (the PC, 2026-11-04), with 46 to 59 MB of garbage. Since then the rule switch's own work is gone (#181: no pass
over the cells; the view redraws only what changed), and #210 measured what's left: about 85 to 91 ms of
`World.ApplyRoom` switching the mod on, the same on a 64 x 64 world as on the nine.

## Where the time goes

The bench is `Chemistry/Tests/ModSwitchBench.cs` ([Explicit]; Release, .NET 8, this Linux VM). It builds the valley or
the nine as ChemGrid does, opens a room and ticks a few times. Then it switches a mod on and reverts it six times,
timing the parts warm, median of five:

- **Read:** `ModPatch.Parse`.
- **Checks:** `ModValidator.Validate`, against the rules in force.
- **Compose:** `RoomRules.WithMods` with the mod.
- **The whole switch-on:** `World.ApplyRoom(RoomCommand.Apply)`.
- **Allocation:** what each part allocates on the thread.

| Map, mod | Checks | Compose | Rest of the switch-on | Switch-on | Allocated (checks / compose / all) | Revert |
|---|---|---|---|---|---|---|
| Valley, sim-only | 13 to 53 ms | 6 to 42 ms | 0 to 3 ms | 21 to 81 ms | 29.5 / 13.9 / 43.4 MB | 0.1 ms |
| Valley, Moonstone | 13 to 14 ms | 6.5 to 11 ms | 0 to 2 ms | 21 ms | 29.6 / 13.9 / 43.7 MB | 7.4 to 7.6 ms |
| Valley, Wildfire | 12 to 13 ms | 6 to 14 ms | 0 to 2 ms | 21 to 22 ms | 29.5 / 13.9 / 43.4 MB | 0.1 ms |
| The nine, sim-only | 16 to 19 ms | 6 to 9 ms | 2 to 13 ms | 27 to 38 ms | 29.5 / 13.9 / 43.4 MB | 0.1 ms |
| The nine, Moonstone | 12 to 25 ms | 6 to 8.5 ms | 0 to 3 ms | 21 to 32 ms | 29.6 / 13.9 / 43.7 MB | 53 to 56 ms |

(Ranges are two or three runs; the first case of a run is slower, warming.)

**What it says:**
- **The switch-on is the checks (about two thirds) and the compose (one third).** Everything else, the policy, the
  site and live checks, putting the rules in force, the fit, is 2 to 3 ms.
- **It doesn't follow the map:** the valley and the nine allocate the same 43 MB and take the same 20 to 40 ms. Under
  Unity's Mono the same work was 85 to 91 ms (#210), and 43 MB of garbage in one frame brings the collector in. That's
  the rest of #155's 115 to 182 ms.
- **The rules are composed twice in the tick.** `ModValidator.Validate` composes the rules in force with the mod (its
  reference check: `baseRules.WithMods(new[] { mod }).CopyDoc()`), then `SwitchOn` composes the room's base with
  every mod on (`RoomRules.WithMods`). Each is a deep copy of the rules and a read of every table.
- **A revert is free unless the mod brought something cells hold.** Moonstone's revert drops a terrain, so the active
  chunks are made to fit (`World.Fit.cs`): 7 ms on the valley, 53 to 56 ms on the nine. That one does follow the active
  chunks and isn't #155. It's noted here for #190's follow-up.

## What can move off the tick

The checks and the compose are pure functions of their inputs: the rules in force (the `RuleSet`: base and mods, in
order), the budgets, the temporary materials' owners, and the mod's id and JSON. Nothing reads the cells, the tick or
the RNG. So both can run on a worker before the tick the switch lands at, and the tick does only:

1. Check that the inputs are still the ones it was prepared with (the `RuleSet`'s fingerprint, the temporary materials;
   a cheap compare).
2. The verdict's other checks (policy, already on, site and live problems), as now.
3. Put the prepared `RuleSet` in force (`PutInForce`), as now.

That's the "rest" column: 2 to 3 ms. If the inputs changed between preparing and the tick (another mod switched first,
a lapse), the tick does it all as now: slow, but the same result on every peer.

**The prototype** (`ModSwitchBench.AModComposedOnAWorkerIsTheSameRules`): the valley's room, Moonstone composed on a
worker (`Task.Run`) while the main thread ticks ten times, against the same compose on the main thread. The rules are
the same to the byte (`Effective.ToJson()` and `Fingerprint`). The compose shares nothing writable, so it's
thread-safe as it stands.

## What it needs from the protocol (D4)

A peer can only prepare a switch it knows about before the tick it lands at. Today a room command is stamped for the
next tick (`RequestRoomRpc`, `RoomIntent.From(command, 0)`). Clients learn of it in that tick's batch and compose in
the tick, as the host does.

- **Stamp a switch-on a few ticks ahead,** as casts already are (`hostTick + 1 + delay`). At 6 ticks a second, 3 ticks
  is 500 ms: enough for 20 to 90 ms of work on a worker, with room for the batch to arrive. The tick it lands at is
  in the command (the batch's tick), so every peer switches at the same tick, as now.
- **Every peer prepares as soon as it hears the command,** on a worker, keyed by the command (mod id, JSON hash, the
  tick, and the inputs' fingerprint).
- **A peer that's late** (its worker isn't done when the tick comes, or it never started, like a joiner replaying the
  history) does the work in the tick, as now. It may wait for the worker or start afresh. Either way the result is the
  same rules: only that peer's frame is slow, and lockstep holds.
- **The verdict stays in the tick.** Whether the switch is refused depends on the room at that tick (policy, already
  on), so the preparation computes the expensive parts and the tick decides. A refusal of a prepared switch just drops
  the prepared rules.
- **Determinism:** the prepared `RuleSet` is a pure function of the inputs, and the tick checks those inputs match
  before using it (above). Two peers can't put different rules in force. `RuleSet.Effective.Fingerprint` is already in
  the checksum (the rules part), so a difference would be caught at the next checksum tick.

## The smallest slice, golden-safe

**Core only, no protocol or `Scripts/` change:**
1. **`RoomSwitchPrep`** (core): `Prepare(RuleSet inForce, ModBudgets, temporary owners, mod id, json)` returns the
   checks' problems and the composed `RuleSet`, with the inputs' fingerprint. It's pure and safe on any thread.
2. **`World.ApplyRoom(command, prep)`:** when `prep` matches the room's inputs now, it uses `prep`'s problems and
   rules; otherwise, or with none, it does what it does today. `ApplyRoom(command)` is unchanged.
3. **Tests (golden-safe):**
   - Two worlds through the same history: one switches with a prep made on a worker, one without. They have the same
     `CellHash`, `RoomHash`, `CastHash` and save text at every tick, through a switch on, a refused switch, a revert
     and a switch on again.
   - A stale prep (another mod switched on in between) is ignored and gives the same result as none.
   - The bench reports the in-tick time with a prep: the 2 to 3 ms "rest".

   No golden moves, because the rules put in force are the same objects' content.
4. **Then, separately (`Scripts/`, Needs: unity):** stamp room switch-ons 3 ticks ahead, and prepare on receipt in
   `NetChem.Room`. That's the protocol step. It's safe to land after the core slice because a peer without a prep
   behaves as today.

**Cheaper and independent of the protocol:** the checks needn't compose the rules a second time.
- When the room has no mods yet, the checks' composed rules (rules in force + the mod) and the switch's (the room's
  base + the mod) are the same input, so the switch could reuse the checks' compose.
- More generally, `ModValidator` could take the composed `RuleSet` instead of making its own.

That saves about a third of the work and 13.9 MB, on every peer, with no protocol change. It's worth doing first if the
protocol step waits.

## Open questions

1. **How many ticks ahead?** 3 (500 ms) by these numbers. A player sees a switch they asked for land half a second
   later, which seems fine for a mod. Andrew may prefer it immediate for the asker; the asker's own peer could show
   "switching..." meanwhile.
2. **Reverts:** cheap except where cells must be made to fit (Moonstone's revert on the nine: 53 to 56 ms, the active
   chunks' `Sanitize`). Prepare them the same way, or leave them in the tick?
