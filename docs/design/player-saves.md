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

## The land (found building step 1)

A baked map's land (`land.json`, the hills under the cells that only the water reads) isn't in the world save, and
`World.Load` doesn't take it. A loaded valley has flat land, so its water runs differently: the Gauntlet's pits
slosh one way in the world never saved and settle in the loaded one, the first tick after the load. Setting the land
again after a load doesn't fix it. `SetLand` marks a cell NoLand by the water standing in it *when it's called*,
and the map's build lays the pits' water after the land, so a load (water already there) marks them differently.
A generated map's land comes from its source (`LandFromSource`) and isn't affected.

`SaveTests.TheValleyComesBackFromASaveTheSame` covers everything else on the valley (cells, puzzles, buildings,
golf, the Hold) with its land left flat until this is decided. Two ways to fix it:

- **A. The save keeps the land as it is:** each chunk's land, as `LandCodec` bytes, for the chunks whose land isn't
  flat. Saves are self-contained and right whatever order a map was built in, but bigger: up to the map's land again
  on a valley-sized map, though it compresses well.
- **B. The save keeps only where the water marked NoLand:** with a hash of the land rows. `World.Load` takes the
  map's land rows (as it takes the source) and rebuilds the same land, refusing a save made over other land. Saves
  stay small and the land stays map data, but the load needs `land.json` and a new `SetLand` path that takes its
  marks from the save rather than from the cells.

I'd pick B: it's how cells already work (map data plus changes). Either way, the bench's `Build` and the game
(`ChemGrid` lays the puzzles, `ValleyChemistry` sets the land) should be checked to build in the same order.

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
30 ticks before the save and 60 after, its land flat (see "The land"). Not yet covered: a generated map, an act, a
land stamp and a placed thing.

## Build steps

1. **(S)** The field guard and the real-map round trip with its golden (the slice above). Core.
2. **(S)** The glossary words **world save** and **player save** (added with this note) and a stable player id: a
   random id made once and kept in the player save, used in place of `p{clientId}` where the core names a player.
   Core sees only a string, as now.
3. **(M)** The player save file in the core (`PlayerSave.cs`): it takes over `ProgressFile`'s points and adds
   `known`. It writes atomically (`.tmp`, rename, `.bak`, as `ProgressFile` does). Tests: a save of a load is the
   save again, an unknown format is refused, a broken file falls back to the `.bak`.
4. **(M)** The grimoire joins `ComboCommand.Bring`: brought known combos are unioned in at a tick every peer runs,
   with a golden. The world save keeps writing `Known` for a while so older builds still load.
5. **(M)** Solo save and load in the game (`Scripts/`): a save slot under `persistentDataPath/worlds/`, written from
   the pause menu and on quit, with the player save beside it in `players/`. On start, load if the slot's source and
   rules still match, else start fresh and say why.
6. **(L)** Late join from a snapshot: the host's world save at tick T, then replay from T, checked by hash on join.
   A two-peer probe.
7. **(M)** Autosave, every few minutes and on a discovery. Writes are off the main thread for the file, but the save
   text is made between ticks.
8. **(S, later)** Kept items and the wizard's look in the player save, once inventory (BACKLOG 5) lands.

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
7. **The land in a save:** A (the save keeps the land) or B (the save keeps where water marked NoLand, and the load
   takes the map's land)? See "The land" above. Until it's decided, a baked map's save loses the land under its water.
