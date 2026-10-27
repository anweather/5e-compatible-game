# The playtest bot: design and feasibility (phase 0)

Andrew asked for an autonomous playtester: it wanders the world, plays with
goals, finds paper cuts, reproduces them, and files them with a screenshot and
steps, ranked into a backlog, with a digest of what got fixed. It runs on a
schedule against the real built player. It finds, it never fixes.

This is the World lane's design (2026-10-27). Nothing is built yet. The
decisions Andrew needs to make are at the end.

## Verdict

Feasible, mostly with parts we already have. The probe framework
(`ProbeScenario`, `ShowcaseScript`) runs in a player build as it is: none of
it is Editor-only. It already places the wizard, casts spells, cuts the camera
and takes screenshots, and its ~45 scenarios (tower, swim, court, defense,
wildfire, sleep, menu...) carry checks. `FpsProbe` shows the pattern for a
command-line flag in a player. Builds stamp their commit in
`StreamingAssets/build.json`. The new work is the bot's goal-driven wandering,
the detectors, the run and replay plumbing, and the triage loop.

There are three real risks, each with a spike in phase 1 before anything is
built on it:

1. **Rendering while out of the way.** The player must render (screenshots,
   visual bugs) without stealing the screen.
2. **Sound.** The player must not play sound out loud every 45 minutes, but
   the audio detectors still need the signal.
3. **Exact replays.** Frame timing and physics vary from run to run, so a
   replay needs a locked frame rate.

## 1. Shape

```
Task Scheduler (every 45 min)        Claude scheduled task (every 2-3 h, and a digest every 2 days)
        |                                     |
tools/playtest/run.ps1               reads playtest/runs/* --> dedupes against playtest/findings/
  preflight (memory, Unity jobs)       replays new candidates (run.ps1 -replay) --> confirmed?
  launch player -playtest ...          reviews screenshots for visual oddities
  wait / time out / collect            writes findings, re-ranks playtest/BACKLOG.md, writes digests
  quick code-only replays              (later: gh issues)
        |
playtest/runs/<run-id>/  run.json (seed, build, trace, candidates), shots/, Player.log
```

The bot itself is `Scripts/Playtest/**`. It starts only with `-playtest`, as
`FpsProbe` does with `-fps-probe`, so a normal game never runs it.

## 2. The bot in the player

**Goals, not scripts.** Each run picks a seed. A seeded RNG (never Unity's, so
it replays) chooses a sequence of goals for about 8 minutes, weighted towards
what hasn't been visited lately (it reads a small `playtest/coverage.json`).
The goal library, v1:

- **Go somewhere:** the tower (and climb it), the ball court, the defense
  field, the village, the beach and island, the waterfall, a random far
  meadow. The bot walks, sprints and jumps by writing to
  `StarterAssetsInputs` (move, look, jump, sprint), the same path a gamepad
  takes, with a straight-line steer and simple obstacle tries. When it gets
  stuck it may use Misty Step or a teleport, and logs that it did, so the
  detector doesn't blame the game for the bot's own clumsiness.
- **Play with the chemistry:** cast a random spell from the radial at a
  random target nearby (`SpellCaster.Select` / `Cast`), sometimes combos
  (oil then fire, water then frost, a storm on water).
- **Use things:** pick up and throw an item, push a prop, sit on a chair,
  swim, ride the swell.
- **Menus:** open and close pause, the mods panel, the map, and change a
  setting and back (`PauseMenu.Navigate` / `Submit` / `Back`).
- **Known activities:** run one of the existing probe scenarios as a goal.
  These are scripted, but their checks become detectors for free.

