# Judgment calls

(Not to be confused with `docs/DECISIONS.md`, the architecture decisions D1, D2, ...)

Every decision the coordinator asked Andrew for. **Before you ask Andrew
anything, look here first.** If a past decision covers the question, follow
it, and add 1 to its *Revisited* count when the question came up again.
Ask again only if something has changed, and say what.

Agents: send judgment calls to the coordinator ("Project Manager"), not to
Andrew directly. The coordinator checks this log, then asks Andrew if needed,
and records the answer here.

Each entry has:
- **Asked:** the question, when, and who raised it.
- **Also comes up when:** other sessions or situations likely to hit the same question.
- **Answer:** what Andrew decided.
- **Why:** Andrew's reason if he gave one. Otherwise, the coordinator's reason
  for its recommendation that Andrew accepted, marked "(recommendation)".
- **Revisited:** how many times the question has come up again after the
  answer, with dates.

## Open

### J-021: Finer ground, five design questions (backlog #15, Later)

- **Asked:** 2026-10-27, raised by World's spike (`docs/FINER_GROUND.md` on
  `finer-spike`, never merged). It recommends option 2: finer height only
  (four 0.75 m sub-heights per cell), with water reading the mean.
- **Also comes up when:** earth shaping, trenches, the save format, cave
  shafts.
- **The questions:**
  1. Which shapes go finer? Proposed: slanted lines and round craters only.
  2. Is a half-raised cell a wall at its highest sub-square, or at its mean?
  3. Is water moving a cell at a time acceptable along a smooth channel?
  4. Should it land before saves ship?
  5. Does a shaft open when all four sub-squares reach the bottom, or when
     their mean does?
- **Answer:** pending; ask when it comes up in the order.
- **Revisited:** 0.

### J-020: Caves, the six design questions (backlog #14, Later)

- **Asked:** 2026-10-27, raised by World's spike (`docs/CAVES.md` on
  `caves-spike`, never merged). It recommends C: levels linked by shafts.
- **Also comes up when:** anything underground, digging, shafts, light in
  caves, saves (SaveFormat 2).
- **The questions,** with World's proposals:
  1. How deep, and how many levels? One, about 6 m down.
  2. Can players dig new caves anywhere, or only reach caves that exist?
  3. Fixed levels with stairs, ramps and shafts, or continuous slopes?
  4. What crosses a shaft? Water down, gas and smoke up, wizards falling
     (SRD damage past 10 ft) or climbing; no spells.
  5. Are caves always dark? Yes.
  6. Solo first, or online from the start?
- **Answer:** pending. Ask Andrew (with the question tool) when caves comes
  up in the priority order. Not urgent.
- **Revisited:** 0.

## Decided

### J-025: Where are release-candidate builds made?

- **Asked:** 2026-10-27, by the coordinator, when the Mac spent 40+ min
  compiling Windows shader variants from scratch for the 0.1.5 RC, while
  the PC had them warm in its cache from PC-5.
- **Also comes up when:** every release, a shader or graphics settings
  change, the time to ship.
- **Answer (2026-10-27):** let 0.1.5's Mac build finish. From 0.1.6 on,
  **the PC builds the Windows half** (warm cache) and the Mac builds macOS,
  in parallel, both from the same main commit. Build updates RELEASING.md
  and ship-update to match.
- **Why:** Andrew picked the recommendation. Don't throw away nearly-done
  work. Each platform builds fastest on its own machine.
- **Revisited:** 0.

### J-024: Should rain make the ground look wet?

- **Asked:** 2026-10-27, by the coordinator. The rain WaterMesh fix stopped
  drawing rain's 1-unit film on open ground.
- **Also comes up when:** any weather visuals, terrain shaders, snow.
- **Answer (2026-10-27):** yes, a terrain wetness shader: darker and glossier
  while it rains, drying after. A view-only change (Gameplay), for 0.1.6.
- **Why:** Andrew picked it. It brings back the "wet" read without the
  chemistry or mesh cost.
- **Revisited:** 0.

### J-023: Skip the Editor's domain reload on entering Play mode?

- **Asked:** 2026-10-27, by the coordinator, after static-resets
  (`46abc35`) made the code safe for it.
- **Also comes up when:** Editor start-up time, new static fields (they
  must reset in SubsystemRegistration), Editor-only bugs on a second Play.
