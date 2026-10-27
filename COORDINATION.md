# Coordination

Three Claude sessions work on this repo at once, on two machines. This file
says who owns what, so they don't collide. Andrew approved the lanes on
2026-10-27.

The coordinator is the Claude session **"Unity skill install"** (PC). Message
it with SendMessage. It doesn't write game code.

## Rules for every agent

1. Read this file before you start each new task (`git fetch` first, then
   read the copy on `origin/dev`).
2. Work only inside your lane. Before you touch anything outside it, ask
   the coordinator and wait for its OK.
3. Work on a short-lived branch off `dev`, never directly on `dev` or `main`.
   On the PC, each session keeps its own checkout or worktree.
4. Commit often, in small commits.
5. Merge to `dev` only when the coordinator says it's your turn in the merge
   order. Fetch and rebase on `origin/dev` first, and push straight after.
6. Tell the coordinator when a task is done or blocked.
7. Never discard, reset or overwrite work you didn't write. If you find
   uncommitted work in a file outside your lane, stop and report it.
8. On the PC, only one heavy Unity job at a time (an Editor build, a player
   build, or Multiplayer Play Mode). Memory runs out with two. Ask the
   coordinator before you open an Editor or start an MPPM player.

## Agents and lanes

| Agent (session name) | Machine and checkout | Lane |
|---|---|---|
| **Gameplay** (`gameplay-session-player-look`) | PC, main checkout `C:\Users\Andrew\Development\5e-compatible-game` | The player and the wizard: controls, looks, spells and casting, items, menus and HUD, sound, the scene and yard builders, and the yard scene |
| **World** (`world-look-and-streaming`) | PC, worktree `.claude/worktrees/village` | Simulation and streaming: the chemistry, chunks, streaming, weather and fire, the rules data, and the C# core and TS sandbox that mirror them |
| **Build** (`unity-game-macbook`) | Mac, `/Users/anweather/Documents/Development/5e-compatible-game` | Build size, performance settings, packages, versions and releases |

## File ownership

Each path has exactly one owner. Anything not listed here belongs to no lane:
ask the coordinator first.

| Owner | Paths |
|---|---|
| Gameplay | `unity/Assets/Prototype/Scripts/**`, except the World and Build paths below |
| Gameplay | `unity/Assets/Prototype/Prefabs/**` (including `NetWizard.prefab`) |
| Gameplay | `unity/Assets/Prototype/Scenes/**`, `unity/Assets/Prototype/Generated/**` |
| Gameplay | `unity/Assets/Prototype/{Audio,Materials,Shaders,Settings}/**`, `unity/Assets/InputSystem_Actions.inputactions` |
| Gameplay | `world/looks.json` |
| World | `unity/Assets/Prototype/Chemistry/**` (including `Chemistry.asmdef` and `Chemistry.Tests.asmdef`) |
| World | `unity/Assets/Prototype/Scripts/ChemGrid.cs`, `Scripts/Net/NetChem.cs`, `Scripts/ProbeExplore.cs` |
| World | `rules/**`, `core/**`, `sandbox/**` |
| World | `world/` except `looks.json` |
| World | `docs/WORLD_STREAMING.md`, `docs/FAST_LOOP.md` |
| Build | `unity/ProjectSettings/**` (including the version) |
| Build | `unity/Packages/manifest.json`, `unity/Packages/packages-lock.json` |
| Build | `unity/Assets/Settings/**` (the URP assets) |
| Build | `unity/Assets/Prototype/Scripts/Editor/AssetPacks/**`, `unity/AssetPacks.json` |
| Build | `tools/ship-update.sh`, `docs/RELEASING.md`, `unity/ReleaseReadme.txt` |

These shared files have **no owner**. Changes go through the coordinator's
queue, and only one agent edits them at a time: `CLAUDE.md` (every level),
`README.md`, `docs/GLOSSARY.md`, `docs/ARCHITECTURE.md`, `docs/BACKLOG.md`,
`.gitignore`, any other `.asmdef`, the Asset Store and third-party folders
under `unity/Assets/`, and `tools/probe.sh`.

Cross-lane needs that come up often:

- **A spell needs a change to `rules/spells.json`:** Gameplay asks the
  coordinator, and World makes the change (or the coordinator lends the file
  to Gameplay for one commit).
- **Something needs a ProjectSettings or package change:** ask the
  coordinator, and Build makes the change.
- **A yard rebuild rewrites `NetWizard.prefab` or the scene:** only Gameplay
  runs it.
- **`.meta` files:** never edit a GUID. Commit each new asset together with
  its `.meta`, in the owner's lane.

## Current tasks

| Agent | Task | Branch | State |
|---|---|---|---|
| Gameplay | Measure `perf/no-static-batching` on Windows (two player builds, `-fps-probe`) and send the numbers to the coordinator and Build. Don't commit or merge it | a throwaway local branch | In progress |
| World | Streaming part 2, slice 5 (props sleep with their chunk) | none yet | Waiting on Andrew |
| Build | Merge `perf/no-static-batching` into `dev` once the Windows numbers hold up | `perf/no-static-batching` | Waiting on Gameplay's numbers |

## Shared-file queue

The coordinator approves each change and schedules them one at a time.

| # | File | Agent | Why | State |
|---|---|---|---|---|
| (none yet) | | | | |

## Merge order

1. ~~`git-path` (Gameplay: builds find git off the PATH)~~. Merged as `0bf2208`.
2. `perf/no-static-batching` (Build), after the Windows check.
3. Streaming slice 5 (World), once Andrew approves it. Rebase on `dev` after step 2.

## Open questions

1. **Streaming slice 5:** Andrew to approve. Props are spawned by
   `ThingSpawner.cs` and `PhysicsProp.cs`, which are in the Gameplay lane.
   World's proposed split: World adds a chunk activate/deactivate event in
   `ChemGrid`/`NetChem`, and Gameplay adds a small subscriber in
   `ThingSpawner`/`PhysicsProp` that puts props to sleep (Rigidbody sleep or
   kinematic) in inactive chunks and wakes them on activation. World merges
   first, then Gameplay.
2. **`origin/feat/spell-radial`:** it's already in `dev` (rebased as `1300ff9`
   and `f8d1a9f`, then phase B in `e970913`), so there's nothing to merge. The
   remote branch could be deleted. Andrew to decide.
3. **Old local branches:** `playtest-fixes`, `streaming-live`,
   `streaming-linger`, `village` and `feat/chunked-valley` are all merged into
   `dev`. They could be deleted. Andrew to decide.

## Log

- 2026-10-27: lanes approved. Removed 11 merged agent worktrees under
  `.claude/worktrees/agent-*` and their branches. The spell radial was
  already on `dev`.
