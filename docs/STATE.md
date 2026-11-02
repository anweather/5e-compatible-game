# State (2026-11-02)

The one page a new PM session reads first. Keep it short: history goes in
`docs/coordination-log/<yyyy-mm>.md`, the rules for agents in `COORDINATION.md`.

## Who runs where

| Where | Account | What it's for |
|---|---|---|
| **PM** (this role; the `coordination` worktree, pushes to dev) | Max | Plans, briefs, merges (`tools/workers/land.sh`), GitHub issues, releases, reports. Writes no game code unless asked |
| **DesktopUnity** (the PC, interactive) | Max | The only Windows GPU: host passes (looks, feel, sound), player builds, release candidates. Long passes run as fresh sub-agents per part |
| **unity-game-macbook** (Remote Control) | Max | Compute with Unity and a GPU (EditMode, probes, profiling); the headless Windows build as a fallback. Its route doesn't confirm reading; check its branch if quiet |
| **Windows VMs** w1 (Pro), w3 (Max); w2 and w4 are rescued and off, for Andrew to remove | | Rules plus Unity batch compile. Idle since Oct 30 |
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

- **u1** (Pro): `mods-add-things`, field-level patches and the squirrel test (`docs/handoffs/briefs/mods-add-things.md`). Resumed at 06:01 UTC on Nov 2.
- **DesktopUnity, u2 and the Mac:** stopped. Mac's handoff: `docs/handoffs/mac-2026-11-01.md`; PC's: `docs/handoffs/desktopunity-2026-11-01.md`.
- **Test player:** `.claude/worktrees/host-ux/unity/Builds/PlayTest/` from 1c97274. Don't let anyone clean that folder. It lacks only stucco.

## Next, in order

1. Unity side of u1's squirrel work (DesktopUnity), then the squirrel test in a real player.
2. `creature-data` on the Mac after Andrew clears it (`docs/handoffs/briefs/creature-data.md`), then Punchy's people, variety and water as rows.
3. Temporary materials and products as data (`docs/design/primitives.md`).
4. Gaits by rule (`docs/design/builders.md`).
5. 0.1.11, when Andrew says: he put it on hold. Before it ships: #53 (spoken words in the log), confirm the terms on running Claude Code (J-060), version bump.
- Backlog and priority: `docs/BACKLOG.md`. Designs: `docs/design/world-tools.md`, `primitives.md`, `builders.md`. First play: `docs/playtests/2026-11-01-mcp.md`.

## Andrew's board

https://claude.ai/artifact/KhW6DyMwmA1ncXCxLxbBgY: what needs his verdict, what landed, who's on what.
He reads it on his phone instead of scrolling the chat. Update it (Artifact `read`, then publish with
`url`) whenever a verdict, a landing or an assignment changes.

## Open questions for Andrew

- The real test of the PlayTest player: real Claude answering in chat, words by voice, a house built by asking, and how the radial, spell list and Esc menu feel at 4K.
- Cost: use a cheaper model for mechanical PM work? (`docs/handoffs/pm-2026-11-02.md`)
- Cast sounds and the new whoosh: never listened to.

## Telemetry

- Every worker commit carries cost lines; `docs/telemetry/` holds the reports (latest 2026-11-02:
  coordination 55% of spend, cache reads 66% of feature cost).
- Skills: `agent-cost-telemetry`, `agent-cost-report` (in `~/.claude/skills`), `screenshot-budget`.
- Known gaps: the Mac has no commit hook; merge lines double-count the host's work; Pro transcripts
  aren't on disk.