- **Answer (2026-10-27):** try it in 0.1.6. Build flips Enter Play Mode
  Options on a branch, measures Play-mode start (about 11 s now), and runs
  the probes twice in a row. It's Editor-only, so players aren't affected.
- **Why:** Andrew picked the recommendation. It saves most of the ~11 s on
  every Play, for every agent, and reverts easily.
- **Revisited:** 0.

### J-022: Does Zachary Franklin's permission cover modifying all of Punchy?

- **Asked:** 2026-10-27, by the coordinator, after Gameplay changed
  `ThirdParty/Punchy/Sky.hlsl` (Punchy has no LICENSE file).
- **Also comes up when:** any edit to or reuse of files in
  `ThirdParty/Punchy/`, the credits, and releases.
- **Answer (2026-10-27):** yes. "Permission applies. We are writing
  together on this."
- **Why:** Andrew and Zachary Franklin are writing it together. Keep the
  attribution in Credits.cs, and record each change in the file's header
  and the Punchy README.
- **Revisited:** 0.

### J-019: When do we cut releases?

- **Asked:** 2026-10-27, by Andrew: "when do we want to cut a new release?
  How many features are enough... if we wait too long we're play testing
  really old versions".
- **Also comes up when:** a big fix lands, the bot finds a blocker,
  someone asks "is X out yet?", any version bump.
- **Answer (2026-10-27):** **cut 0.1.5 today,** once the online reset
  (net-reset and net-reset-view), cheap SSAO (perf-pass) and the playtest
  bot have merged. After that, **a release about every 2 days,** or sooner
  after a big fix.
  - Each release: a merge freeze on dev, then dev → main, then the version
    bump, then the release-candidate build, then the bot's runs and a
    quick human look, then fix any blockers, then ship (docs/RELEASING.md).
  - The Build lane runs the release, and the coordinator calls the freeze.
  - Andrew (later the same day): "2 days is probably fine as a starting
    point", but we may release more often while so much is landing. So
    treat 2 days as the longest gap, not a fixed schedule: cut sooner when
    a solid batch is verified.
- **Why:** Andrew picked the recommendation. About 20 merges had landed
  since 0.1.4 that morning. Short releases keep playtests on what players
  actually have, and bugs found on a release candidate get checked against
  dev before filing.
- **Revisited:** 0.

### J-018: Which build does the playtest bot run against?

- **Asked:** 2026-10-27, by Andrew: don't rebuild for every playtest; use
  the last known build.
- **Also comes up when:** scheduling the bot, any playtest-build rebuild,
  filing a bug found by the bot.
- **Answer (2026-10-27):** **the current release candidate,** built once
  per release with the bot inside (behind `-playtest`). There's no rebuild
  per run and no rebuild on every dev change.
- **Why:** Andrew picked the recommendation. 0.1.4 predates the bot, so it
  can't run it; the bot has to ship inside a release build. That way it
  plays exactly what players get.
  - Findings are checked against dev (a replay on a dev build, or reading
    the fix log) before filing, so nobody files a bug dev has already fixed.
- **Revisited:** 0.

### J-017: How do we relieve the Editor-testing bottleneck?

- **Asked:** 2026-10-27, by the coordinator. Code was written much faster
  than one PC Editor could test it: 8+ branches queued, agents idle waiting.
- **Also comes up when:** any queue for the PC Editor, memory warnings,
  scheduling perf runs, MPPM tests.
- **Answer (2026-10-27):**
  - The Mac is a second test rig: it verifies other lanes' branches
    (compile, EditMode, probes; not Windows-only or PC perf numbers) and
    never edits them.
  - Batch-mode tests may run in parallel, when 10 GB or more is free.
  - Two Editors at once on the PC are allowed. Andrew: "We ran two editors
    overnight. Why can't we do it again?"
  - Limits:
    - start only with 10 GB or more free;
    - MPPM only with ONE Editor open (two Editors plus MPPM hit 52 of
      53 GB);
    - no perf measurements and no Windows builds beside another Editor's
      MPPM.
- **Why:** the earlier one-Editor rule came from the 52/53 GB incident,
  which involved MPPM. Two Editors without MPPM fit (11 GB free with one
  open). Perf numbers need a quiet machine.
- **Revisited:** 0.

### J-015: SSAO, off, cheap or as it is? (perf pass)

- **Asked:** 2026-10-27, raised by Build (the B1 sweep).
- **Also comes up when:** any visual-quality-versus-speed setting (shadows,
  HDR, post-processing), and quality presets later.