**Replayable.** Every action goes into a trace with its frame number: "goal
tower", "move (0.7, 1) for 40 frames", "cast fire-bolt at (412, 230)". During
a run the bot sets `Time.captureFramerate = 30`. Game time then advances
exactly 1/30 s per frame however long the frame really took, so physics,
animation and the chemistry tick see the same steps on every run. The
chemistry is deterministic already. Seed + trace + build makes an exact replay
on the same machine and build: `-playtest-replay <run.json>` feeds the trace
back frame by frame. A finding counts as reproduced when the replay triggers
the same detector at about the same frame. If a replay doesn't match (PhysX
isn't guaranteed bit-exact even then), the triage step tries 3 replays and
reports a repro rate. Real frame times are still measured for the
performance detectors. Frames are locked, not wall time, so a locked run
measures true cost per frame.

## 3. Detectors

Each detector files a **candidate** with a signature (detector + key, for
example `exception:NullReferenceException@PhysicsProp.Sleep`), the frame, the
trace position, the build and a screenshot.

**Judged in code, in the player:**

| Category | Detector |
|---|---|
| Game-breaking | Exceptions and errors (`Application.logMessageReceived`, plus anything in Player.log afterwards), deduplicated by message and top frame. The player crashing or hanging (run.ps1 sees no heartbeat for 60 s, or the process dies) |
| Game-breaking | Wizard position NaN or infinite; fell through the world (below the terrain by more than 2 m, or below the world's floor); out of bounds; airborne for more than 10 s; stuck: input held but under 0.5 m of progress in 10 s, with no bot teleport (filed low-confidence, since it's often the bot) |
| Gameplay | A spell that did nothing: no cell in its area changed within N ticks when the rules say it should (fire on grass, water into a pit). A menu that doesn't respond (SetOpen, then the state didn't change). An item that can't be picked up when in reach. A seat that can't be sat on |
| Physics | A prop with a NaN position, launched (speed over 50 m/s), lost (below the terrain, or more than 200 m from its spawn), or awake in an inactive chunk (slice 5's rule) |
| Visual | Screenshot checks in code: magenta pixels (missing shader), a black or blown-out frame (mean brightness), the camera inside geometry (a ray from the camera to the wizard blocked by terrain for more than 2 s) |
| Performance | Frame spikes over 100 ms; p95 over budget for a place; chemistry tick over 8 ms; memory growing across goals (a leak); GC spikes |
| Sound | An `OnAudioFilterRead` tap on the listener: peaks at or above 0.99 (clipping), NaN samples, total silence for more than 20 s while sounds are playing (`AudioSource.isPlaying` with nonzero volume) |
| Chemistry | Lingering chunks pinned at the cap for 5 minutes; the probe goals' own checks failing |

**Judged by Claude:** visual oddities code can't name (floating trees, a robe
clipping through a leg, z-fighting, water where it shouldn't be, text off the
screen), and whether a candidate is a real paper cut or the bot being clumsy.
The bot takes a screenshot every goal and at every candidate. The triage pass
reviews new ones with a short checklist and files what it sees as low
priority unless it's obvious.

**Online desyncs** are v2. It needs two players (host and join) running at
once. That's memory we don't have while Editors are open, but the plumbing
(`-host` / `-join` and comparing checksums in Player.log) is cheap to add
later.

## 4. Triage, dedupe, reproduce (the Claude pass)

A Claude scheduled task (every 2-3 hours, not every run, to keep cost down):

1. **Read new runs.** Group their candidates by signature, and match them
   against open findings: the same signature adds an occurrence, and nothing
   new is filed.
2. **Reproduce each new signature.** Run `tools/playtest/run.ps1 -replay
   <run> -at <frame>` up to 3 times; this starts from the run's seed and
   replays its trace. Confirmed means it reproduced at least once (the rate
   is recorded). If it doesn't reproduce, it stays a candidate and is filed
   only if it turns up in 3 separate runs.
