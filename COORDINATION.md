# Coordination

Three Claude sessions work on this repo at once, on two machines. This file
says who owns what, so they don't collide. Andrew approved the lanes on
2026-10-27.

The coordinator is the Claude session **"Project Manager"** (PC; it was called
"Unity skill install" before). Message
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
6a. Judgment calls for Andrew go through the coordinator. Check
   `DECISIONS.md` first: if a past decision covers it, follow that.
7. Never discard, reset or overwrite work you didn't write. If you find
   uncommitted work in a file outside your lane, stop and report it.
8. On the PC, only one heavy Unity job at a time (an Editor build, a player
   build, or Multiplayer Play Mode). Memory runs out with two. Ask the
   coordinator before you open an Editor or start an MPPM player.

## Branch cleanup (Andrew's rule, 2026-10-27)

A branch that is fully merged into `dev` can be deleted, both locally and on
`origin`, without asking Andrew. The coordinator does this. Agents delete only
their own branches, once their merge has been pushed. A branch counts as
merged when `git branch --merged origin/dev` lists it, or when its commits
reached `dev` by rebase or cherry-pick and `git cherry` / `git diff` confirm
nothing is left. Never delete `dev`, `main`, an unmerged branch, or a branch
a worktree has checked out.

## Demo reel (every feature ships with a clip)

The reel is the "Valley Demo Reel" artifact:
https://claude.ai/artifact/MeFmq29Uv2CE6ZggRKVMkd. The coordinator owns it.
Its source is `showcase/reel.html` (the page, which never changes) and
`showcase/reel.json` (the clip list, which only grows). See J-011.

When you merge a feature with something to see, also make:
- a clip: MP4 (H.264), 720p, 30 s at most, 5 MB at most. Film it with
  `DemoRecorder` while the feature's probe runs, or in the player;
- a poster JPEG for it;
- or just a screenshot, if a clip doesn't suit the feature.

Fixes get a before and an after: the playtest bot's `before.jpg` and
`after.jpg` are ideal. Save the files in `unity/Builds/demo-videos/`, which
git ignores, using short, unique, plain names (`props-sleep.mp4`).

Then send the coordinator:
- the file names;
- a title;
- the lane;
- an area (Spells, Moving and playing, The world, Menus, Fixes, ...);
- the length;
- the merge commit;
- one or two plain sentences on what the clip shows.

The coordinator appends the entry to `reel.json` and republishes. Filming
uses the Editor, so ask for the PC's Editor slot as usual.

Space: one artifact version holds at most 256 MB and 511 files, and the
reel uses about 50 MB now. Near 200 MB, the coordinator starts a
"Volume 2" artifact and links the two.

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
| World | The playtest bot (new): `unity/Assets/Prototype/Scripts/Playtest/**`, `tools/playtest/**`, `playtest/**` |
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
| Gameplay | Quit-crash test A (visibility) on an existing build; no downloads, no changes (J-010) | none | Started 2026-10-27 |
| World | The playtest bot (see `playtest/DESIGN.md` on the branch, and J-008 in `DECISIONS.md`). Phase 1: spikes (rendering minimised or off-screen, muted audio, incremental build time), then the bot. Stop before the scheduled tasks are set up | `playtest-bot` (off `dev`) | Phase 1 started 2026-10-27; has the PC Editor slot |
| Build | (free). The quit-crash research is done (see Open questions) | | Waiting for its next task |

## Shared-file queue

The coordinator approves each change and schedules them one at a time.

| # | File | Agent | Why | State |
|---|---|---|---|---|
| (none yet) | | | | |

## Merge order

1. ~~`git-path` (Gameplay: builds find git off the PATH)~~. Merged as `0bf2208`.
2. ~~`perf/no-static-batching` (Build)~~. Merged as `d4ae548` (473 → 267 MB on
   Windows, frame times within noise). Restart Unity after pulling; the first
   player build takes about 14 min to compile shader variants.
3. ~~Slice 5, part A (World: the chunk event)~~. Merged as `3c1d848`.
4. ~~Slice 5, part B (Gameplay: props sleep and wake)~~. Merged as `a473873`.

## Open questions

1. **Windows builds crash on quit.** It's an access violation in
   `D3D12Core.dll` (DirectX 12 Agility 1.618.1.0) during shutdown, after
   saving. It's in every Windows build since at least 25 Oct. With
   `-force-d3d11` the quit is clean, but frames are about 7% slower (mean) and
   12% slower (p95). Options:
   (1) put D3D11 first (Build);
   (2) end the process ourselves on quit (Gameplay; check Velopack's restart);
   (3) leave it and look for a Unity or driver fix.
   Build's research (2026-10-27): no fixed Unity 6.3 version exists. Ranked:
   (a) test whether players see it at all;
   (b) drop in Agility 1.618.5's `D3D12Core.dll` (1.618.1 is a known
   regressed build), copied in by a post-build step in Build's lane;
   (c) end the process at quit (Velopack is safe; shut down Netcode first);
   (d) graphics jobs off, as a test only;
   (e) D3D11 first.
   Proposed PC tests: A (visibility) and B (the Agility swap on an existing
   build). Andrew to decide.

## Log

- 2026-10-27: lanes approved. Removed 11 merged agent worktrees under
  `.claude/worktrees/agent-*` and their branches. The spell radial was
  already on `dev`.
- 2026-10-27: branch cleanup rule added. Deleted 5 merged local branches and
  13 merged remote branches, including `feat/spell-radial`, whose content was
  already on `dev`. Left on `origin`: `dev`, `main`,
  `perf/no-static-batching`.
- 2026-10-27: Andrew approved slice 5. World does part A (the chunk event),
  then Gameplay does part B (props sleep and wake). The coordinator is now
  named "Project Manager".
- 2026-10-27: the perf branch merged (`d4ae548`), then slice 5 parts A
  (`3c1d848`) and B (`a473873`). Lane slip: part B added a row to
  `docs/FAST_LOOP.md` and a paragraph to `docs/WORLD_STREAMING.md`, both
  World's. Accepted, World told, Gameplay reminded to ask first.