- **The evidence** (Mac player at 1080p, mean / p95 ms):
  - base 42.0 / 70.4;
  - off 32.7 / 61.0 (-22%);
  - cheap (depth-only, half resolution, low samples) 35.4 / 62.4 (-16%).
  - Off loses the contact darkening under the eaves, at the ground and
    between bushes. Cheap looks nearly the same as base.
  - Screenshots on the Mac:
    `unity/Builds/demo-videos/ssao-compare-*.png`.
- **Answer (2026-10-27):** **cheap SSAO.** Then measure soft shadows at
  Medium on top of it, and measure the result on the PC (PC-5).
- **Why:** Andrew picked the recommendation. It gets most of the gain
  (-16% mean, -11% p95 on the Mac) without changing the look, and weaker
  GPUs benefit most.
- **Revisited:** 0.

### J-016: May agents force-push their own unmerged branches after a rebase?

- **Asked:** 2026-10-27, by the coordinator, after World force-pushed its
  four rebased branches. Andrew had asked to be consulted before
  force-pushes.
- **Also comes up when:** every rebase of a pushed feature branch, by any
  agent.
- **Answer (2026-10-27):** **allowed, with limits.** Only an agent's own
  unmerged feature branch, after a rebase, with `--force-with-lease`. Never
  `dev`, `main` or another agent's branch; any other force-push needs
  Andrew.
- **Why:** Andrew picked the recommendation. Rebasing your own pushed
  branch is routine, and the lease stops it overwriting work.
- **Revisited:** 0.


### J-013: How may the playtest scripts run past the PowerShell execution policy?

- **Asked:** 2026-10-27, raised by World.
  - This PC's policy blocks .ps1 scripts. The coordinator's own attempt to
    pass `-ExecutionPolicy Bypass` was refused by its safety check.
  - World had been running its spike and run scripts with
    `-ExecutionPolicy Bypass`, which applies to one process and changes no
    setting. It has stopped until Andrew decides.
  - The options:
    (a) Task Scheduler passes `-ExecutionPolicy Bypass`;
    (b) set CurrentUser to RemoteSigned (a security setting);
    (c) sign the scripts;
    (d) port `run.ps1` and `build.ps1` to a small .NET console tool (no
    policy involved).
- **Also comes up when:** any script on this PC: the coordinator's tools,
  `tools/playtest`, release scripts, scheduled tasks.
- **Answer (2026-10-27):** **(d)**, a .NET console tool
  (`tools/playtest/Playtest.Runner`, in `core/Game.Core.sln`) replaces
  `run.ps1` and `build.ps1`. No PowerShell scripts, no Bypass, no policy
  change.
- **Why:** Andrew: "Agree with .NET tool." No security setting changes. It
  shares the bot's rules and tests with the core. For any future script on
  this PC, prefer bash (Git Bash) or .NET over .ps1 files.
- **Revisited:** 0.

### J-014: How should resetting the world online behave (backlog #4)?

- **Asked:** 2026-10-27, raised by Gameplay's plan (World builds the reset
  command, Gameplay the view).
- **Also comes up when:** any host-versus-client authority question, mods
  across a world rebuild, any countdown UX.
- **Answer (2026-10-27):** all three as recommended:
  - a 3 s countdown;
  - the host decides (a client asks; no vote);
  - mods stay on through a reset (re-stamped in the reset batch).
- **Why:** Andrew: "yes that sounds great".
  - With at most 4 players, the host is in charge.
  - A reset restores the world, not the settings.
- **Revisited:** 0.


### J-012: The backlog's priority order

- **Asked:** 2026-10-27, by Andrew ("backlog grooming and prioritization").
  The coordinator groomed `docs/BACKLOG.md` into Now / Next / Later, with a
  lane for each item.
- **Also comes up when:**
  - any lane goes idle (take the next item in its lane);
  - the playtest bot's backlog grows (merge its paper cuts in every few days);
  - a new feature idea arrives (place it in the order).
- **Answer:** approved as proposed (see "Suggested order" in
  `docs/BACKLOG.md`).
  - Now: the quit-crash fix, the playtest bot, the performance pass (19),
    resetting the world online (12), terrain hardness (4).
  - Next: components in the rules (10.1), items and an inventory (5), what
    night does (1), world life (20), swimming then climbing (13, 14),
    streaming's remaining work (11).
  - Later: first person then weapons (7, 6; weapons need a fit decision),
    voice (8), caves (3, a representation spike first), finer ground (18),
    IK (9).
