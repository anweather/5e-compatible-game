# Coordination

Three Claude sessions work on this repo at once, on two machines. This file
says who owns what, so they don't collide. Andrew approved the lanes on
2026-10-27.

The coordinator is the Claude session **"Project Manager"** (PC; it was called
"Unity skill install" before). Message
it with SendMessage. It doesn't write game code.

## The coordinator's responsibilities

The coordinator ("Project Manager") doesn't write game code unless Andrew
asks. It is responsible for:

1. **Lanes and ownership:** each file has one owner. It approves and
   schedules every edit outside a lane, and every shared-file change.
2. **Merges:** it sets the merge order, reviews each branch on `origin/dev`
   (the files touched, staying in lane), and has agents rebase in turn.
3. **The PC's Editor slot:** one heavy Unity job at a time. It hands the slot
   from session to session.
4. **Handing out work:** it gives the next task to each agent as it
   finishes, and keeps "Current tasks" here up to date.
5. **This file:** it keeps COORDINATION.md current and pushes it to `dev`
   from its own worktree, `.claude/worktrees/coordinator`.
6. **Judgment calls:** it checks `JUDGMENT_CALLS.md` before asking Andrew
   anything. It asks Andrew only when no decision covers the question, then
   records the answer, the reason, where else it comes up, and how many
   times it has been revisited.
7. **Branch cleanup:** it deletes branches merged into `dev` (J-004).
8. **The demo reel:** it collects a clip or screenshot of every feature you
   can see from the agent that merges it, and appends it to the Valley Demo
   Reel artifact (see "Demo reel" below and J-011).
