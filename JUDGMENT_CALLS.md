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
- **Why:** Andrew: "No download."
- **Revisited:** 0.

## Decided

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
- **Revisited:** 0.

### J-009: Keep a judgment-call log?

- **Asked:** 2026-10-27, by Andrew.
- **Answer:** yes, this file. Record each question, where else it could come
  up, the answer, why, and how often it was revisited. Check it at every
  decision point.
- **Why:** so the same question isn't asked twice, and past answers guide new
  ones.
- **Revisited:** 0.