3. **Write the finding.** `playtest/findings/F-0001/finding.md` holds the
   title, category, severity, build, the steps to reproduce in plain words
   (written from the trace: "From the yard, walk to the tower, cast Fireball
   at the second platform..."), the exact replay command, how often it
   reproduced, and links to `before.jpg` and the log excerpt.
4. **Re-rank `playtest/BACKLOG.md`.** Order by severity (game-breaking >
   gameplay > physics > performance > visual > sound), then how often it
   occurs, then how recently it was seen.
5. **Digest, every 2 days** (`playtest/digests/<date>.md`). A finding counts
   as fixed when its signature hasn't appeared in the last 10 runs on newer
   builds and its replay no longer triggers it on the newest build. The
   digest takes an `after.jpg` from that replay and shows it next to
   `before.jpg`, and lists what's new and what's still open. It ends with a
   **Fixes drop** for the Valley Demo Reel (COORDINATION.md, "Demo reel"):
   for each fixed finding, the title, area, fixing commit, and its
   `before.jpg` / `after.jpg` paths, which the triage task sends on to the
   Project Manager.

The bot only finds. Neither the bot nor the triage task touches game code.

## 5. Running it out of the way

- **No headless.** `-batchmode -nographics` renders nothing, so no
  screenshots and no visual bugs. A real window and the GPU are needed.
- **ProjectSettings** already have `runInBackground: 1` and
  `visibleInBackground: 1`, so an unfocused player keeps running. The bot
  drives inputs in code, not the OS, so the window never needs focus.
- **Spike 1, rendering:** a windowed 1280×720 player
  (`-screen-fullscreen 0 -screen-width 1280 -screen-height 720`), launched
  minimised or pushed off to a corner. Does it keep rendering, and do the
  screenshots work? The plan is screenshots from a camera into a
  RenderTexture, which don't depend on the window being visible, rather than
  `ScreenCapture`. A Unity player also grabs focus as it starts; check
  whether launching it minimised avoids that. Fallback: run only when the PC
  is idle (Task Scheduler's "run only if idle").
- **Spike 2, sound:** mute the output without blinding the audio detectors,
  for example `AudioListener.volume = 0` with the tap placed before it, or the
  tap on the mixer. Fallback: no audio detectors in v1.
- **Memory.** The player takes about 1.5-2.5 GB. run.ps1 skips a run (and
  says so in a log) when free memory is under 8 GB or a heavy Unity job is
  running: a player build, an Editor in batch mode, or an MPPM player. It
  also never runs two playtests at once (a lock file).
- **The GPU** is shared with any open Editor. It's fine, but a run while
  someone works in an Editor measures performance badly. Performance findings
  only count from runs the preflight marked "quiet" (no Editor open).

## 6. Builds

- The bot runs a player from a build made for it: `Builds/Playtest/` in a
  **dedicated worktree** (`.claude/worktrees/playtest-build`, on
  `origin/dev`). Building there never locks anyone's Editor project, and its
  `Library/` keeps incremental builds fast. The cost is disk: 10-15 GB for a
  second `Library/`.
- **When it builds:** at most every 4-6 hours, only when `origin/dev` has
  changed and the PC's heavy-job slot is free. That's a batch-mode Unity call
  of `PrototypeBuild.Windows`, or a small playtest build method if the Build
  lane prefers. The first build pays the 14-minute shader compile once.
  Incremental builds should be a few minutes, to be measured.
- **Version:** every run records the build's commit (from `build.json`), so
  findings, replays and "fixed in" all name a commit.

Alternative, if a second Library is too much: run against whatever is in the
main checkout's `Builds/Windows/`. That's simpler, but builds come at random
and are sometimes stale.

## 7. Where things live

| Path | What | In git |
|---|---|---|
| `unity/Assets/Prototype/Scripts/Playtest/**` | The bot, detectors, trace and replay | yes |
| `tools/playtest/run.ps1`, `build.ps1`, `install-schedule.ps1` | The runner, the builder, the Task Scheduler entries | yes |
| `playtest/runs/<run-id>/` | Raw runs: run.json, Player.log, screenshots | **no** (needs a `.gitignore` line, which is shared, so it goes through the Project Manager). Pruned after 7 days |
| `playtest/findings/F-NNNN/` | finding.md, before.jpg, after.jpg | yes |
| `playtest/BACKLOG.md`, `playtest/digests/`, `playtest/coverage.json` | The ranked list, the digests, where it has been | yes |

**Screenshots** are JPEG, 960 px wide (about 100-150 KB). The root
`.gitattributes` already sends `*.jpg` and `*.png` to LFS, so they go to LFS
with no change. At a few findings a day that's small.

**GitHub issues later:** one issue per finding, with labels for category and
priority. The triage task opens it with `gh issue create`, puts the
screenshot in by linking the LFS file on the branch, and closes the issue when
the digest marks the finding fixed. This waits for `gh auth login`.

## 8. Phases

1. **Spikes (half a day, one Editor slot and one player build).** The
   `-playtest` flag says hello, walks a few metres, screenshots from a
   RenderTexture, and quits. Then test it minimised, off-screen, unfocused
   and muted. Measure an incremental build in a worktree.
2. **The bot.** The goal library, steering, the seeded RNG, the trace,
   `captureFramerate`, the code detectors and run.json. Its checks run as
   EditMode tests where they can (the stuck and NaN rules, signatures).
3. **The runner.** run.ps1 with the preflight, the lock and replays;
   install-schedule.ps1; the build job.
4. **Triage.** The Claude scheduled task, the findings format, BACKLOG.md and
   the digest.
5. **GitHub issues**, once `gh` is signed in.

## Phase 1 results (2026-10-27)

The spike (`Scripts/Playtest/PlaytestBot.cs`, `tools/playtest/spike.ps1`,
`spike-mute.ps1`) ran a Windows player build with `-playtest` for 12 game
seconds in four ways: a normal window, launched minimised, moved off-screen by
the bot, and off-screen with the listener muted. On the RTX 4080 SUPER:

- **Rendering out of the way works.** All four rendered every frame (360
  frames for 12 game seconds at 30 fps, in 12.3 s real time) and walked the
  wizard 27 m. Both kinds of screenshot (camera to RenderTexture, and the
  screen) came out right, with the same brightness in every case, including
  minimised and off-screen.
- **No focus stealing.** The player was never the foreground window in any of
  the 178 half-second samples (Windows won't hand focus to a process a script
  starts in the background), though `Application.isFocused` reads true. So the
  fallback to overnight and idle times isn't needed: runs can go any time the
  preflight passes.
- **Sound: mute in Windows, not in the game.** `AudioListener.volume = 0`
  silences the bot's own audio tap too (peak and RMS read 0), because the tap
  hears the mix after the volume. Muting the player's session in the Windows
  volume mixer from the runner (`tools/playtest/AppMute.cs`, 0.8 s after
  launch, before any sound) leaves the tap hearing everything (peak 0.036, the
  same as unmuted) while nothing reaches the speakers.
- **Builds.** The first build after the perf merge took 27 min (the shader
  compile). Incremental after a script change took 16 s. The bot's builds
  must come from a freshly built yard: this worktree's stale yard scene went
  into the spike build (the wizard has no look), which a dedicated build
  checkout avoids, since the yard rebuilds when missing.
- **No errors** in any of the four Player.logs, and no crash dump on quit.
  The D3D12Core fault on quit is a known issue (being fixed by Gameplay); the
  crash detector records it against that known issue rather than filing it.

## Phase 2, first runs (2026-10-27)

The bot plays: `tools/playtest/run.ps1 -Seed <n> -Seconds <n>` launched a
build off-screen and muted. In 2 minutes of game time it did 6 to 20 goals
(walks, casts, combos, throws, sits) and wrote run.json with its trace,
candidates and screenshots. What the first runs showed:

- **Replays aren't exact.** The same seed twice on the same build matched
  for the first 7 steps, then drifted during a long walk: one run was
  blocked and jumped where the other got through. Game time is locked, but
  loading and physics use real time in places, so props and terrain arrive
  on different frames. So "reproduced" now also means the same signature
  met during the same goal, not only within a second of the same frame,
  and triage replays up to 3 times. Making long walks replay exactly is
  follow-up work. Suspects, to check in order: anything in loading or
  streaming timed by `Time.realtimeSinceStartup`, a Stopwatch or a per-frame
  time budget (props and terrain spawning on whichever frame the budget
  allows); physics or animation on unscaled time (`Time.unscaledDeltaTime`,
  `AnimatorUpdateMode.UnscaledTime`, Cinemachine set to ignore time scale);
  async loads and texture streaming finishing on different frames; and
  `Time.fixedDeltaTime` steps not dividing 1/30 s evenly, which only matters
  if one of the above shifts a frame.
- **A real paper cut, probably.** After the bot cast Hasten Time, frames
  took 0.8 to 2.2 s while it ran: 20 chemistry ticks a frame over the
  active chunks. Spikes during Hasten Time are now keyed "(hasten time)" so
  they group as one finding.
- **False positives fixed:** black pictures at night (Hasten Time had run
  the clock into the evening; night is now expected to be dark); "airborne"
  while swimming (swimming now counts as not airborne); the waterfall and
  the sea located by what they draw, not their objects' origins (both had
  sent the bot to the ball court).
- Low-confidence candidates seen: stuck (twice, mid-meadow) and "Plant
  Growth changed nothing". These are what the Claude triage pass is for.

## Phase 3, the runner (2026-10-27)

- `tools/playtest/run.ps1` runs one playtest:
  - Preflight: it skips (logged in `playtest/runs/skipped.log`) when under
    8 GB of memory is free, a Unity batch job or MPPM player is running, or
    another playtest holds the lock.
  - It runs the playtest-build checkout's player (or this checkout's),
    off-screen and muted in the mixer, with a heartbeat watch.
  - A crash or hang becomes a candidate. Crashes come from Windows' error
    events matched by process id, so another session's test player isn't
    counted, and from Unity's crash folder.
  - It updates `playtest/coverage.json` (counts fade by a fifth a run).
  - It never passes `-clean-quit`, which brings the old quit crash back on
    purpose.