9. **The playtest bot:** it oversees the bot (World's lane) and relays its
   backlog and digests to Andrew. The bot finds; it never fixes.
10. **Reporting to Andrew:** status, findings from the other sessions, and
    the decisions that are his to make.

It asks Andrew first before it:
- deletes files or unmerged branches;
- force-pushes;
- resolves a conflict where one agent's work would be lost;
- changes the overall plan.

## The project update (coordinator to Andrew)

Andrew gets one short update in this shape, so he doesn't have to read
everything. It comes when he asks, and unasked after a milestone (a merge
or a finished task), or when something needs him. It fits on one screen:

```
PROJECT UPDATE · <date time> · since <last update's time>

WINS     · <what merged or was verified, one line each, with its commit>
FAILS    · <what broke, or a test that failed, and what happens next>
BLOCKED  · <what's stuck, and on whom>

WORKERS  (active % since the last update)
  Gameplay  <task>  <branch>  <busy / waiting on X / idle>  <n>%
  World     <task>  <branch>  ...                            <n>%
  Build     <task>  <branch>  ...                            <self-reported>

COMPUTE  (the PC; Unity is the bottleneck)
  Unity running <n>% · working (build, probe, player) <n>% · two Editors <n>%
  No-Unity gaps: <time for n min: why>   · Editor slot: <who has it, and who's next>

NEEDS YOU
  1. <decision, with the options, my pick first>
```

How it's measured: `tools/coordinator/sample.sh` runs in the background on
the PC and logs one row a minute. Each row says whether each PC worker's
Claude transcript was written in the last 3 minutes, or a Unity batch job is
running on its checkout. It also counts Editors, players and batch jobs, and
records free memory and CPU. The log is
`.claude/worktrees/_coordinator-logs/activity.csv` (not in git), and
`tools/coordinator/report.sh <hours>` summarises it. The Mac reports its own
busy and idle time.

The rule: if no Unity has run on the PC for 15 minutes while any lane has
work, the coordinator finds out why and fixes it. Either it gives the Editor
slot to someone with Unity work, or it moves Unity work earlier in the
queue. An alert fires when that happens (`report.sh --idle`).

Agents report to the coordinator in the same shape, briefly: Done, Failed,
Blocked, Next, and whether they hold Unity.

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
   Force-pushing is allowed only for your OWN unmerged feature branch,
   after a rebase, with `--force-with-lease`; never `dev`, `main` or
   another agent's branch (J-016).
6. Tell the coordinator when a task is done or blocked.
6a. Judgment calls for Andrew go through the coordinator, who asks him with
   the AskUserQuestion tool. Check
   `JUDGMENT_CALLS.md` first: if a past decision covers it, follow that.
7. Never discard, reset or overwrite work you didn't write. If you find
   uncommitted work in a file outside your lane, stop and report it.
8. On the PC (J-017): two Editors may be open at once if 10 GB or more is
   free. MPPM runs only with ONE Editor open. Perf measurements and Windows
   player builds need the machine to themselves. So does a first or full
   build (a fresh import and shader compile): beside an open Editor it took
   commit headroom down to 4 GB. Ask the coordinator before
   opening an Editor, starting MPPM or measuring perf. The Mac is a second
   test rig for other lanes' branches.

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
| World | `unity/Assets/Prototype/Scripts/ChemGrid.cs` and every `ChemGrid.*.cs` partial (Effects, Rules, Shaping, Visuals), `Scripts/Net/NetChem.cs`, `Scripts/ProbeExplore.cs` |
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
  runs it in a checkout that commits. The playtest bot's detached
  `.claude/worktrees/playtest-build` checkout may rebuild the yard for its
  builds, because it never commits or pushes.
- **`.meta` files:** never edit a GUID. Commit each new asset together with
  its `.meta`, in the owner's lane.

## Current tasks

| Agent | Task | Branch | State |
|---|---|---|---|
| Gameplay | sky-ambient (`057a182`) and fps-timing (`cc7b22f`) are merged. Now, code only: perf G1 (ValleyGround buffers, spreading SyncHeightmap, dirty-box paint upload, a detail cap), the paint budget on tick frames, and a look at ValleyFauna. Then reset part B (J-014) after World's part A. Possible later: a terrain wetness shader for rain (rain no longer draws a film on open ground) | own branches | Editor released |
| World | HAS THE PC EDITOR (from about 14:00): the verification script for gas-pool, tick-spread, still-water and rain-water-mesh (`a193510`), with --online. Then the .NET runner (J-013 d), reset part A (J-014), then hardness (WIP `e18a075`). The playtest bot (J-008). Phase 3 code is done (`4510dee`, `37ba28c`). Phase 2 done (`b880057`): it compiles, runs off-screen and muted, and made 4 real runs. Its first real paper cut: Hasten Time frames take 0.8–2.2 s. Exact replay drifts (follow-up). Now phase 3, code only (coverage, crash and hang checks, build.ps1); then about 27 min of the build slot. Stop before the scheduled tasks. Queued next, perf-pass items: (0) FIRST: ChemGrid.WaterMesh under rain, 2.3–2.8 s frames (the bot's Hasten Time and Control Weather spikes; branch `rain-water-mesh`); (1) RenderGas instantiating gas particle prefabs: each Awake runs a full particle update, about 36 ms (the PC's worst frames; pool them or turn Prewarm off); (2) the tick hitch: `tick-spread`, a resumable tick sliced across frames, lockstep-exact; (3) slice 6, still water costs nothing; (4) W1 (dirty-cell render). (The Hasten Time frames are NOT extra ticks: it's the sky and weather view, so they go to Gameplay) | `playtest-bot` | In progress |
| Build | Backlog #3, the performance pass. Mac M1/M3/M5 done: the worst spikes are SkyLight's ambient readback (up to 122 ms; routed to Gameplay as `sky-ambient`) and the chemistry tick in one frame (about 15 ms; routed to World). The steady cost is ChemGrid.Render walking every cell (5.3 ms; W1, World). Now: B1 (URP settings) and B2 (occlusion), one at a time | `perf-pass` | In progress |

## Shared-file queue

The coordinator approves each change and schedules them one at a time.

| # | File | Agent | Why | State |
|---|---|---|---|---|
| 1 | `docs/FAST_LOOP.md` (World's) | Gameplay | The `online` row's new reset checks, one commit on `net-reset-view` | Lent 2026-10-27 |

## Merge order

1. ~~`git-path` (Gameplay: builds find git off the PATH)~~. Merged as `0bf2208`.
2. ~~`perf/no-static-batching` (Build)~~. Merged as `d4ae548` (473 → 267 MB on
   Windows, frame times within noise). Restart Unity after pulling; the first
   player build takes about 14 min to compile shader variants.
3. ~~Slice 5, part A (World: the chunk event)~~. Merged as `3c1d848`.
4. ~~Slice 5, part B (Gameplay: props sleep and wake)~~. Merged as `a473873`.
5. ~~`sleep-clip` (Gameplay: the probe films the crate)~~. Merged as `e517d79`; on the reel.
6. ~~`quit-exit` (Gameplay: the game ends itself on quit)~~. Merged as `4a61ee3`: quits take 0.15–0.3 s, with no crash or dump; `-clean-quit` restores Unity's own shutdown. Velopack's restart is untested (Build checks it at the next ship-update).
7. `playtest-bot` (World), in phases, when each is ready.
8. `perf-pass` (Build), in slices (the SSAO choice waits on Andrew).
9. World, in this order once each passes its Editor turn:
   a. ~~`rain-water-mesh`~~ `f27d0b7` (the rain worst frame from 2.7 s to 75 ms); ~~`gas-pool`~~ `afbe09d`;
   b. ~~`tick-spread`~~ `41de805`;
   c. ~~`still-water`~~ `f3a8964`; `net-reset` waits for the online run (with Gameplay's part B, MPPM);
   d. `hardness`: passed, merging now;
   e. `playtest-bot`.
10. Gameplay: ~~`static-resets`~~ `46abc35`; ~~`villagers`~~ `4e74992` (on the reel); `net-reset-view` (after the online run); `ground-perf` (perf numbers need a solo slot); `showcase-fire` (the Mac is verifying it).
11. Build: `perf-pass` (cheap SSAO and soft Medium, `49bd690`) after PC-5 on a solo PC.

## Release 0.1.5 (J-019; today)

Waiting on, before the freeze:
1. `net-reset` and `net-reset-view`: Gameplay's solo online run.
2. `perf-pass`: PC-5 on a solo PC.
3. ~~`playtest-bot`~~ `20c499a` (the .ps1 files are gone; `playtest build --ref <tag>` is required).

Then:
1. Freeze dev (the coordinator calls it).
2. Build merges dev into main and bumps the version.
3. The release-candidate builds: Windows on the PC, Mac on the Mac.
4. The bot runs on the Windows RC (J-018), plus Andrew's look.
5. Fix any blockers on main, and merge back.
6. Publish.

After that, a release at least every 2 days, and sooner while a lot is landing (J-019).

The work order comes from "Priority" in `docs/BACKLOG.md` (J-012).

## Open questions

(none: the quit crash was decided as J-010, and the backlog order as J-012)

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
