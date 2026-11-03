# State (2026-11-02, evening)

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

- **Paused (Andrew, 2026-11-02):** no new work until he has play tested tonight; in-flight work may finish. Then he decides whether 0.1.11 goes before the cleanup. Draft notes: `docs/handoffs/release-0.1.11-notes.md`; focus list on the board.
- **Mac and DesktopUnity:** idle for the pause. `roomruin-wait` landed (1ada360): #65, #50 and #67 closed.
- **Unmerged, kept:** `second-map` (afff4c9): the report and the word are on dev (`docs/design/second-map-2026-11-02.md`), the code waits for shipped maps (#69, backlog 29), after the cleanup.
- **#59 telemetry:** items 1 to 4 landed (b5e3465), checked against the real stream. "In the world" is stamped on frames, so a slow rebuild could stamp early. Item 5 (faster) not started: run `chatbench` and read the first real record.
- **#58 voice:** the freeze is fixed and landed (d5de6de). Open: real words twice in a row, the real stuck case, `-voice-no-meter`; all need Andrew talking. Separate from chat tuning now (J-064). Lead: the Scarlett is 44.1 kHz, 32-bit, the other mics 48 kHz, 16-bit.
- **u1, u2:** stopped. Pro is back (2026-11-02 20:02); nothing queued while work is paused.
- **Note:** `YardAutoBuild` skips batch mode, so a hidden Editor needs Build Chemistry Yard run by hand.
- **Landed tonight:** the whole creature port (`creature-data`, `punchy-people`, `life-variety`, `punchy-water`; Punchy's assets deleted), #57 building light. Merged dev passed full EditMode on the Mac and Windows.
- **Test player:** `.claude/worktrees/host-ux/unity/Builds/PlayTest-f736c4d/SpellChemistryValley.exe`, dev at f736c4d, for Andrew's play on 2026-11-02 after 8pm. Copies passed the squirrel test, save right after apply, 2 real Haiku prompts, two built peers over Relay on this PC. The exe itself has never been launched, or anything run in a real window. Older `PlayTest/` and `PlayTest-1ad55fb/` stay until he has moved over.

## Next, in order

1. Andrew plays the test player: #57 in a real window, voice words, the first real prompt, the creatures.
2. Code base cleanup (Andrew, 2026-11-02), before new features: the ranked list is `docs/design/cleanup-review-2026-11-02.md`, on the board. Branches next, from the top, after his verdict.
3. Temporary materials and products as data (`docs/design/primitives.md`), on u2.
4. Gaits by rule (`docs/design/builders.md`).
5. Design things on water (#60); size patches for Claude (#61).
6. 0.1.11, when Andrew says: he put it on hold. Before it ships: the notes tell anyone with saved mods to copy them out first (#63), version bump. J-060 comes back before anything commercial.
- Left out of the port, for later: flight and biomes for spawn rows, `glides`, a shore rule for reeds, mod budgets on rows, water animals as shapeshift targets. Dev's main thread may have risen about 1 ms on the Mac (unexplained; not fauna).
- Backlog and priority: `docs/BACKLOG.md`. Designs: `docs/design/world-tools.md`, `primitives.md`, `builders.md`, `chat-telemetry.md`. First play: `docs/playtests/2026-11-01-mcp.md`.

## Andrew's board

https://claude.ai/artifact/KhW6DyMwmA1ncXCxLxbBgY: what needs his verdict, what landed, who's on what.
He reads it on his phone instead of scrolling the chat. Update it with `tools/workers/board.mjs`, then publish
`.pm/board.html` with `url`, whenever a verdict, a landing or an assignment changes.

## Open questions for Andrew

- The real test of the PlayTest player: real Claude answering in chat, words by voice, a house built by asking, and how the radial, spell list and Esc menu feel at 4K.
- The sound pass and the cast sounds: he listens on the board when he has time (J-065).

## Telemetry

- PM cost: up to 69% of one session's context re-reads were mechanical (2026-11-02). Landing and the board are scripted (`land.sh auto`, `board.mjs`); measure the next batch before trying a cheaper main PM.

- Every worker commit carries cost lines; `docs/telemetry/` holds the reports (latest 2026-11-02:
  coordination 55% of spend, cache reads 66% of feature cost).
- Skills: `agent-cost-telemetry`, `agent-cost-report` (in `~/.claude/skills`), `screenshot-budget`.
- Known gaps: the Mac has no commit hook; merge lines double-count the host's work; Pro transcripts
  aren't on disk.
