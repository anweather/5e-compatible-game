# Player saves (design note)

Status: design only, nothing built. BACKLOG 21; Andrew's answer in
`JUDGMENT_CALLS.md` J-038: "If you join you should bring your progress."

A **player save** is the small file a player keeps on their own machine. It
carries their progress from world to world: into another host's world, and
past a world reset (J-014). The **world save** (`World.Save`,
`docs/WORLD_STREAMING.md` "Part 2, third slice") stays the world's: cells,
weather, raiders, what play changed.

## What goes where

| In the player save | Stays in the world save |
|---|---|
| The player's id and display name | The cells that differ from the chunk source |
| The grimoire: the combos they have cast (`PlayerCast.Known`) | The tick, RNG, weather, wind, meteors, the defense |
| Later: unlocks, and the items they keep (an inventory, BACKLOG 5) | A player's cast in progress (meter, loaded elements, d-pad slot) |
| Later: the wizard's look (`WizardOutfit`) | Things (props and items) lying in the world |
| The world they were last in, and where, as a hint for respawning | |

Settings (volume, controls seen) stay in `PlayerPrefs`, as `GameVolume` and
`ControlsMenu` keep them now. They are the machine's, not the character's.

Position is a hint, not progress. A player who joins a different world, or
a reset one, spawns at that world's spawn. Only a return to the same world
(same world id, same source) uses the saved spot.

## Identity

Today a player is `p{clientId}` (`NetChem.PlayerName`), which changes every
session. A player save needs a stable id: a random id made once on first
run and kept in the save (and so in the machine's profile folder). The core
only ever sees the id as a string, as now.

## Where it lives

One file per profile under Unity's `Application.persistentDataPath`, in a
`players/` folder: `players/<id>.json`.

- Windows: `%USERPROFILE%\AppData\LocalLow\<company>\<product>\players\`
- macOS: `~/Library/Application Support/<company>.<product>/players/`
- Linux: `~/.config/unity3d/<company>/<product>/players/`

The world save's slot (the view's next slice) sits beside it in `worlds/`.

## Format and versioning

The world save's patterns: JSON, integers and ids only, a fixed order, so
one player always saves to the same text.

- A header: `format` (a `PlayerSaveFormat`, refused if unknown, like
  `SaveFormat`), the player `id`, `name`.
- `known`: the grimoire, sorted (it's already a `SortedSet`).
- The rules' `Fingerprint` it was last saved under, as a note, not a lock:
  a combo id the current rules don't know is kept but not shown, so a mod
  or rules change never loses progress.
- New fields are added at the end with a default when missing (as
  `World.Save`'s cell arrays grew: `Later`). A breaking change bumps the
  format and gets a migration step (below).

The code is engine-free, in the core, beside `World.Save`: a new
`Chemistry/PlayerSave.cs` (read, write, merge), tested in `SaveTests` style
(a save of a load is the save again; an unknown format is refused). C#
only, like the world save; the sandbox has none.

## Single player and lockstep multiplayer

**Single player:** on start, the game loads the player save and stamps it
into the world (below); on save, it writes both the world save and the
player save.

**Lockstep (D4):** every peer runs the same world from the same inputs, so
a player's progress must enter as a **command**, never by a peer writing
its own state.

1. A peer joins and sends the host its player save (its id and grimoire).
2. The host checks it (known combo ids, a sane size) and stamps a
   `PlayerJoinCommand` into the next tick's batch, as `StampRules` does.
3. Every peer applies it at that tick: the player's `PlayerCast.Known` is
   set to the union of what the world had for that id and what was brought.
4. A late joiner gets the world from a snapshot (WORLD_STREAMING
   "Joining an island"), then the same command.

**Whose save:** each player owns theirs. The host keeps only the world
save. When a player discovers a combo in someone else's world, their peer
writes it to their own player save (from the `ComboFired` with
`Discovered`, which every peer sees). Nothing is sent back to the host.

**Trust:** the host can refuse what it doesn't know, but can't verify that
a grimoire was earned. That is fine for friends' worlds. If a world wants
it, a host setting could ignore brought progress and start everyone fresh.

The new command is core state, so it goes in both engines: the C# command
and `World` apply, and the sandbox's combos (`sandbox/src/combos.ts`), with
a shared golden.

## When it's written, and crash safety

- On a discovery (a new combo), at once: progress is small and rare.
- From the pause menu's save, and on quit (`PauseMenu`, `QuickExit`).
- With the world's autosave, when that exists.

Each write goes to `<id>.json.tmp`, is flushed, then replaces `<id>.json`
(a rename), keeping the last good file as `<id>.json.bak`. On load, a
broken file falls back to the `.bak`, then to a new player with a warning
in the log. A crash loses at most what came after the last write.

## Migration

The grimoire lives in the world save now (`casts`, combo-core d81c0ae). On
the first run with player saves:

- Single player: copy the local player's `Known` from the loaded world into
  a new player save.
- The world save keeps `casts` (a cast in progress), and keeps writing
  `Known` for a while so older builds still load it. The player save wins
  where they differ (the union, so nothing is lost).

## Files code would touch

- New: `unity/Assets/Prototype/Chemistry/PlayerSave.cs` and its tests.
- `Chemistry/Combos.cs` (`PlayerCast.Known`, the join command's apply),
  `Chemistry/World.Save.cs` (keeping `Known` for the transition).
- `Scripts/Net/NetChem.cs` (a stable player id in place of `p{clientId}`,
  the join hand-off, stamping the command), `Scripts/Net/NetWizard.cs`.
- `Scripts/PauseMenu.cs`, `Scripts/QuickExit.cs` (save on quit),
  `Scripts/ItemHolder.cs` and the inventory, once it exists.
- `sandbox/src/combos.ts` (the join command), with a shared golden.
- `docs/GLOSSARY.md`: add **player save** (and **world save**) before
  building.

## Open questions for Andrew

1. Several characters per machine (profiles), or one player per machine?
2. What else is progress: kept items and unlocks (likely), the wizard's
   look? Spells beyond combos?
3. May a host ignore brought progress (a "fresh start" world), or does
   progress always come along?
4. Does a player keep what they found in a world they only visited (yes by
   default), and does the host's world remember them when they leave?
5. Should the save follow the player between machines (a cloud copy, Steam
   Cloud later), or stay local for now?