- **Why:** (recommendation)
  - Small, high-value work that unblocks others comes first.
  - The performance pass comes before the next playtest with friends.
  - Components come before more items and weapons, so new things combine with
    the rest for free.
  - Caves wait for hardness and a representation spike.
- **Revisited:** 0.

### J-010: How should we handle the Windows quit crash (D3D12)?

- **Asked:** 2026-10-27, raised by Gameplay. It's an access violation in
  `D3D12Core.dll` on every quit of a Windows build, after saving.
  - The options were: D3D11 first; end the process ourselves; leave it and
    look for a fix.
  - Andrew had the Build lane research it first. Its ranked recommendation:
    (a) test whether players see it;
    (b) drop in Agility 1.618.5;
    (c) end the process at quit;
    (d) graphics jobs off;
    (e) D3D11 first.
- **Also comes up when:** a release (Build), any crash report or event-log
  entry from a Windows player, the playtest bot's crash detector (it will
  flag every quit), graphics API or Unity version changes.
- **Answer:** pending; first, the PC tests to run.
  - Sub-answer (2026-10-27): **test A only, no download.** Test B, the
    Agility 1.618.5 swap from NuGet, isn't approved. Ask again before any
    fix that needs downloaded binaries.
  - Test A result (Gameplay, 2026-10-27): no dialog, and the window closes at
    once (Alt+F4 0.9 s). But the process lingers, hidden, for 17–39 s, then
    crashes in D3D12Core and writes a 42 MB dump to
    `%LOCALAPPDATA%\CrashDumps` on every quit. Windows keeps 10 dumps, about
    420 MB. Every session also shows as a crash in Reliability Monitor.
    Shutdown seems stuck for about 20 s before the fault. A relaunch during
    that time, and Velopack's restart, weren't tested.
- **Answer (2026-10-27):** **the game ends itself on quit** (option c), in
  the Windows player only.
  - Steps on quit: save, shut down Netcode (so peers see a disconnect, not a
    timeout), flush the log, then terminate the process.
  - Test a quick relaunch and Velopack's update-and-restart.
  - No Agility swap (no downloads).
  - Gameplay builds it.
- **Why:**
  - Andrew: "No download" (for the tests). Ending the game on quit "makes
    sense to me".
  - The crash is unseen, but it leaves a hidden process for 17–39 s and
    writes a 42 MB dump on every quit.
  - Ending the process costs no speed, and Velopack is safe with it.
  - The known costs: Unity's own cleanup is skipped (OnDestroy and
    OnDisable, Netcode unless shut down first, the last lines of
    Player.log), and it could hide a real crash on quit later. Anything that
    must happen on quit has to happen before the exit.
- **Revisited:** 0.

### J-011: A growing demo reel of features

- **Asked:** 2026-10-27, by Andrew. Coordinate short videos and screenshots
  of each feature in an artifact that keeps growing, if the artifact system
  supports that.
- **Also comes up when:**
  - any merge of a feature you can see;
  - playtest fixes (their before and after);
  - releases (a reel per alpha?);
  - anyone asking "where can I see X?".
