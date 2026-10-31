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

## Now and history

- **The current state** (who runs where, what's in flight, the queue, open decisions, standing rules): `docs/STATE.md`. Read that first; it's kept short.
- **History:** the old lanes, file ownership, merge order and the dated log up to 2026-10-31 are in `docs/coordination-log/2026-10.md`. New dated entries go in `docs/coordination-log/<yyyy-mm>.md`, not here.
