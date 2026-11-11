# Player saves: what's built, what a save holds, and the first slice (#119)

Status: design note, nothing built here. It brings `docs/PLAYER_SAVE.md` (the 2026-10 note, J-038) up to date with
what has landed since, and answers the lead's brief for #119. Read with `docs/adr/0017-the-sparse-world.md` (D17),
`docs/design/sparse-world.md` and `World.Save.cs`.

## Two saves, not one

#119's brief lists "the world's cells by chunk, the tick, the seeded RNG state, the character, mods/rules, the map's
hash" as one save. J-038 ("if you join you should bring your progress") splits that in two, and the code already
follows the split:

- The **world save** is the world's state: its cells, tick, RNG, weather, raiders, room mods and acts. The world
  owns it and the host keeps it. It's `World.Save` / `World.Load`, built and tested, but nothing in the game calls
  it yet.
- The **player save** is one player's progress: who they are, what they've earned and unlocked, and later what they
  carry. It travels with the player into any host's world and past a reset. The Hold's half of it is built
  (`ProgressFile`, `progress.json`).

So "a save" in the menu means writing both: the world save for the world you're in, and your own player save. A
peer that joins brings only its player save. The rest of this note keeps the two apart.

## What exists now (origin/dev 1aaa799a)

| Piece | Where | State |
|---|---|---|
| World save, format 1 | `Chemistry/World.Save.cs` (`Save`, `Load`, `SaveFormat = 1`) | Built and tested; no caller in `Scripts/` |
| Saved state | Header: `source` (the map's `Version`: `baked:<rows hash>` or `generated:<input hash>`), `rules` (`Rules.Fingerprint`), `w`, `h`, `seed`, `tick`, `rng`, clock, weather, wind, meteors, raiders, the Hold, `progress`, `hands`, `casts` (with each player's grimoire, `Known`), machines, golf, dragon, course, `room` (mode, host, base fingerprint, mods), placed things, acts, land stamps, laid rows, triggers, fits | One `JObject`, fixed order, integers and ids |
| Cells | `chunks`: per chunk, only the cells that differ from the source (`SameCell`), plus its streaming state; an untouched chunk isn't saved | A packed chunk (D17) saves its changes as they lie (`SavePacked`), without unpacking |
| Round-trip goldens | `Chemistry/Tests/SaveTests.cs` | `SaveLoadAndStepIsTheSameAsSteppingStraightThrough` (save, load, 120 ticks, same state); `ASaveOfALoadIsTheSaveAgain` (the format's FNV-1a golden, `c1737215`); `OnlyChangedChunksAreSaved`; `ASaveThatDoesntFitIsRefused` |
| Player save, the Hold's points | `Chemistry/HoldProgress.cs` (`ProgressFile`, `.bak` fallback), `Chemistry/World.Progress.cs`, `Scripts/ProgressKeeper*.cs` | Built: points are read from the player's folder, brought to every peer by a command, and written back as they're earned (the progress-restore probe, 9c28329b) |
| The grimoire (`PlayerCast.Known`) | In the world save's `casts` | Still the world's; `PLAYER_SAVE.md` moves it to the player save |
| Late join | `Scripts/Net/NetChem.cs` | Replays every tick batch from tick 0 (or from the last reset) |
| `keep_land` | maps-as-data phase 9 | A tool for making maps, not a player save: it folds stamps into `land.json` and so changes the map's hash |

## What the world save holds, and must not hold

**Holds:** what's above, which is everything the tick reads. The test for "everything" is that a world loaded from
a save and then stepped stays equal to one never saved (`SaveLoadAndStep…`, run on a Meadow source).

**Must not hold:**

- **Anything only a view has.** That means rigid-body positions, the camera, drawn tiers, Unity objects and
  `PlayerPrefs` settings. Physics never decides shared state (D4), so a crate's tumble isn't in the save; the
  core's placed thing is.
- **Caches the tick rebuilds.** That covers the still-water snapshots (`Snap*`), `Marks`, unpacked pool blocks, the
  `RollingHash` and any per-chunk dirty hash (#168). A cache that ends up in a save stops being a cache.
- **The source itself.** Only its `Version` is saved, and a save is refused over another source, so a changed map is
  a new world, not a corrupted one.
- **Peer and session things.** That covers `p{clientId}` names, wall-clock times, file paths, chat logs and keys.
  Players in a world save are named by the stable id the player save brings, not by the session's client id (below).
- **Another player's progress as theirs to keep.** The world save remembers a visiting player's points and
  grimoire for when they come back, but their own player save stays the record (the union wins, so nothing is
  lost).

## What the player save holds, and must not hold

**Holds:** a header (`format`, a stable random `id`, `name`), Hold `points` (built), and then, as
`PLAYER_SAVE.md` plans, the grimoire (`known`, sorted), kept items and unlocks, the wizard's look, and the last
world (id and source) plus spot as a respawn hint. It also notes the rules `Fingerprint` it was saved under. That's
a note, not a lock: a combo id the rules don't know is kept and hidden, never dropped.

**Must not hold:** any world state (cells, tick, RNG) or a cast in progress. It also mustn't hold anything another
peer would need to trust beyond what the host can check: points and known combo ids are checked against the rules
when the command is stamped, and nothing else is believed.

## How a load reaches the same CellHash (D4)

1. **The world is made from its source,** then the save's changed cells and state are laid over it. Every peer
   loads the same text over the same source `Version` and the same rules `Fingerprint`, or the load is refused.
2. **The tick and RNG state are integers in the save,** so the next tick draws the same numbers. Fixed key order
   and ordered chunks (`SortedChunks`) mean one world always saves to the same text. A peer can hash the save text
   itself to check it got the host's.
3. **Room mods are recomposed from the save's mod list,** over the base rules, in their saved order, so the rules in
   force match too.
4. **Packing doesn't show** (D17): a packed chunk saves its changes as they lie and loads either way. The cell hash
   reads packed chunks without unpacking, so active or packed, the hash agrees.
5. **The player save enters as a command,** never as a peer writing its own state. A peer's progress reaches the
   world as a command (`ComboCommand.Bring`, built for points) at a tick every peer runs. A loaded world stays equal on every
   peer whatever players bring.

The check: `CellHash(Load(Save(w)))` equals `CellHash(w)` at the save's tick, and equals it again after N ticks of
the same inputs. `SaveTests` covers this for a Meadow; the first slice below adds it for a real map's source.

## Sparse chunks and late join

- **Sparse (D17):** nothing to add. The save was the model for packing: a packed chunk *is* its save record.
- **Late join:** today a joiner replays from tick 0, which gets slower the longer a session runs. A world save makes
  the snapshot: the host saves at tick T, sends the text (packed chunks are small), the joiner loads it and replays
  only the batches after T. Because load-then-step equals straight stepping, the joiner lands on the host's hash.
  This is WORLD_STREAMING's "Joining an island" with the save as its record. It belongs in a later step, not the
  first slice: it needs the host to pick T between ticks and the replay buffer to start at T.

## Does `Cell` need to change first?

No. `Cell` is a public mutable class with public fields, and the tick writes through references it holds (#168).
That blocks a per-chunk dirty hash, which must see every write. A save doesn't need to see writes: it's taken
between ticks (`FinishTick` first) and reads every cell of every chunk, comparing it to the source. Whatever wrote
the cell, the save sees the result.

The real risk is a **new field**: a field added to `Cell` that `SameCell` / `SaveCell` / the loader don't know is
silently dropped from saves. `SameCell` lists 20 fields by hand today. Step 1 below adds a guard against that.

An autosave that writes only the chunks changed since the last save would need dirty tracking, and so #168's
change. A full save of a packed world is cheap enough to do without it: the changes are already held per chunk.

## The land (found building step 1, fixed in `save-land`)

A baked map's land (`land.json`, the hills under the cells that only the water reads) wasn't in the world save, and
`World.Load` didn't take it, so a loaded valley had flat land and its water ran differently from the first tick (the
Gauntlet's pits). Setting the land again after a load didn't fix it: `SetLand` marks a cell NoLand by the water
standing in it *when it's called*, and a load has the water of play, not of the build. A generated map's land comes
from its source (`LandFromSource`) and was never affected.

**Done (B, the lead's call, 2026-11-11):** `SetLand` keeps the cells it marked NoLand for their water and an FNV-1a
hash of the rows it was given. The save writes them as `landSet` (`rows`: the hash; `marks`: runs of
`[first Idx, count]`), only for a world given land, so saves without land, the Meadow's goldened format among them,
are as they were. `World.Load(..., land: rows)` lays the save's state and cells, then makes the land from the rows
with the saved marks plus what the stamps moved. That's the same land whatever water stands where now. A save given
land is refused without the rows or with other rows; one whose land was flat (a stamp's) needs none. The valley test
loads with the map's land, with and without casts, and matches the world never saved 60 ticks on, its land too.

**The build order (answered):** they differ, and it matters.
- **The game, baked map:** `ChemGrid.MapWorld` makes the cells, lays the puzzles, stamps, rows, golf and Hold, and
  `StartWorld` keeps the start. `ValleyChemistry` sets the land later, as the ground binds, so the water the lays put
  down is NoLand.
- **`SparseWorldBench.Build`:** it sets the land right after making the cells, before the lays, so that water gets
  land. That's also how a **generated map's** land is made (`LandFromSource` marks NoLand by the water the source
  makes), and `GeneratedSourceTests.ItsWorldIsTheBakesWorld` relies on it, so `Build` is left as it is.
- **For the hash:** on the valley, the two orders differ in 38 cells of land (the Gauntlet's pits), and the
  `CellHash` splits on the first tick. The valley save test sets the land again after `Build`, which gives the
  game's land, and loads that.
- **New question for the lead:** the same map played baked (land set after the lays) and played generated (land as
  the source makes it) disagree on the land under laid water. So the generate switch isn't the same world as the bake
  in water that a lay put down. Should laid water be NoLand (the baked game's way) or keep its land (the generated way
  and the bench's)?
- A game load will have to give `Load` the land rows itself (step 5). The marks make it not care when.

## The smallest first slice

**A world save of a real map round-trips to the same hash: core only, with a golden.**

- A test loads the valley's `BakedSource` (and a generated map's source) and plays a fixed scripted run: casts,
  an act, a land stamp and a placed thing. It saves at tick T, loads, and asserts the same `CellHash`, the same save
  text, and the same hash after 200 more ticks of the same commands.
- No hex pinned for the valley: any edit to its map data would move it. The run checks equality (the load against
  the world never saved), and the Meadow's `c1737215` still pins the format.
- Plus the field guard: a reflection test that every public field of `Cell` and `Cell.Rare` changes the outcome of
  `SameCell` and survives a save and load. It fails when someone adds a field and forgets the save.

That proves the world save on the maps players play, before any file, menu or command exists, and every later step
stands on it.

**Built (`saves-guard`):** the guard, as `SaveTests.CellMembersAreAllInTheGuard` and
`EveryCellMemberComesThroughASave` (20 members, the Rare ones included). Also the valley's round trip, with casts,
30 ticks before the save and 60 after, on the map's land (see "The land"). Not yet covered: a generated map, an act, a
land stamp and a placed thing.

## Build steps

1. **(S)** The field guard and the real-map round trip with its golden (the slice above). Core.
2. **(S)** The glossary words **world save** and **player save** (added with this note) and a stable player id: a
   random id made once and kept in the player save, used in place of `p{clientId}` where the core names a player.
   Core sees only a string, as now.
3. **(M)** The player save file in the core (`PlayerSave.cs`): it takes over `ProgressFile`'s points and adds
   `known`. It writes atomically (`.tmp`, rename, `.bak`, as `ProgressFile` does). Tests: a save of a load is the
   save again, an unknown format is refused, a broken file falls back to the `.bak`.
   **Built (`player-save-file`):**
   - **The file:** `PlayerSave` is the Hold's `progress.json` grown into the player save. It keeps the name and
     `ProgressFile`'s care with bad files (`.broken`, `.bak`, points never lower), so `ProgressFile` is now a thin
     wrapper and `ProgressKeeper` is unchanged.
   - **The format:** version 2 is `version`, `id` (when given), `points` and `known` (sorted), indented, in that
     order, so the same progress is the same text. Version 1 (points alone) still loads.
   - **The write:** whole to `.new`, then put in place by `File.Replace`, keeping the last good file as `.bak`. A
     crash mid-write leaves the last save.
   - **The id:** an opaque string the caller gives, or none; step 2 decides what it is.
   - **`known`:** kept and round-tripped, but not brought to a world (step 4).
   - **Downgrade:** an older build reads a version-2 file as bad. It keeps it as `.broken` and falls back to the
     `.bak`, which is version 1 only if the first version-2 write replaced it.
4. **(M)** The grimoire joins `ComboCommand.Bring`: brought known combos are unioned in at a tick every peer runs,
   with a golden. The world save keeps writing `Known` for a while so older builds still load.
5. **(M)** Solo save and load in the game (`Scripts/`): a save slot under `persistentDataPath/worlds/`, written from
   the pause menu and on quit, with the player save beside it in `players/`. On start, load if the slot's source and
   rules still match, else start fresh and say why.
6. **(L)** Late join from a snapshot: the host's world save at tick T, then replay from T, checked by hash on join.
   A two-peer probe.
   **Built, core half (`late-join-snapshot`):**
   - **`JoinSnapshot`** (`Chemistry/JoinSnapshot.cs`) holds:
     - the host's world save at tick T, taken between ticks;
     - the mods in force outside a room (a room's mods are in its save);
     - the host's checksum at T, part by part, in `NetChem.Checksum`'s order.

     Its text is one line of JSON, the same on every machine.
   - **`Join(baseRules, source, land)`:**
     - loads the save over the joiner's own map, rules and land, and rebuilds the `RuleSet` to run batches with;
     - checks the loaded world against the host's checksum before any tick, so a part the save misses shows at once,
       named, not ticks later as a desync;
     - is refused, saying why, for another map ("made by ..."), other rules ("saved under other rules"), land missing
       or different ("the map's land", "other land"), or a world that doesn't come back to the checksum.
   - **The stands:** the world save now keeps them (`stands`, written only when there are some, so other saves read
     as before). They're in the checksum, and a snapshot joiner doesn't have the strike history to rebuild them.
   - **Tests** (`LateJoinTests`):
     - `AJoinerFromASnapshotRunsAsAPeerThereFromTheStart`: a wrapping world, two players walking over the seam with
       step triggers, Fireballs, water, the planner's chunk switches, a rule switch and back, and the dragon.
       Snapshots taken at 12 ticks before, during and after each join and replay to tick 420. Every checksum part
       equals the host's at every tick.
     - `ASnapshotThatDoesntFitIsRefusedSayingWhy`, `ASnapshotOverOtherLandIsRefused`.
   - **Cost** (Release, `SparseWorldBench.ASnapshotsBytesAndTime`, after 60 ticks of play):

     | Map | Size | Gzipped | Taken in | Joined in |
     |---|---|---|---|---|
     | The valley (81 chunks) | 258 KB | 40 KB | 77 ms | 148 ms |
     | The nine (729 chunks) | 354 KB | 60 KB | 310 ms | 229 ms |

     Taking it is a hitch on the host's frame (the save's diff against the source), so it wants a frame of its own or
     a worker. Join's time is the joiner's.
   - **Found, then fixed (`snapshot-start`, question 8 (a)): a world save didn't keep the dragon's start.**
     - **The bug:** a load took the start to be the source as made, with mods' buildings and golf put back. The game
       keeps its start *after* laying the map's puzzles, map stamps and rows (`ChemGrid.MapWorld`, then
       `StartWorld`'s `KeepStart`). So a loaded world's start lacked them, its audit counted other changes, and the
       dragon's checksum part moved the next tick. A dragon's reset after a solo load would have put back the source,
       not the map.
     - **The fix:** the save keeps each chunk's start where it isn't what its source makes (`start.chunks`: cells,
       and fire generations, in `SavePacked`'s form). A load makes each start from that, in one pass a chunk, once the
       chunks are laid and fit. It's written only when some chunk's start differs, so other saves, and every golden,
       are as they were.
     - **Tests:** `AStartLaidBeforeKeepStartComesBackFromASnapshot` (now a normal test). The main snapshot test, with
       its trees and water-filled pits laid before `KeepStart` as the game lays a map.
       `SaveTests.ADragonAfterALoadPutsBackWhatTheMapLaid`: the valley saved after play, loaded with its land, the
       dragon summoned on both, and the same world after the reset, the Gauntlet's pits included. All three fail
       without the fix.
     - **Cost** (Release, `SparseWorldBench.ASnapshotsBytesAndTime`, two runs; the start is the map's laid rows,
       puzzles and stamps):

       | Map | Size | Gzipped | Taken in | Joined in |
       |---|---|---|---|---|
       | The valley | 400 KB | 53 KB | 86 to 88 ms | 141 to 196 ms |
       | The nine | 533 KB | 75 KB | 115 to 221 ms | 214 to 237 ms |

   - **What the net layer (`Scripts/Net/NetChem.cs`) would need** (not built):
     1. **The host:** when a peer joins, take a `JoinSnapshot` between ticks (`World`, `world.Source`, the `RuleSet`
        it runs) and send its text gzipped. It's 40 to 60 KB, past one NGO message, so it goes as a fragmented
        reliable message or a named message stream. The batches from T + 1 follow, not the timeline from tick 0.
     2. **The joiner:** `JoinSnapshot.Parse(...).Join(baseRules, source, ValleyChemistry.LandOf(world), budgets)`, then
        bind the view to the loaded World as a reset does (cells, things, stands, the drawn tier), then run batches
        from T + 1 with the usual checksums. On a `SaveException`, show why and fall back to today's full replay.
     3. **The timeline:** the host may then keep batches only since its last snapshot, and `StandKeeper` the strike
        history only since then.
     4. **Taking it off the frame:** at worst ~300 ms on the nine, so a frame of its own, or done on a worker from a
        copy taken between ticks.
7. **(M)** Autosave, every few minutes and on a discovery. Writes are off the main thread for the file, but the save
   text is made between ticks.
8. **(S, later)** Kept items and the wizard's look in the player save, once inventory (BACKLOG 5) lands.

## Status (2026-11-11)

| Step | State | Waits on |
|---|---|---|
| 1. Field guard, real-map round trip | **Built** (`saves-guard`, `save-land`; on dev) | |
| 2. Glossary words, stable player id | **Words in** (**player save**, **world save**, **player id**). **`PlayerId` built as a type only** (`saves-glossary-id`), used nowhere yet | Question 4 (profiles), then `Scripts/` |
| 3. The player save file | **Built** (`player-save-file`, with `"\n"` on every machine; on dev) | |
| 4. The grimoire brought by `ComboCommand.Bring` | Not started: it *is* the grimoire moving to the player save | Question 6 (and question 5: whether a host may ignore what's brought) |
| 5. Solo save and load in the game | Not started (`Scripts/`) | Question 1 (slots), question 2 (rules drift); question 4 for whose player save |
| 6. Late join from a snapshot | **Core half built** (`late-join-snapshot`, `snapshot-start`; question 8 resolved (a)); the net layer not (`docs/handoffs/briefs/snapshot-join-net.md`) | Question 3 (whose world) only for what a guest keeps |
| 7. Autosave | Not started; after step 5 | Question 1 |
| 8. Kept items, the wizard's look | Not started | Inventory (BACKLOG 5) |

**`PlayerId`** (`Chemistry/PlayerId.cs`, `PlayerIdTests`):
- **The value:** an opaque value type, 1 to 64 of a-z, 0-9, `-` and `_`, compared as written.
- **Made once:** `PlayerId.New` makes one from 16 random bytes the caller brings, so the core draws no randomness.
  It's never made from a client number or any other per-session value.
- **Reading:** `TryParse` / `Parse` / `Problem` read one back and say what's wrong. A session's name for a player
  (`"p"` and digits, `"solo"`) is refused, so the two can't be taken for each other.
- **Where `p{clientId}` is made and read today:**
  - It's made only in the view: `NetChem.PlayerName` (`Scripts/Net/NetChem.cs:314`). It's used there for the local
    player (311), a player leaving (254), the strike and combo intents' senders (487, 495) and a cast's caster
    (`CastIntent.ToCommand`, 785).
  - The core carries it as an opaque string (`CastCommand.By`, `ComboCommand.Player`, `StrikeCommand`, the hands,
    the Hold's points, course runs). It reads the form in one place: `TriggerDef.PeerOf` (`Rules.Triggers.cs:94`)
    turns `"p<n>"` into "Player n+1", called from `World.Golf.cs:164` and `World.Triggers.cs:88, 102, 111, 145`.
  - Swapping in a `PlayerId` touches `NetChem` (the view) and those `PeerOf` sites (a peer's name would come from
    the id, not parsed from it). That waits on question 4.

## Open questions for Andrew

1. **Slots:** one world save per map (it just continues), or named slots a player picks from?
2. **Rules drift:** a save made under rules that have since changed is refused today (`rules` fingerprint). Is that
   right for players? The other choice is to load it with today's rules and accept that the world may differ.
3. **Whose world, in multiplayer:** only the host saves the world, and a guest keeps just their player save. Is that
   right, or should guests be able to take a copy of a friend's world home?
4. **Profiles:** one player per machine, or several characters (`PLAYER_SAVE.md` question 1, still open)?
5. **Fresh-start worlds:** may a host ignore brought progress (`PLAYER_SAVE.md` question 3)? The Hold already
   starts dev builds with everything, so the switch exists in part.
6. **Grimoire, world or player:** may it move to the player save (step 4)? Then a discovery made in a friend's
   world goes home with you.
7. **The land in a save:** resolved, B (the lead, 2026-11-11). The save keeps the NoLand marks and the rows' hash, and
   `Load` takes the map's land rows. Built in `save-land`; see "The land".
8. **The dragon's start in a save:** resolved, (a) (the lead's call, 2026-11-11; open for Andrew to veto). The save
   keeps each chunk's start where it isn't what the source makes (`start.chunks`, cells in `SavePacked`'s form), and a
   load makes each chunk's start from it. Built in `snapshot-start`; see step 6.
