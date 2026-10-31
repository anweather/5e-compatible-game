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
   Never pass `--force` past the playtest runner's preflight: if it says
   no, wait for the PC.
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

A branch that is fully merged into `dev` is deleted, both locally and on
`origin`, right after the merge is pushed, always (Andrew, 2026-10-29), without
asking. The coordinator does this (`tools/workers/land.sh push` does it for
worker branches). Agents delete only
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
| **Workers** (`w1`, `w2` on the Pro account; `w3`, `w4` on Max once signed in) | Hyper-V VMs on the PC (`tools/workers/README.md`), each with its own clone; driven by the Pro coordinator (`coordinator`, reached through `tools/workers/bridge.sh`) | Headless work, no Editor: core and sandbox code with tests, in both engines on one branch per feature. They take World-lane items when the coordinator assigns them; World rebases on what they merge. One task per boot: once its branch is merged the coordinator stops the VM, which ends its Claude session, so the next task starts with an empty context. The coordinator lands each branch with `tools/workers/land.sh` (its merge commit carries Coordinator-* cost lines beside the worker's Worker-* ones, and the branch is deleted) |

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
| World | `unity/Assets/Prototype/Scripts/ChemGrid.cs` and every `ChemGrid.*.cs` partial (Effects, Rules, Shaping, Visuals), `Scripts/CellWaterMesh.cs` (the water surface ChemGrid.Visuals draws), `Scripts/Net/NetChem.cs`, `Scripts/ProbeExplore.cs` |
| World | `rules/**`, `core/**`, `sandbox/**` |
| World | `world/` except `looks.json` |
| World | `docs/WORLD_STREAMING.md`, `docs/FAST_LOOP.md` |
| World | The playtest bot (new): `unity/Assets/Prototype/Scripts/Playtest/**`, `tools/playtest/**`, `playtest/**` |
| Build | `unity/ProjectSettings/**` (including the version) |
| Build | `unity/Packages/manifest.json`, `unity/Packages/packages-lock.json` |
| Build | `unity/Assets/Settings/**` (the URP assets) |
| Build | `unity/Assets/Prototype/Scripts/Editor/AssetPacks/**`, `unity/AssetPacks.json`, `unity/Assets/Prototype/Scripts/Editor/UpdateBuild.cs` (release builds) |
| Build | `unity/Assets/Prototype/Scripts/Perf/**` (new: rendering and perf tooling, e.g. the pipeline-state trace and warm-up) |
| Build | `tools/ship-update.sh`, `docs/RELEASING.md`, `unity/ReleaseReadme.txt` |
| Workers (the Pro coordinator) | `tools/workers/**`, `docs/handoffs/pro-coordinator-report.md` |

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
| 1 | `docs/FAST_LOOP.md` (World's) | Gameplay | The `online` and `villagers` rows | Done (merged) |
| 2 | `docs/WORLD_LIFE.md` (shared) | Gameplay | Item 4 marked built | Done (merged) |
| 3 | `docs/GLOSSARY.md`, `docs/MODDING.md` (shared) | World | The "Component" entry and the MODDING example, on `components` | Approved; merges with 0.1.6 |
| 5 | `docs/FAST_LOOP.md` (World's) | Gameplay | The `wet` and `night` probe rows, on `rain-wet` and `night-view` | Lent 2026-10-27 |
| 4 | `unity/Assets/Prototype/ThirdParty/Punchy/Sky.hlsl` (shared, third-party) | Gameplay | A second star layer and _PunchyStarBright, on `night-view` | Approved after the fact; change comment and README row added; used under Zachary Franklin's permission, confirmed by Andrew 2026-10-27: it covers all of Punchy, as they are writing it together (J-022) |

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

## Release 0.1.5: SHIPPED

Published 2026-10-28 02:23 UTC as a pre-release:
https://github.com/anweather/5e-compatible-game/releases/tag/alpha-0.1.5
(the tag is at `4a9af50`).
- Size: Windows 442 → 134 MB, Mac 458 → 153 MB.
- The in-place update 0.1.2 → 0.1.5 was checked on the Mac.
- Still to check: the Windows in-place update with QuickExit (PC-R4: the
  test channel's 0.1.104 → 0.1.105).

Finding (Build, 2026-10-27), for Gameplay, low priority: on the FIRST online
probe after an Editor start (with either domain-reload setting),
prop_moved_matches and player_knockback_matches can fail. After a client's
Thunderwave, the crate was 63 cm off and the host wizard's copy about 16 m off
on the client. It's possibly a first-frame or physics warm-up issue on the
virtual player. Real, but not caused by J-023.

Next release: 0.1.6, within about 2 days (J-019). The PC builds Windows and
the Mac builds macOS (J-025).

FREEZE LIFTED 19:16. The RC (main `4a9af50`, the draft alpha-0.1.5) is built and fully tested; publishing waits on Andrew's approval on the Mac. dev is open for 0.1.6, one merge at a time on the coordinator's go.

Landed before the freeze:
0. ~~`perf-pass`~~ `0bd59b6` (cheap SSAO, soft shadows Medium: PC -9% mean, Mac -13%).
1. ~~`net-reset`~~ `7d721a3` and ~~`net-reset-view`~~ `08ad78f` (online 20/20, checksums equal after the reset).
3. ~~`playtest-bot`~~ `20c499a` (the .ps1 files are gone; `playtest build --ref <tag>` is required).

RC tests: PC-R1 install over 0.1.4 and update (Gameplay); PC-R2 the bot on the
Windows RC portable zip (World); PC-R3 a Mac-to-PC Relay game (Gameplay and
Build); PC-R4 the Velopack restart check, after the publish.

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
- 2026-10-27: 0.1.6 merges, World: playtest-findings-0.1.5 `bf848c8`,
  playtest-player `1049b53`, components `5a2c9f6`, night `6213c4b`,
  stream-disk `be437fa`. Merged branches that sessions couldn't delete
  (their permissions refuse it): net-reset, playtest-findings-0.1.5,
  playtest-player, components, night, stream-disk. Andrew can delete them.
- 2026-10-27 (evening): Andrew is low on tokens. No new big features until
  THURSDAY 2026-10-29: caves (J-020 decided), finer ground (J-021 open) and
  interest by chunk (J-028, waits for the continent) all start then or later.
  Until then, agents finish what's in flight (cloth round 3 and the beard;
  check-bot-sparks and the D3D12 warm-up; the Metal warm-up), merge on the
  coordinator's go, and then stop. Idle is fine. Held, unmerged:
  `soundscape`, `terrain-reach`, `bot-fixes`, `spark-pool`, `perf/pso-trace`,
  `wizard-cloth-v2`.
- 2026-10-27 (late): Andrew wants the playtest paper cuts
  (`playtest/andrew-2026-10-27.md`) worked NOW, in priority order (P1, then
  P2). Only caves stay paused. To work them in parallel, the coordinator
  lends Gameplay-lane files per item (one small branch per item; merge on
  the coordinator's go; a before/after screenshot each):
  - World: P1 #1 signs (and the mirrored-text audit), #2 villager faces,
    #3 villagers sitting, #4 houses (roofs, doors, shutters, pots), #5 well
    collision, #8 deer habitat, #10c lights in burnt houses. Lent: the
    village, defense and coast builders, Village.cs, ValleyFauna (folk and
    habitat), Seat.cs.
  - Build (Mac): #6 Hasten Time sun smoothing (SkyLight.cs), #10 court gate
    direction (CourtGate.cs), #10a meteor shake (its camera-shake code),
    #7 underwater view. Lent: those files.
  - Gameplay: after cloth round 3 and the beard: #9 swim speed, #10b camera
    in the ground, #10d throwing, #10e lantern, #10f roll, #10g tree
    see-through, #10h camera snapping. Then the P2s, shared out the same
    way.
- 2026-10-27 21:45: FREEZE for 0.1.6 (J-031; Andrew: "prioritize it"). dev
  takes no merges except the splashes already approved. The release goes:
  Mac measures cloth cost → if it passes, Build merges dev into main and
  bumps to 0.1.6 → in parallel the PC (Gameplay, main checkout, warm shader
  cache) builds Windows with ship-update --windows-only and the Mac builds
  macOS → R1 install plus R3 Relay (no bot run: alpha, J-026) → publish on
  Andrew's go. Every other job yields the PC to the release.
- 2026-10-28 05:56 UTC: 0.1.6 PUBLISHED on Andrew's go (J-033), https://github.com/anweather/5e-compatible-game/releases/tag/alpha-0.1.6 (the tag is at `18d10c6`). main merged back into dev (`8e9db46`); fix/splash-online on dev (`502d4d8`) for 0.1.7. R1 install and R3
  Relay passed; the only known issue is the splash log error, fixed in
  `fix/splash-online` for 0.1.7. FREEZE LIFTED: paper-cut verification
  resumes (World's Editor batch, then Gameplay's), merges one at a time on
  the coordinator's go.
- 2026-10-28: FRUGAL MODE (Andrew: running low on usage). One agent works
  at a time, on small goals, in this order; the others stay idle until
  called. To save round trips, a step's merge is PRE-APPROVED when its own
  checks pass (compile clean, no new warnings, its probes, a before/after
  shot where visual); report once per step, in a few lines.
  1. World: finish the current slot (the snow-mounds look and merge;
     bot-lost-wizard plus the no-activate test and merge; D3D12 traces for
     seeds 103/104, the merge and the commit onto perf/pso-trace).
  2. Build (Mac): merge perf/pso-trace once PsoDirect3D12 is in.
  3. Gameplay: #33 rocks in the river.
  4. World: #31 water down slopes, then #32 a flowing river surface.
  Then stop. Nothing new is started without Andrew.
- 2026-10-28: Frugal queue done (#33 20a67f8, #31+#32 bcb7aba, soaking and
  ground types 44ea217). Andrew: the next direction is refining the systems:
  material types, their properties, and how they react to effects. First step,
  World: an optional "material" on entities.json and items.json, a building
  spec (wall material per WallKind/RoofCover), and the house builders stamping
  their footprint as cells of that material, as the cabins do. LEND to World
  for this step: the village and house builders (Gameplay's; Gameplay is
  idle). "flesh" for creatures comes later. Merge pre-approved on checks.
- 2026-10-28: FOCUS (Andrew). The game is about material interactions,
  attributes and effects, spell effects, and the little mini games (finding
  what's fun). World look and feel is a side quest, not critical. Zach is
  improving terrain and looks in Punchy: don't spend effort porting it
  (BACKLOG 17-20 drop to the bottom). The coordinator challenges any plan,
  Andrew's included, that drifts off this focus before queuing it.
- 2026-10-28: Release 0.1.7 approved (Andrew), to be cut after World's
  materials migration steps 1-4. Low tokens: the release runs as scripted
  (RELEASING.md, ship-update.sh) with no extra exploration. PC: World runs
  --windows-only; then Mac: --mac-only into the same draft; then the bot
  runs on the RC; then Andrew publishes.
- 2026-10-28: RULE (Andrew): every release gets player-facing notes before it
  is published: the intro, the Install (new players) section copied from
  the last release, what's new, what's fixed, performance, any first-launch
  notes. The coordinator writes them from the merges since the last release
  and sets them on the draft; always keep budget for this. 0.1.7's notes
  were added after publishing.
- 2026-10-29: BUDGET (Andrew). Frugal mode is for when usage is low; the
  normal mode is now LEAN, so the weekly plan lasts the week while features
  keep landing: screenshots only when they decide something, short
  fixed-shape briefs and reports, analysis sized to the decision, batched
  tasks, cheaper models for read-only jobs, the reel at release time, and a
  mid-week usage check. See .claude/skills/agent-coordinator ("Budget").
- 2026-10-29: WORKERS (Pro coordinator). Worker VMs w1 and w2 built five materials
  features headless, in both engines, merged on core and sandbox checks:
  soak.dampens (8823b4c), damp burns shorter (4d42d9c), frozen things
  shatter (d5214d8), rust (8d05ccd), acid and Acid Splash with a dissolve
  verb (de14cc1). MATERIALS step 6 is done. Each took 8-20 min, and two VMs
  run side by side within J-037. They worked in World's lane (rules/, core/,
  sandbox/, Chemistry/): World, rebase on dev before continuing your branches.
  The view still needs Gameplay: rusted-metal's colour (CellDressing.cs),
  Acid Splash's radial slot (a decision: every group is full), icon, VFX and
  sound, a shatter effect, and probes for all of them (host Editor).
  Worker commits carry Worker-* cost trailers (git log --grep Worker-Tokens).
  Report and how the PM reaches the coordinator:
  docs/handoffs/pro-coordinator-report.md.
- 2026-10-29: RULE (Andrew): the host's Unity Editor and GPU are used only
  when a task needs them (views, shaders, probes, screenshots, clips,
  performance runs). Everything else runs in the worker VMs, including Unity
  batch work: compiling, imports, EditMode tests and batch builds. The desktop
  is left free for playing. Workers w3 and w4 (Max) get set up tonight. The
  GPU-partitioning test for VMs is parked.
- 2026-10-29: RULE (Andrew): effects are GPU-first. Show materials and
  effects with shaders, vertex colours and pooled particles, not by spawning
  many entities or GameObjects. When objects are really needed, build them
  procedurally from small simple pieces (as the world builders do), not
  high-detail models. LEND to the Workers (Pro coordinator) for the view pass
  on the new materials: the Gameplay view files those features need, listed
  in its report. The host Editor does one probe and screenshot pass after.
- 2026-10-29: RULE (Andrew): never stop a worker VM with unpushed work.
  Before stopping one for any reason (usage limit, swapping accounts, memory),
  commit its work in progress and push it as `wip/<worker>-<feature>`. Lost
  today: w1's shock-water, redone on w4. TODO (Workers lane): stop.cmd does
  this itself (commit and push WIP over PowerShell Direct) before shutting
  down, and refuses if the push fails unless -Force is given.
- 2026-10-29: DONE: the WIP TODO above (21d7df6). stop, sleep and
  remove-worker push unpushed work as wip/<worker>-<branch> first and refuse
  unless -Force; rescue.cmd recovers a stopped worker whose account is out
  of usage. Tested on the host: w3's stop (nothing unpushed) and a rescue of
  w1 (pushed wip/w1-shock-water).
- 2026-10-29: Host Editor pitfall: start Unity Hub before the Editor, or it
  waits at a licence prompt (it looked like a slow Editor). Host sessions
  check for Hub first.
- 2026-10-29: Host sessions' commits carry cost trailers too (Host-*), like the
  workers' (Worker-*). commit-cost.mjs takes COMMIT_COST_PROJECT, the exact
  Claude transcript folder to count (on the host one account runs many
  sessions). The host repo's .git/hooks/prepare-commit-msg (not versioned)
  runs it only in .claude/worktrees/host-ux, counting the repo-root folder
  (the folder that session started in). Add a case there for each new host
  worktree.
- 2026-10-29 night: OVERNIGHT PLAN (Andrew: "keep the lean mode going ... use
  the Pro sessions if you can ... picking stuff off our backlog until
  morning"). Merges pre-approved when their checks pass. Memory: while the
  host Editor (DesktopUnity) works, no worker VMs; after it closes, at most 2
  VMs, Pro first, Max (w3, w4) when Pro is out. Queue, in order:
  1. DesktopUnity: host UX Part 1 (effects shots and fixes), then Part 2 (the
     combo UI and Andrew's HUD layout). Then it closes the Editor.
  2. The combo stand-ins: the 11 of 20 combos marked todo in rules get real
     spells (flame wave, scalding steam burst, fog bank, wave, mud, frost,
     molten boulder, plant growth, dust storm, rain cloud, sand blast,
     whirlwind: whichever are stand-ins), 3 or 4 per worker task, headless.
  3. Tuning from tonight: the oil slick's spread limit and boil-off; Light
     gets a real spell for its d-pad slot.
  4. BACKLOG 21 (player saves): a design note only, no code.
  Stop when the queue is empty or usage runs low, and leave a morning report
  in docs/handoffs/morning-2026-10-30.md.
- 2026-10-29: Host UX Part 1 merged (96c7e10): every new effect seen and
  fixed on screen; panes now break on heavy hits (threshold 120, measured).
  Pitfalls: seed a new worktree's Library only from a checkout whose licence
  was good (a copy taken after a licence failure left pack scripts
  unresolved until a clean rebuild), and copy UserSettings/Search.index too.
  Lend: docs/FAST_LOOP.md to DesktopUnity for the `looks` probe row.
  Queue: S3776 warnings are 8 against the baseline of 6 (Chemistry/), a
  worker task.
- 2026-10-30: Host UX Part 2 merged (cbe8547): the combo UI, Andrew's HUD
  layout, and the combo input online (host-stamped ComboIntent, late-join
  replay, CastHash in the checksum). Controller changes: elements on the
  bumpers and triggers, dodge roll moved to right-stick click, pad zoom
  gone, F2 swaps back to the old layout, the radial stays on Tab. Gaps:
  casts have no tap, hold or ground shapes yet; other players' hand orbs
  aren't drawn; the HUD drawing is untested in a player build (check it in
  the next release candidate). Pitfall: exclude Library/VP when seeding a
  worktree's Library, or Player 2 runs the source worktree's old code. The
  host-ux worktree holds tonight's screenshots: keep it until they're copied.
- 2026-10-30: The mailbox was unreliable (Andrew: "unacceptable"). Mail only
  arrived while the receiving session kept a background `bridge.sh wait`
  alive, and three things killed it unnoticed: Claude Code reaping it when
  memory ran low (the Pro session's launcher lacked the setting that stops
  that), the session ending, and the account hitting its usage limit (the
  coordinator then can't act, so nobody stopped its workers or told the PM;
  w2 idled 3 h overnight). Fix: `tools/workers/bridge-watch.sh`, a watchdog
  with no Claude in it, started at sign-in. It stops an out account's
  workers (work pushed to wip/), mails the other account "out until",
  mails and wakes the role when usage is back, and wakes a role whose mail
  sits unread while nobody listens. Both Pro launchers now keep their
  listener under memory pressure. Tested dry against fake and real
  transcripts; running since 08:39.
- 2026-10-30: Workers stuck at a prompt nobody sees (w3 waited 1 h 40 min
  to push whirl-dust). Cause: in auto mode a worker session may push only
  the branch of its first task; a new task's branch needs Andrew's approval.
  Detection: `start.cmd` installs an attention hook in each worker
  (attention.mjs: Claude Code's Notification hook, permission prompts only),
  and bridge-watch.sh reads it once a minute and mails the PM, who tells
  Andrew. Prevention is Andrew's call, not the agents': a permission rule in
  the workers' settings allowing pushes to any branch but dev and main
  (never forced, never deleting). The auto-mode check refused to let the PM
  write it into the workers, and refused a brief rule that would have
  pushed to another branch to avoid the prompt (rightly: that routes round
  the approval). Until Andrew applies the rule, a worker's push may wait
  for him, and the attention hook makes sure he hears of it in a minute.
- 2026-10-30: UX pass 2 merged (411bea6, DesktopUnity; Editor closed).
  Wizard: robe hides the robot but hands, shins and feet; black fuzzy face,
  red slit eyes; hat or cowl (Esc menu, sent with the seed). Looks for
  Forge, Falling Star, Whirlwind's carries, the dust flash, Lava Pool, Rain
  Cloud, Sand Blast, Light, the pick, frozen raiders; spiderweb cracks. Bugs
  fixed: a cast's dust flash never drawn, sand left the ground black, a
  Whirlwind particle warning. 509/509 EditMode, 453/453 core. New probe
  `spelllooks` (row added to FAST_LOOP.md). Open: cracks break up on bulging
  ice; frozen raiders show cracks as colours, not a web; a crate at 8 m/s
  can land flat and miss the pane threshold (149 vs 120, flaky in `looks`);
  WizardBeard unused with the new face (Andrew's call); the WizardFace and
  CellLava shaders not yet checked in a player build; NetWizard.prefab
  changed, so other checkouts rebuild.
- 2026-10-30: The first real wake (11:56) worked, after two fixes. The Pro
  coordinator went idle and compacted at 09:17; its listener took the w1
  water brief at 11:29, but the session never reacted, and the mail sat in
  taken/ for 27 min. (1) Taken mail went back to the inbox only inside a
  running `wait`, so with no listener nothing returned it; bridge-watch.sh
  now returns mail taken and not acked in 10 min. (2) wake judged "busy" by
  the transcript file's time, but an idle Remote Control session's file is
  still touched; it now goes by the last message's own time. w1 is briefed
  (combo-water).
- 2026-10-30 13:50: Release 0.1.8 (Andrew: Option C, "publish when checks
  are passed", notes of the same calibre, and squeeze in the crack polish).
  The combo table's rules are all on dev (b33c6bd). Order: UX pass 3 on the
  PC (DesktopUnity, docs/handoffs/ux-pass-3-brief.md, about 1.5 h) → FREEZE
  → dev into main, version 0.1.8 → the PC builds --windows-only and the Mac
  --mac-only into one draft (RELEASING.md step 4) → notes on the draft
  (docs/handoffs/release-0.1.8-notes.md, finished from UX pass 3) → the
  Windows install from the draft, a quick look, the Mac install → publish.
  No worker VMs until it's out; the PC is the release's.
- 2026-10-30 14:00: 0.1.8 is Windows only (Andrew: the MacBook isn't signed in; skip the Mac build). The PC builds with --windows-only; the Mac stays on 0.1.7 and catches up at 0.1.9 (its feed only reads releases with osx files, so it simply sees no update). The notes say so, including that 0.1.7 Macs and 0.1.8 PCs can't play online together.
- 2026-10-30 15:45: UX pass 3 landed (39ee1a7, fa76118; DesktopUnity): all of
  part 1 and part 2 (crack webs on windows, ice and frozen raiders; the crate
  throw at 11 m/s), plus Andrew's staff flare (one NaN pixel). EditMode
  522/522, core 466/466, sandbox 190/190. FREEZE for 0.1.8 (Windows only):
  dev takes no merges until it's published.
- 2026-10-30: RULE (Andrew): playtest findings go on GitHub. A new issue gets
  a GitHub issue (label `playtest`) with a "Discovery cost" section: the
  session's cost (commit-cost.mjs --record, docs/telemetry/sessions.jsonl)
  and the issue's share. An issue found again gets a comment on its open
  GitHub issue with the rediscovery's cost (label `rediscovered`), never a
  duplicate. Issues we keep paying to find again rank first for fixing.
  The steps: playtest/sessions-0.1.8-brief.md, step 5. Gameplay sessions run
  on the Pro account, as a fresh sub-agent on the host.
- 2026-10-30 16:15 (23:15 UTC): 0.1.8 PUBLISHED on Andrew's go,
  https://github.com/anweather/5e-compatible-game/releases/tag/alpha-0.1.8
  (tag at 4378764), Windows only, pre-release. RC checks: install from the
  draft, 0.1.7 → 0.1.8 in place, no exceptions; the crack webs not seen in a
  build. Known issue: the robe tail after a long walk (also 0.1.7); the fix
  (snap on resume) goes to DesktopUnity after the playtest. main merged back
  into dev. FREEZE LIFTED.
- 2026-10-30 16:50: Playtest of 0.1.8 on Pro (seeds 811-813, merged as
  c486d84): #2 new and a real 0.1.8 regression (cracked ground and ice draw as
  cream squares in the player; the Editor draws the web), #3-#5 rediscovered
  (waterfall pool band, robe tail, 185 ms walking hitch). 9.6 issues/hour
  against 9.5 for 0.1.5's runs; about 200k tokens, 50k per issue. DesktopUnity
  has #2 and #4 for a 0.1.9 hotfix. Gaps: the bot can't cast by elements (the
  12 combo spells untested); the runner crashes with no default audio device
  (AppMute); the cost hook can't see the Pro sessions (no transcript on disk
  for the sub-agent, and the coordinator's own file unwritten since 15:56),
  so the cost is the harness's count. Issue links now point at commits, not
  branches (merged branches are deleted).
- 2026-10-30 17:40: Hotfix on dev (499774e, DesktopUnity): the robe snaps
  back when seen again (#4; seed 801's route clean in a player build), and
  webs no longer speckle. #2 was NOT a crack bug: the cream squares are the
  spell aim markers (SpellCaster.ShowMarkers, since 2026-10-24); crack webs
  draw in a player build on glass, ice, panes and a frozen raider. The
  playtest triage misread them, and the bot on a locked PC renders almost
  only at its shots (the robe tail was mostly that). Closing #2, and the
  comments on #2 and #4 (refused to DesktopUnity as an external write), are
  Andrew's. Now: w1 bot-elements, w2 spell-golf (Pro). start.cmd no longer
  takes Unity Hub's helper for an Editor.
- 2026-10-30: RULE (Andrew): the PM and the Pro coordinator may write to
  this repo's GitHub issues without asking: file issues, comment on them
  (fix notes, causes, rediscoveries), label, retitle, and close them when
  the fix ships in a release. Each write says what changed and why. Not
  covered: deleting issues, releases (publishing stays Andrew's go), repo
  settings. Workers and host sessions hand their issue text to the PM, who
  posts it. First use: #2 is now "Redesign the spell aim markers" (label
  design; it was the aim markers, not cracks), and the fix notes are on #2
  and #4.
- 2026-10-30 18:15: DECIDED (Andrew): workers take briefs straight from the
  mailbox (option A), built once w1 (bot-elements) and w2 (spell-golf)
  finish. start.cmd <vm> <brief> copies the brief into the VM
  (~/.worker/inbox; Copy-VMFile or ssh) and the worker starts on it; its
  report goes to ~/.worker/outbox, which bridge-watch.sh pulls into the PM's
  mailbox on its once-a-minute pass (as with attention.json); a follow-up is
  a second file in the inbox, which a free background listener in the worker
  wakes on. No coordinator session in between. To test: that a file landing
  in the inbox reliably wakes the worker.
- 2026-10-30 20:00: Pro out until 23:30 (5-hour limit, hit by w1 and w2 at
  19:56). The runner missed it ("5-hour limit" didn't match) and spent the
  queued charged-casts brief on the refusal; fixed (4e84cac): any limit puts
  the brief back as a resume and waits. Merged before: half-built (ba064e1:
  snow, fog, the gate puzzle) and playtest-rules-a (00bfd3d: #11 the wave
  aims and leaves water, #15 no brick under water). w1 and w2 stopped (WIP:
  wip/w1-moving-b, wip/w2-playtest-rules-a). Max takes over (Pro first, then
  Max): w3 has charged casts (#7), w4 finishes moving-b and the boulder (#21)
  from the WIP, both through the direct mailbox.
- 2026-10-30 21:00: DECIDED (Andrew, one at a time):
  1. Charged casts cost time: each charge level adds cast time (full charge
     about 2 s), so tapping stays the quick option (#7).
  2. Raise Earth held is a strong raise (charge 3: a tall pillar or mound),
     not Lower Earth; digging stays with the pick and a cast aimed at your feet.
  3. Zero health (#14): downed, then respawn. At 0 you fall and drop what you
     carry; about 3 s later you're back at the yard with full health.
  4. Sleet Storm: a slippery field, as in 5e (wide frost glaze, creatures
     slide, fires go out); Frost stays the precise one.
  5. The punch goes on Circle (pad), when not casting (B still cancels a
     cast); F on keys. Square keeps picking up and sitting (#18).
  6. Voice: the sound pass first; Vivox voice waits for Andrew's setup.
- 2026-10-30 21:25: UX pass 4 part 1 merged (aeb98ad; DesktopUnity): #2
  targeting markers by spell shape (new probe aimlooks), #8 HUD (stamina
  a thin line under Health and Magic; the Health and Magic block read as the
  "top bar"), #9 the cast bar fills smoothly with charge steps, #10 Back
  closes the grimoire. Notes posted on the issues. Not yet played by hand or
  in a player build (part 3). FAST_LOOP: aimlooks row, combo's stamina line.
  Part 2 (head flare, hat, fire, camera at houses) running.
- 2026-10-30 21:35: DECIDED (Andrew): a performance pass after UX pass 4
  (docs/handoffs/perf-pass-brief.md, host, player build, -fps-probe, the
  Profiler; issues labelled perf), then a project skill `perf-pass` written
  from how it actually ran. Order on the PC: UX pass 4 parts 2-3, Andrew's
  five calls (worker), the perf pass, then the sound pass.
- 2026-10-30 21:55: Linux workers. u1 (Pro) works through the direct
  mailbox with no trust click (headless runs skip it): its test brief came
  back as a usage-limit report and retries itself after Pro's reset. u2
  (Max) cloned from ubuntu-template (1-6 GB), in bridge-watch's Max list;
  first boot waits for the host Editor to close (8.8 GB free, 8 must stay).
  u2 needs Andrew's one-time sign-ins in the VM: Claude (Max) and GitHub.
- 2026-10-30 22:05: DECIDED (Andrew): the Linux margin beside an open Editor
  is 5 GB (was 8), and the idle Pro coordinator session is closed (workers
  take briefs directly; the PM merges and files issues). bridge-watch no
  longer wakes a role at a reset unless it has mail. Memory, for the record:
  of 31 GB, the Editor and its compiler about 8.6, Claude sessions 2.7,
  Andrew's apps (Firefox, Discord, Steam) about 3.8, Windows and drivers
  about 5. u2 booted (172.26.221.64); it waits for Andrew's Claude (Max) and
  GitHub sign-ins.
- 2026-10-30 22:15: u2 (Linux, Max) is ready: Andrew signed in (Claude Max,
  GitHub anweather), the sign-ins are in its slot, and a test brief came back
  through the direct mailbox. Both accounts now have a Linux worker (u1 Pro,
  u2 Max) for core and sandbox work beside an open Editor (5 GB margin). For
  the next Linux clone: the template lacks /var/lib/worker (sudo mkdir it)
  and its Guest Service Interface was off (start.cmd's name copy failed).
- 2026-10-30 22:20: The MacBook is back (Andrew): compute only, no Mac
  builds. Its session `unity-game-macbook` (Max, Remote Control) has Unity
  with a GPU, so it takes Unity work the VMs can't (EditMode, probes) besides
  rules work. First task: Andrew's five calls (branch andrew-calls), which
  had been waiting for memory on the PC. Replies come by SendMessage; that
  route doesn't confirm reading, so check its branch if it goes quiet.
- 2026-10-30 22:30: UX pass 4 part 2 merged (46848eb; DesktopUnity): #16 the
  head flare (hidden bones at size zero gave NaN normals at the neck), #17
  the hat and cowl fit, #19 fire redrawn as scattered GPU flame tongues
  (Andrew to judge the look; close views about 70 ms p95 in the Editor),
  #20 the camera check was wider than the wizard. Notes posted. New probes
  flare, housecam, flames in FAST_LOOP. Known: castlooks wave_carries fails
  on base code too (part 3 takes it); a sun glint on the ground over 12x in
  150-200 of 960 views (open); not seen on Mac (Flame uses StructuredBuffer).
- 2026-10-30 23:10: DECIDED (Andrew): proximity voice (Vivox, D8) targets
  release 0.1.10, with the sound pass. 0.1.9 ships what's on dev now
  (today's rules, UX pass 4, the five calls, perf fixes). Before voice work
  starts, Andrew sets up a Unity Gaming Services project with Vivox and
  links it to the Unity project (the PM gives him the steps; agents don't
  enter account keys).
- 2026-10-30 23:15: DECIDED (Andrew): the sound pass moves into 0.1.9; voice
  (Vivox) stays in 0.1.10. The MacBook takes the sound pass after Andrew's
  five calls, in parallel with the perf pass on the PC.
- 2026-10-30 23:20: DECIDED (Andrew): the MacBook profiles overnight (weaker
  hardware, a minimum-spec baseline; a macOS player for profiling only),
  after the five calls. The PC takes the sound pass after UX pass 4 part 3,
  and repeats the perf measurements on Windows tomorrow for comparison.
- 2026-10-30 23:35: Andrew's five calls merged (bd488d5, MacBook): core 549,
  sandbox 223, EditMode 609. Caveats for Andrew: (1) Sleet Storm is on no
  combination, so its new slippery field is only reachable from the F2 radial
  (make it Frost's held form?); (2) no element combination digs now (Lower
  Earth is radial-only; a cast at your own feet never dug). The yard must be
  rebuilt before the downed fall shows. The Mac started the overnight perf
  pass.
