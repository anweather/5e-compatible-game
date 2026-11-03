# State (2026-11-03, afternoon)

The one page a new PM session reads first. Keep it short: history goes in
`docs/coordination-log/<yyyy-mm>.md`, the rules for agents in `COORDINATION.md`.

## Who runs where

| Where | Account | What it's for |
|---|---|---|
| **PM** (this role; the `coordination` worktree, pushes to dev) | Max | Plans, briefs, merges (`tools/workers/land.sh`), GitHub issues, releases, reports. Writes no game code unless asked |
| **DesktopUnity** (the PC, interactive) | Max | The only Windows GPU: host passes (looks, feel, sound), player builds, release candidates. Long passes run as fresh sub-agents per part |
| **unity-game-macbook** (Remote Control) | Max | Compute with Unity and a GPU (EditMode, probes, profiling); the headless Windows build as a fallback. Its route doesn't confirm reading; check its branch if quiet |
| **Windows VMs** w1 (Pro), w3 (Max); w2 and w4 are rescued and off, for Andrew to remove | | Rules plus Unity batch compile. Idle since Oct 30; w1 didn't fit beside the PC's Editor on 2026-11-02 |
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
- Caves are deferred permanently. Restart the PM after each big batch, with a handoff; clear DesktopUnity and the Mac the same way.
- The world is built from primitives as data (J-055, J-057, J-058): examples are tests of the tools, never features.
- Don't use the PC while Andrew's on it unless he says it's free. Never run a VM beside a perf run.

## Releases

- **0.1.10** published 2026-10-31 (Windows only, pre-release): the dragon, the IK punch and 5E unarmed strike,
  the cast sounds (B + pulse 30%), the lost-cast fix (#39), the play log (F8 marks), the Gauntlet's rules, surf
  toward land, the night stutter fix. Notes: `docs/handoffs/release-0.1.10-notes.md`. Install from the draft
  and update from 0.1.9 green on the PC; main merged back (dev = main = 155ac1c).
- **0.1.11:** live rule changes in the game (Claude writes and applies mods), proximity voice (Vivox: Andrew
  sets up Unity Gaming Services first), the rest of the sound pass (`docs/handoffs/sound-pass-brief.md`).
- Builds: Windows builds on the PC (Andrew, 2026-10-31); the Mac's headless build
  (`PrototypeBuild.BuildWindowsFromCommandLine`, RELEASING.md) is the fallback. The GitHub Actions runner is
  shelved (`docs/CI_BUILDS.md`). The Mac app is shelved at 0.1.7 until Andrew brings it up (J-049): releases are Windows only.

## In flight

- **0.1.11 waits** (Andrew, 2026-11-03: "wait on the creatures"; the creatures have landed). Nothing blocks it: the notes cut voice and carry the saved-mods warning (#63). Before cutting: version bump, add today's landings to `docs/handoffs/release-0.1.11-notes.md`, a green release candidate on the PC (no VMs then).
- **DesktopUnity:** `glow` (P4, `look.glow` on covers, clip of glowing moss), then compile `liquids` (ac5041e5) and build `liquids-view` (coloured water mesh, honey and paint clip).
- **u2 (Max):** stopped for memory mid `liquids-2` (wip branch); restart after the PC's filming. `dependents-all` (#88) waits on the PC's compile.
- **Mac:** `channels-3` (strikes onto CommandChannel with #83); channels-2 landed. `whisper-spike` scaffolding pushed (51e5bed0, not landed): the real Whisper step waits on Andrew clearing the downloads in the Mac's session (J-061 seam; no Windows compile yet).
- **Unmerged, kept:** `second-map` (afff4c9); `wip/u2-cover-spread` (superseded by dev's cover-spread, differs only by a refinement; Andrew's to delete).
- **Note:** `YardAutoBuild` skips batch mode, so a hidden Editor needs Build Chemistry Yard run by hand.
- **Test player:** `.claude/worktrees/host-ux/unity/Builds/PlayTest-f736c4d/` (2026-11-02), before today's landings.

## Next, in order

1. Land what's in flight (glow, liquids + view, #88, channels-2).
2. 0.1.11 when Andrew says.
3. Primitives: P5 mix (after liquids), the rest of P1 (`hot`, `freezes`, `sets`); `hot` and the "make a centaur" real-prompt test (#73). Strikes onto CommandChannel with #83. #87 flaky dragon budget test. Building on water (#60).
4. Gaits by rule (`builders.md`), after bodies (J-068).
- Cleanup: items 1, 2 (combos; defense and room in flight), 3, 5, 7 (items, entities, reactions; spells left), 8, 9 done; 4 dropped (J-069); 6 waits on #69.
- Backlog and priority: `docs/BACKLOG.md` (29: more than one map, #69).

## Andrew's board

https://claude.ai/artifact/KhW6DyMwmA1ncXCxLxbBgY: what needs his verdict, what landed, who's on what.
He reads it on his phone instead of scrolling the chat. Update it with `tools/workers/board.mjs`, then publish
`.pm/board.html` with `url`, whenever a verdict, a landing or an assignment changes.

## Open questions for Andrew

- Whisper: clear the downloads in the Mac's session (whisper.unity, native libs, a 75-150 MB model).
- On the board: soot and black ice read as a pale blue film; darker and more solid? Creatures clip; walking-rules clip judged (no gesture, J-069).
- Release 0.1.11 when he says ship.

## Telemetry

- PM cost: up to 69% of one session's context re-reads were mechanical (2026-11-02). Landing and the board are scripted (`land.sh auto`, `board.mjs`); measure the next batch before trying a cheaper main PM.

- Every worker commit carries cost lines; `docs/telemetry/` holds the reports (latest 2026-11-02:
  coordination 55% of spend, cache reads 66% of feature cost).
- Skills: `agent-cost-telemetry`, `agent-cost-report` (in `~/.claude/skills`), `screenshot-budget`.
- Known gaps: the Mac has no commit hook; merge lines double-count the host's work; Pro transcripts
  aren't on disk.
