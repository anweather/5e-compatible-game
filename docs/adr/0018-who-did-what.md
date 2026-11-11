# D18. Who did what: the core records an effect's cause (2026-11-11)

**Status:** Proposed

- **Context:**
  - #131 (backlog 26): a kill, a burnt house or a score should be able to name a player. Today the core can't. A cast
    knows its caster only while it goes off (`CastCommand.By`, kept in `World.castBy` for golf's turns); the cells it
    leaves don't.
  - The view keeps its own note of who cast what on each cell (`ChemGrid.LookFire.cs`, `LitNote.Caster`) for looks
    "by" a player. It's view-only and "near enough" on each machine, so nothing shared may read it.
  - D9 asks for integer ids in hot state and commands as the only way in. D4 keeps shared outcomes in the core.
  - There is a precedent for per-cell state kept outside `Cell`: `ChunkBlock.FireGeneration`, an int per cell,
    copied from the burning neighbour as fire spreads (`World.Sim.cs`), saved with the chunk (`fireGen`), and not in
    `CellHash`.
  - Peers already compare more than cells: `NetChem.Checksum` is `CellHash` plus the rules, things, sky, defense,
    cast, dragon and land hashes.
- **Decision (proposed):**
  - **A cause is a small integer, interned per world.**
    - `Causes` is a table of rows `{kind, who, what, tick}`:
      - kind is `player`, `act`, `trigger` or `world`;
      - who is the player's id as the core sees it (a string, as `CastCommand.By`), or null for the world;
      - what is the spell, act or trigger id.
    - Row 0 is "no one" (the world: weather, meteors, raiders, the dragon, a map's own fires).
    - A row is added the first time a command names a cause, in the order the tick applies commands. Every peer
      gets the same numbers.
    - Rows are never reused within a world; a world reset (the dragon) clears the table.
  - **A cell's live effect carries its cause:** one `int` per cell in the chunk's block, beside `FireGeneration`,
    not in `Cell`.
    - A cast sets it on the cells it touches.
    - Spread copies it from the cell spread from: fire, gas, charge, water a spell made, and reaction products
      (steam from fire and water takes the cause of the side that reacted, the "direct" one in `World.React`).
    - It goes back to 0 when the cell has nothing live left.
  - **Credit follows the chain, the last link wins.** A Fireball lights grass, the grass fire spreads, and a bridge
    plank burns out: the plank's cause is the Fireball's row, so the player is credited with the bridge.
    - Where two causes meet (a fire caught from two neighbours), the neighbour the spread pass reaches first wins.
      That order is already fixed, so it's deterministic.
    - An "assist" list (every cause that touched it) is not in this decision (open question 2).
  - **Outcomes read the cell's cause when they happen:** a raider downed in fire, a building cell burnt, a ball
    moved, a booth's zone hit. They write it into their own state: the defense's kills, golf's `LastBy`, the course
    run. Scores are made from those, not from the cells.
  - **Hashed apart:** a `CauseHash` (the table, and each chunk's non-zero causes) joins `NetChem.Checksum`'s others.
    `CellHash` is left as it is, so no existing golden moves. A cause that drifts between peers is still caught, by
    its own hash.
  - **Saved with the world:**
    - The world save writes the table, and each changed cell's cause where it isn't 0.
    - A packed chunk (D17) keeps its causes with its changes.
    - A cell whose only difference from the source is a cause is a change: `SameCell` misses it today, so the saves
      guard (`SaveTests.EveryCellMemberComesThroughASave`) gains the cause as a member it checks.
    - The player save (docs/design/player-saves.md) holds nothing of this: credit is the world's.
  - **Not decided here:** what a score is, or who sees blame. Those are game modes and minigames that read causes.
- **Why:**
  - **It's the smallest shape that names a player.**
    - An int per cell, copied where `FireGeneration` already copies, with strings only in the table (D9 seam 2).
    - The tick cost is one more store in branches that already write the cell.
    - Memory is 16 KB per unpacked chunk: about 1.3 MB on the valley (81 chunks) with every chunk unpacked, and about
      0.4 MB for 25 active chunks on the nine. Packed chunks hold causes only on changed cells, so cost follows the
      active region (J-086).
    - These are estimates by analogy with `FireGeneration`, not measured. The first slice measures them.
  - **Keeping it out of `CellHash`** keeps every chemistry golden and the save format pin (`c1737215`) where they are.
    Determinism is still checked, by `CauseHash` in the peers' checksum and by goldens of its own.
  - **"Last link wins" needs no new state** and reads as players expect: the Fireball's caster burnt the bridge. An
    assist list costs a set per cell.
  - **It unlocks:**
    - spell golf credit beyond the last cast (a ball knocked in by someone's fire);
    - the Gauntlet's "who broke the course";
    - Hold scoring by kills;
    - blame in the log ("Ann's fire took the bridge");
    - Claude's chat knowing who did what;
    - later, game modes that keep score (#126).
- **Smallest first slice:** fire only, player casts only, core only.
  1. `Causes` table, the per-cell int in `ChunkBlock`, set by `Cast(CastCommand)` on cells the cast lights, copied
     where `FireGeneration` is copied, cleared where it's cleared.
  2. `CauseHash`, and the table and causes in the world save, with the saves guard covering it.
  3. One reader: the log names the player whose fire burnt a building's cell.
  4. Goldens: a two-player fire on the Meadow whose credits and `CauseHash` are pinned. `CellHash` goldens
     unchanged, a save of the run loads to the same `CauseHash`, and the valley and the nine timed with
     `SparseWorldBench` and the valley tick probe, before and after.
- **Reopen if:**
  - the measured cost is over 2% of a tick on the nine, or memory grows with the map rather than the active region;
  - "last link wins" credits wrongly in play often enough to matter;
  - causes need to reach the view every frame (they'd then belong in the view's look notes too, D9 seam 6);
  - the struct-of-arrays move (D9 seam 3) happens first: then the cause is one more array, which is easier.
- **Open questions for Andrew:**
  1. Does Claude's act count as the asking player's (an act's cause is the player who asked) or as Claude's own?
     Likewise a trigger in a player's mod.
  2. Last link wins, or keep assists (every player whose effect touched an outcome)? Assists cost a set per cell.
  3. Should causes reach the view, so a player can see whose fire it is? The view's note does that today, near enough.
  4. What lasts: does a cell keep its cause after the fire is out (a burnt patch still "Ann's"), or only while live?
  5. Does anything need a cause from the world itself (lightning, a meteor), or is 0 enough?
- **Sources:** #131; `docs/backlog-archive.md` priority table item 26; `World.Sim.cs` (fire spread and
  `FireGeneration`); `World.Spells.cs` (`Cast(CastCommand)`, `castBy`); `World.Golf.cs` (`LastBy`);
  `ChemGrid.LookFire.cs` (the view's `LitNote`); `NetChem.Checksum`; D4, D9, D17; docs/design/player-saves.md.