- **Answer:** the existing "Valley Demo Reel" artifact
  (https://claude.ai/artifact/MeFmq29Uv2CE6ZggRKVMkd) became data-driven: a
  fixed page plus `reel.json`, both published beside the clips. A new
  feature only adds its files and appends to `reel.json`, and the agent that
  merges it supplies the clip.
- **Why:** (recommendation)
  - The reel is shared by public link. Published files work for signed-out
    viewers; the db and asset store need a sign-in.
  - Files left out of a publish are kept, so appending is cheap.
  - A version's limit (256 MB, 511 files) leaves room for about 50 more clips
    before a Volume 2.
- **Revisited:** 0.

### J-001: Lanes and file ownership

- **Asked:** 2026-10-27, by the coordinator when it started.
- **Also comes up when:** any new agent or session joins, or a new system
  doesn't fit a lane.
- **Answer:** approved as proposed: Gameplay, World and Build lanes, each
  file with one owner, shared files through the coordinator (see
  COORDINATION.md).
- **Why:** (recommendation) the lanes follow what each session was already
  doing and where they had collided (NetWizard.prefab, ProjectSettings).
- **Revisited:** 0.

### J-002: Rebase and merge `feat/spell-radial`?

- **Asked:** 2026-10-27, by the coordinator; the branch was 197 commits
  behind `dev`.
- **Also comes up when:** any old feature branch turns up unmerged.
- **Answer:** Andrew said rebase and merge. On checking, it was already on
  `dev` (rebased as `1300ff9` / `f8d1a9f`), so the branch was deleted
  instead (see J-004).
- **Why:** Andrew wants finished work in `dev`, not left on branches.
- **Lesson:** before asking about an "unmerged" branch, check
  `git cherry` / the patch content. It may already be on `dev`.
- **Revisited:** 0.

### J-003: Clean up the merged agent worktrees?

- **Asked:** 2026-10-27, by the coordinator: 11 worktrees under
  `.claude/worktrees/agent-*`, all merged and clean.
- **Also comes up when:** subagent or session worktrees pile up after
  merges.
- **Answer:** yes.
- **Why:** they were all merged and had no uncommitted work.
- **Revisited:** 0. J-004 now makes merged-branch cleanup standing.

### J-004: May merged branches be deleted without asking?

- **Asked:** 2026-10-27, by the coordinator (the 5 local and 13 remote
  branches merged into `dev`).
- **Also comes up when:** after every merge, by any agent.
- **Answer:** yes, as a standing rule. A branch fully merged into `dev` can
  be deleted, locally and on origin, without asking. Never `dev`, `main`, an
  unmerged branch, or a checked-out one.
- **Why:** Andrew: "if they are all merged to dev, we can remove them. Save
  as a rule."
- **Revisited:** 0.

### J-005: Start streaming slice 5 (props sleep with their chunk)?

- **Asked:** 2026-10-27, raised by World. It needed `ThingSpawner` /
  `PhysicsProp` from the Gameplay lane.
- **Also comes up when:** any feature that spans two lanes.
- **Answer:** yes, split in two: World builds the event (part A), then
  Gameplay builds the subscriber (part B), merged in that order.
- **Why:** (recommendation) the split keeps each file with its owner, and
  it worked: both parts merged on 2026-10-27.
- **Revisited:** 0.

### J-006: What next for the Build lane after the perf merge?

- **Asked:** 2026-10-27, by the coordinator.
- **Also comes up when:** any lane goes idle.
- **Answer:** research the Windows quit crash (see J-010).
- **Why:** Andrew's choice.
- **Revisited:** 0.

### J-007: What next for the World lane?

- **Asked:** 2026-10-27, by the coordinator.
- **Also comes up when:** any lane goes idle.
- **Answer:** build the playtest bot: nondeterministic, replayable, in the
  real player; it finds paper cuts, never fixes them.
- **Why:** Andrew wants a steady, prioritised list of paper cuts with
  repro steps and before/after screenshots, without a human playtesting.
- **Revisited:** 0.

### J-008: The playtest bot's five design questions

- **Asked:** 2026-10-27, raised by World in `playtest/DESIGN.md` (phase 0).
- **Also comes up when:** any background or scheduled job on the PC (build
  space, run times, Claude cost), and any replay or repro tooling.
- **Answer:** all five as recommended:
  1. A dedicated playtest-build worktree with its own Library.
  2. It runs whenever the preflight passes; if a window flashes up,
     overnight and idle times only.
  3. Lock the frame rate during runs.
  4. Triage every 3 h, only when there are new runs; a digest every 2 days.
  5. v1 is solo only; online and desyncs wait for v2.
- **Why:** (recommendation)
  1. Its own Library keeps its builds away from the other checkouts (there's
     about 1 TB free).
  2. The preflight already keeps it out of the way.
  3. It makes exact replays possible.
  4. Triage only on new runs keeps the cost down.
  5. Solo first keeps v1 small.
- **Result (2026-10-27, phase 1):** the fallback in answer 2 isn't needed. A
  minimised or off-screen player renders every frame and never takes focus,
  so runs can go any time the preflight passes. Sound is muted in the
  Windows mixer (`AppMute`), so the audio detectors still hear it. An
  incremental build takes 16 s.
- **Revisited:** 0.

### J-009: Keep a judgment-call log?

- **Asked:** 2026-10-27, by Andrew.
- **Answer:** yes, this file. Record each question, where else it could come
  up, the answer, why, and how often it was revisited. Check it at every
  decision point.
- **Why:** so the same question isn't asked twice, and past answers guide new
  ones.
- **Revisited:** 0.
