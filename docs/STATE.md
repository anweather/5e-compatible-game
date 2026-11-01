# State (2026-10-31)

The one page a new PM session reads first. Keep it short: history goes in
`docs/coordination-log/<yyyy-mm>.md`, the rules for agents in `COORDINATION.md`.

## Who runs where

| Where | Account | What it's for |
|---|---|---|
| **PM** (this role; the `coordination` worktree, pushes to dev) | Max | Plans, briefs, merges (`tools/workers/land.sh`), GitHub issues, releases, reports. Writes no game code unless asked |
| **DesktopUnity** (the PC, interactive) | Max | The only Windows GPU: host passes (looks, feel, sound), player builds, release candidates. Long passes run as fresh sub-agents per part |
| **unity-game-macbook** (Remote Control) | Max | Compute with Unity and a GPU (EditMode, probes, profiling); the headless Windows build as a fallback. Its route doesn't confirm reading; check its branch if quiet |
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

- **0.1.10** published 2026-10-31 (Windows only, pre-release): the dragon, the IK punch and 5E unarmed strike,
  the cast sounds (B + pulse 30%), the lost-cast fix (#39), the play log (F8 marks), the Gauntlet's rules, surf
  toward land, the night stutter fix. Notes: `docs/handoffs/release-0.1.10-notes.md`. Install from the draft
  and update from 0.1.9 green on the PC; main merged back (dev = main = 155ac1c).
- **0.1.11:** live rule changes in the game (Claude writes and applies mods), proximity voice (Vivox: Andrew
  sets up Unity Gaming Services first), the rest of the sound pass (`docs/handoffs/sound-pass-brief.md`).
- Builds: Windows builds on the PC (Andrew, 2026-10-31); the Mac's headless build
  (`PrototypeBuild.BuildWindowsFromCommandLine`, RELEASING.md) is the fallback. The GitHub Actions runner is
  shelved (`docs/CI_BUILDS.md`). The Mac app stays on 0.1.7; the Mac reports what shipping it again takes.

## In flight

- **Landed:** `live-rules-unity` on dev at 3d4975f ( brief `docs/handoffs/briefs/live-rules-unity.md`): the room
  menu, RoomIntent, the M panel through the room, the game serving the rules MCP on 127.0.0.1:47777 with an off
  switch (J-047), Claude writing its own mods (J-048). Player-build check green. Now: merge dev, the play
  log's mods column from the room's titles, rerun room probes and online; then the PM lands it.
  Andrew's test build: `unity\Builds\live-rules-test\SpellChemistryValley.exe`, plus
  `claude mcp add --transport http spell-valley http://127.0.0.1:47777/mcp`.
- **unity-game-macbook:** `flaky-probes`: #42 (dragon), then #41 (sleet); #43 castfeel leftovers after
  (`hit_stop_is_short`, the varying check count). Then a few lines on shipping the Mac app again.

## Next, in order

1. A fireball scenario for the MCP's simulate (it starts with a Fire Bolt).
2. Perf: Andrew sees a lasting frame-rate drop in snow and rain (#29, backlog); storm and fog first
   (#27 uncapped tick debt, #31). The play log now gives Andrew's own numbers.
3. #40 (a yard's first pass leaves 37 materials white), procedural-looks leftovers (CellPaint first,
   `docs/handoffs/procedural-looks-scan.md`), the Gauntlet's view (`docs/handoffs/obstacle-course-brief.md`),
   fog patches (#34).
4. Dragon shelter questions for Andrew, with media first: water never shelters; a raised Wall of Stone is
   wiped before the breath arrives.
- Backlog and priority: `docs/BACKLOG.md` ("Priority (groomed 2026-10-30 with Andrew)").

## Andrew's board

https://claude.ai/artifact/KhW6DyMwmA1ncXCxLxbBgY: what needs his verdict, what landed, who's on what.
He reads it on his phone instead of scrolling the chat. Update it (Artifact `read`, then publish with
`url`) whenever a verdict, a landing or an assignment changes.

## Open questions for Andrew

- The cast sounds in the game (clip on the board): never listened to; shipped in 0.1.10 as picked.
- The live-rules clip ("Claude writes the rule"): his verdict, and his own test of the MCP.

## Telemetry

- Every worker commit carries cost lines; `docs/telemetry/` holds the reports (latest 2026-10-31:
  building 36.1M ite, the PM session 26.5M, host passes 69% of building cost).
- Skills: `agent-cost-telemetry`, `agent-cost-report` (in `~/.claude/skills`), `screenshot-budget`.
- Known gaps: the Mac has no commit hook; merge lines double-count the host's work; Pro transcripts
  aren't on disk.
