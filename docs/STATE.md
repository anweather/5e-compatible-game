# State (2026-11-03, night)

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
- **0.1.11** published 2026-11-03: live rule changes, talking to Claude by voice (Whisper), bring your own Claude. **0.1.12:** proximity voice (Andrew, 2026-11-03), and the rest of the sound pass (`docs/handoffs/sound-pass-brief.md`).
- Builds: Windows builds on the PC (Andrew, 2026-10-31); the Mac's headless build
  (`PrototypeBuild.BuildWindowsFromCommandLine`, RELEASING.md) is the fallback. The GitHub Actions runner is
  shelved (`docs/CI_BUILDS.md`). The Mac app is shelved at 0.1.7 until Andrew brings it up (J-049): releases are Windows only.

## In flight

- **0.1.11 published** 2026-11-03 at b79f537 (Windows only): `main` merged back into dev. The update hop 0.1.106 → 0.1.107 passed; the Esc menu's "Update and restart" wasn't tried.
- **Single thread on the PC (Andrew, 2026-11-03):** frugal, one Unity job at a time, no clips to or from sessions. **DesktopUnityPro** (Andrew's session) drives the Editor in `host-ux`; the `DesktopUnity` session the PM started on `~/.claude-account3` stays idle, so two never share the Editor. Max's weekly limit resets Nov 5, 4pm Phoenix (#89 fixed: the watcher reads dated resets). The PM's account can't see the board.
- **Landed:** `glow` (P4), `dependents-all` (#88), `liquids` (P1 core, 1df0614c), `liquids-view` (88b1a277; every liquid shares water's calmness, opacity and ripple speed, and differs only in colour). Also `channels-3` (787b0bde; #83 reduced, not gone) and `liquids-2` (hot, freezes, sets; base rules' fingerprint changed; no view of magma or syrup yet).
- **Settings > Claude setup:** landed and worked with Andrew's real key in a release player (e0074b28 fixed sideways scrolling; one look after a relaunch). His key is still saved on the PC. Not tried for real: the cap stopping a run, Remove, a second Claude Code install. Cap default $1 a session; the model list stays Haiku and Opus (J-072).
- **Whisper landed** (e7480c2b): Andrew's voice reached Claude in a Windows release player (11 voice messages, open 6-56 ms, about 1 s to transcribe). The VC++ runtime and Vulkan loader ship with the game (J-072); proven on a clean-ish VM clone. Update from 0.1.10 is 142.7 MB. Credits' copyright lines checked against the LICENSE files (whisper.unity corrected to Macoron, in 058c5cb1; reaches players in 0.1.12). VC++ DLLs: Microsoft's pages allow unmodified retail copies from VCRedist for licensed Visual Studio users; Andrew has Community; its full licence terms aren't read yet. Linux and Steam Deck haven't run.
- **Vivox (0.1.12):** `vivox-spike` (8f44eae9, not landed): sign-in and libraries answered; the mic and fade aren't. Next: `briefs/vivox-pc-run.md` on the PC with Andrew. Vivox is on in the Dashboard. The Mac may need unlocking to be the second participant: its audio stalled with the screen locked.
- **Unmerged, kept:** `second-map` (afff4c9); `wip/u2-cover-spread` (superseded by dev's cover-spread, differs only by a refinement; Andrew's to delete).
- **`mcp-act` landed** (e8c1113b; `docs/design/world-acts.md`, backlog-archive 30, #127): `act` and `undo_act`, one-off happenings that aren't mods. Not tried with a real Claude yet.
- **`mix` landed** (d53feb01, P5; core 1411 passed): rows of one `mix` group that meet derive a material. Owed: seen in a running game (view unproven), EditMode on the merged tree, a late join over real netcode; `ApiKeyTests` fail 8 on the Mac (Windows-only vault, not confirmed on plain dev). Only `setCover` mixes covers.
- **`ball-in-cup` landed** (1e7fb5b5): `act` places a ball and cup, spells move it, turns, the `holed` trigger. Two-player turns tested in core only; no real-Claude test.
- **Note:** `YardAutoBuild` skips batch mode, so a hidden Editor needs Build Chemistry Yard run by hand.
- **Test player:** `.claude/worktrees/host-ux/unity/Builds/PlayTest-f736c4d/` (2026-11-02), before today's landings.

## Next, in order

1. Land what's in flight (the Vivox PC run).
2. **Cheap playtester landed** (`core/Playtest.Text`, `playtest/cheap/README.md`; text only, no view): Haiku $2.20 and Sonnet $3.68 for 20 minutes, one confirmed finding each (#90, #91), 9 of 10 false alarms from the tool. Fixed tool, Haiku: $2.78, 2 confirmed, 2 false alarms, 0 tool faults ($1.39 each; #91 comment, #92). `run.sh` is zsh, so the PC needs a port; the github sink has filed nothing real; a person judges before filing.
   **Maps as data with terrain through the MCP (Andrew, 2026-11-04, #69):** designed (`docs/design/maps-as-data.md`, nine phases, about five weeks); phase 1 landed (5638da4d; Windows with graphics checked, valley unchanged; `BuildWindowsFromCommandLine` untested on it, so watch the first release build); phase 2 landed (real two-peer run passed, no desync); questions answered (J-073). Phase 4 landed; start-up baseline on the PC 7.37 s on dev, 7.49 s with `ground/` (3 runs, not cold-disk, no real screen): phase 5 may add 2 s. Phase 3 (`maps-stamps`) waits on the PC's run. Named acts (`briefs/named-acts.md`) on the Mac, then phase 5, which must make the drawable range per map (`World.Land.cs`). Phase 8 must give a joiner's `RulesMcpServer` the host's map. Same-network Host and Join have no menu or flag: the PC needed a throwaway driver.
   Primitives: the "make a centaur" real-prompt test (#73). Strikes onto CommandChannel with #83. #87 flaky dragon budget test. Building on water (#60).
3. Gaits by rule (`builders.md`), after bodies (J-068).
- Cleanup: items 1, 2 (combos; defense and room in flight), 3, 5, 7 (items, entities, reactions; spells left), 8, 9 done; 4 dropped (J-069); 6 waits on #69.
- Backlog and priority: the board, https://github.com/users/anweather/projects/3 (epics, `area:` and `p1`-`p3` labels); `docs/BACKLOG.md` is the pointer and `docs/backlog-archive.md` keeps the design detail (more than one map is #69).

## Andrew's board

https://claude.ai/artifact/KhW6DyMwmA1ncXCxLxbBgY: what needs his verdict, what landed, who's on what.
He reads it on his phone instead of scrolling the chat. Update it with `tools/workers/board.mjs`, then publish
`.pm/board.html` with `url`, whenever a verdict, a landing or an assignment changes.

## Open questions for Andrew

- On the board: soot and black ice read as a pale blue film; darker and more solid? Creatures clip; walking-rules clip judged (no gesture, J-069).

## Telemetry

- PM cost: up to 69% of one session's context re-reads were mechanical (2026-11-02). Landing and the board are scripted (`land.sh auto`, `board.mjs`); measure the next batch before trying a cheaper main PM.

- Every worker commit carries cost lines; `docs/telemetry/` holds the reports (latest 2026-11-02:
  coordination 55% of spend, cache reads 66% of feature cost).
- Skills: `agent-cost-telemetry`, `agent-cost-report` (in `~/.claude/skills`), `screenshot-budget`.
- Known gaps: the Mac has no commit hook; merge lines double-count the host's work; Pro transcripts
  aren't on disk.
