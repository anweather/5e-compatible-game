# State (2026-10-31)

The one page a new PM session reads first. Keep it short: history goes in
`docs/coordination-log/<yyyy-mm>.md`, the rules for agents in `COORDINATION.md`.

## Who runs where

| Where | Account | What it's for |
|---|---|---|
| **PM** (this role; the `coordination` worktree, pushes to dev) | Max | Plans, briefs, merges (`tools/workers/land.sh`), GitHub issues, releases, reports. Writes no game code unless asked |
| **DesktopUnity** (the PC, interactive) | Max | The only Windows GPU: host passes (looks, feel, sound), player builds, release candidates. Long passes run as fresh sub-agents per part |
| **unity-game-macbook** (Remote Control) | Max | Compute with Unity and a GPU (EditMode, probes, profiling). No Mac builds (Andrew). Its route doesn't confirm reading; check its branch if quiet |
| **Windows VMs** w1, w2 (Pro), w3, w4 (Max) | | Rules plus Unity batch compile. 4 GB start, grow to 8 |
| **Linux VMs** u1 (Pro), u2 (Max) | | Core and sandbox only, about 10x cheaper for rules work. 1 GB start |

- **Workers take briefs straight from the mailbox:** `start.cmd <vm> <brief.md>` boots and briefs;
  `brief.cmd <vm> <file> [-FollowUp <id>]` briefs a running VM. `runner.mjs` in the VM runs it headless,
  and `bridge-watch.sh` brings the report to the PM's mailbox (`bridge.sh wait pm` as a background
  task). Briefs live in `docs/handoffs/briefs/`. Watch workers with `tools/workers/peek.sh`.
- The Pro coordinator session is closed (2026-10-30); nothing relays.
- **Memory (31 GB):** a Windows VM needs 10 GB left beside an open Editor, a Linux one 5 GB, and with
  no Editor, 6 GB. The watcher stops the newest VM below 1.5 GB free. An online probe takes 3-4 GB, and
  a player build pushes the Editor to about 14 GB: no VMs during release candidates.
- **Usage:** the watcher reads limits from VM reports too; a brief hit by a limit resumes by itself.

## Standing rules (Andrew)

- Focus: material interactions, attributes and effects, spell effects, mini games. Look and feel is
  a side quest. Every spell goes through the material verbs (`rules/materials.json`), not a rule for
  one spell; no new element for one spell. GPU-first effects. Lean budget.
- Pro first, Max when Pro is out. No idle compute.
- Releases are alpha: if the release candidate is green, the PM publishes without asking. Notes are
  player-facing, with the Install section copied (`docs/handoffs/release-<v>-notes.md`).
- GitHub issues are the PM's: file, comment, label, retitle, and close when the fix ships.
  Discovery cost on each (`Discovery-Cost:` line); a rediscovery is a comment, never a duplicate.
- Merged branches are deleted. Deleting unmerged branches, force-pushing and changing the plan are
  Andrew's.
- Screenshot budget (`.claude/skills/screenshot-budget`): sessions check their own shots at 480 px;
  the PM opens an 800 px final only for something it vouches for, or sends it to Andrew unopened.
- Ask Andrew one question at a time. Never type a sign-in, password or token for him.
- Caves are deferred permanently. Restart the PM session after every release, with a handoff.

## Releases

- **0.1.9** published 2026-10-31 (Windows only, pre-release); main merged back, freeze lifted.
  DesktopUnity runs a fresh Editor, serial gates, the Windows build into the draft, install from the
  draft, then the update from 0.1.8. Notes: `docs/handoffs/release-0.1.9-notes.md`. Publish if green;
  then merge main back into dev and lift the freeze.
- **0.1.10:** the rest of the sound pass (`docs/handoffs/sound-pass-brief.md`, minus the casting sounds
  already in) and proximity voice (Vivox: Andrew sets up a Unity Gaming Services project first).

## In flight

- Landed on dev after 0.1.9: the Gauntlet's rules (22cda15), the Dragon's rules (0967b44), surf toward
  the nearest land (1671b55, #23; rebuild the yard after pulling).
- DesktopUnity: #5 fixed on Windows (3cf7624; open until seen on a live desktop). Windows perf
  comparison done (`docs/perf/2026-10-31-win.md`). Now: the uncharged slam, a punch clip (#18), and 2-3
  cast-sound prototypes for Andrew to pick from before anything is built in.
- Mac: the Dragon's view is pushed (`dragon-view`, efb186a), not merged: it waits on `probe.sh online`
  in a normal Editor (DesktopUnity, queued). Every machine rebuilds the yard after it lands. For Andrew,
  with media first: the model (reads friendly, not ancient), water never shelters (no cell reaches
  depth 8), a raised Wall of Stone is wiped by the chunk reset before the breath arrives.
- Rules MCP phase 1 landed (dffbc19; u1, now asleep): `core/Rules.Mcp`, `ModValidator.cs`, budgets in
  `rules/budgets.json`, "Using the rules MCP" in MODDING.md. Unity hasn't compiled ModValidator yet
  (DesktopUnity checks on its next pull). Phase 2 (procedural looks) starts from
  `docs/handoffs/procedural-looks-scan.md` (~70 id-keyed places); it needs Unity.

## Next, in order

1. Wave crest judged good for now (#11 closed). Cast feel judged (J-040, #22): DesktopUnity
   tunes the uncharged slam (slower, more oomph)
   and redoes the cast sounds (lower, layered per element, rising per charge step) after perf, with a new clip.
2. Done: the Windows perf comparison (`docs/handoffs/perf-pass-brief.md` step 1). Check the perf scenarios still
   run (1e8d899 split PerfScenario.Start and PerfStage.Walk). Then the `perf-pass` skill from
   `docs/perf/notes-2026-10-31-mac.md`.
3. The Gauntlet's view: side camera, booths, carnival dressing (`docs/handoffs/obstacle-course-brief.md`).
4. The Dragon's view: model (CC0), flight, breath, sky, knock-down, props on ChunkFreshened.
5. Draw the fog patches (#34). Perf issues #24-#33 (storm at 20 fps, uncapped tick debt first).
6. 0.1.10: the sound pass and voice.
- Backlog and priority: `docs/BACKLOG.md` ("Priority (groomed 2026-10-30 with Andrew)", items 24-26).

## Andrew's board

https://claude.ai/artifact/KhW6DyMwmA1ncXCxLxbBgY: what needs his verdict, what landed, who's on what.
He reads it on his phone instead of scrolling the chat. Update it (Artifact `read`, then publish with
`url`) whenever a verdict, a landing or an assignment changes.

## Open questions for Andrew

- Keep writing every rule twice (C# core and TS sandbox), or freeze the sandbox?
- The punch knocks a crate only 0.42 m: harder?
- Unmerged branches to delete: `wip/w1-*`, `wip/w2-*` (their work landed), and about 17 old spikes.

## Telemetry

- Every worker commit carries cost lines; `docs/telemetry/` holds the reports (latest 2026-10-31:
  building 36.1M ite, the PM session 26.5M, host passes 69% of building cost).
- Skills: `agent-cost-telemetry`, `agent-cost-report` (in `~/.claude/skills`), `screenshot-budget`.
- Known gaps: the Mac has no commit hook; merge lines double-count the host's work; Pro transcripts
  aren't on disk.