- `playtest/known-issues.json` lists issues already decided. An issue is
  known only on builds without its fix: run.ps1 asks git whether the build's
  commit (StreamingAssets/build.json) contains `fixedIn`. On a build with it,
  the crash is a regression and is filed. The D3D12 quit fault is fixed in
  4a61ee3. `Playtest.KnownIssues` applies the same rule in C#.
- `tools/playtest/build.ps1` keeps `.claude/worktrees/playtest-build`
  detached at origin/dev:
  - it builds only when origin/dev has moved and the preflight passes
    (10 GB);
  - it copies the Asset Store packs in from the main checkout;
  - it runs Unity in batch mode on `PlaytestBuild.Build`, which builds the
    yard (in that checkout only, never committed) and then the Windows
    player.
- Not set up yet, on purpose: the Task Scheduler entries and the Claude
  triage task. Andrew sees the first runs first.

## Needs a decision from Andrew

1. **Where builds come from:** a dedicated playtest-build worktree with its
   own `Library/` (10-15 GB of disk; builds every 4-6 hours when dev changed
   and the slot is free), or the main checkout's `Builds/Windows/` as and
   when someone builds?
2. **When it may run:** every 45 minutes around the clock, only when the PC
   is idle, or set hours? Is a window briefly appearing acceptable, if the
   minimised-launch spike fails?
3. **Replayable runs:** lock the frame rate during runs (exact replays, and
   performance measured per frame), as recommended?
4. **Cost:** a Claude triage pass every 2-3 hours and a digest every 2 days.
   Or triage less often?
5. **Goals for v1:** the list in section 2. Anything to add or drop? Online
   play (desyncs) waits for v2.

Needs other lanes, through the Project Manager: a `.gitignore` line for
`playtest/runs/` (shared); a build method or flag in `PrototypeBuild.cs` if
the Build lane wants one (Build); and a spare Editor or build slot for the
phase 1 spikes.
