# State (2026-11-07)

The one page the lead reads first (`unity-lead` skill). Keep it short: history goes in
`docs/coordination-log/<yyyy-mm>.md`, the rules for agents in `COORDINATION.md`.

## Now (2026-11-07, the lead's handoff)

- **No separate PM since 2026-11-07 (Andrew).** DesktopUnity is the lead (`unity-lead`), with Unity jobs going to
  `unity-task` subagents one at a time and long reads to `reader`. Before a clear: `lead-handoff`. `pm-session` is paused.
- **Landed today:** `bridge-outbox-repeats` (74105c32), `mcp-controls-text` (e77f8e3e), `stamp-steepness` (#156 core half,
  1cd07b70), `hold-offer-suits` (#165, efc7b6fe). `hold-ui` was already in (1858577c).
- **Watcher:** only one may run. On 2026-11-07 two old ones (from Nov 4 and Nov 6) were still alive and re-mailed u2's
  reports about 7,000 times; they're killed and pid 818996 remains. The single-start guard in `bridge-watch.cmd` doesn't
  work yet (not filed).
- **D16 unified casting (accepted, `docs/design/unified-casting.md`):**
  - **Core and view landed together** (3e62bee1, 2026-11-07): core 1719/1719, EditMode 1638/1640, two-peer `handlooks`
    6/6 checksums equal, clip `unity/Builds/demo-videos/unified-casting-hands.mp4`. Not Andrew-judged yet.
    Pre-existing, not from this branch: `HoldLockstepTests.TwoPeersWithDifferentPoints…` passes the 180 s Unity timeout
    (3.5-4 min); the `menu` probe's GUILayout "control 14" error (not yet confirmed on dev). Left: E is both right throw and
    pick-up (pick-up wins in reach); the merged icon is a generic two-tone disc, not a glyph per pair; core's `LoadElement`
    path still there for the bot and old probes. `origin/unified-casting-core` is fully in dev; deleting it needs Andrew's OK
    (the auto-mode classifier refused). The stale compile request in the `pc` mailbox can be dropped.
  - **`aimed-spells` landed** (a83b608b): lock-on and pitch (view only, `SpellCaster.LockOn.cs`); lines pass the
    caster's own wall, flyers hit over line blockers; Gust 4 cells tapped / 8 held, half then none, four directions
    only; Whirlwind kept (lifts 2 s, flings 6 cells). Core 1724/1724, EditMode 1644/1645, probe `aimed`, clip
    `aimed-spells.mp4`. Not Andrew-judged. Owed: Gust/Whirlwind numbers are core constants, not `rules/` (the rules
    MCP was down); probe `aimed` not in FAST_LOOP (World's doc).
- **`stamp-looks` landed** (5d6385f5, #147, #148): #147 is a plain tumble down a 40-56 degree flank, not the
  carry (carry 0.08 ms/frame with 10 crates); #148 fixed, stamped land is rock only where its face is 5 m or more
  (core `World.LandFace`, `LandCrag`; view only, no hash moves). The 15 m dome still reads as a crag (kept). Owed:
  `stamplooks` probe's new "carried vs still" check never run; the clip shows the small stamp and dome stills, not
  crates (the probe stalled under the recorder). Issues stay open until a release ships them.
- **`mac-checks` landed** (a1640984): the owed checks pass. stamplooks 16/16; crate clip
  `stamp-looks-crates.mp4` (a 4 m dome; ClipFilm, so the Recorder stall is not root-caused); new probes `stampclip`
  and `airpeers` (two peers, 0 of 9 checksums differ with Lightning, Gust, Whirlwind). `air-tuning-rules` (u2)
  landed too (cacf1b54): Gust and Whirlwind numbers are in `rules/` (`eye`, `lift`, `fall`, `shove`), no golden moved.
  `wip/u2-air-tuning-rules` (23f01af3) is not in dev (its WIP commit was redone); deleting it is Andrew's.
- **Assigned (Andrew, 2026-11-07 evening):** `casting-leftovers` landed (cc47375c: a glyph per merged spell, `PairGlyphs`;
  callers off `LoadElement`, the core Load path and `castfeel` stay since retiring them moves 6 goldens; keys untouched;
  `aimlooks` and `lookfx` probes fail on plain dev too, not filed yet). The Mac now does `hold-batch` (timed waves
  for a 10 min run, keep health, raiders strike a cracking crystal at the heart, attuning in the towers). The PC does
  `castle-cliff-canyon` (sheer cliff right of the road below the castle, a winding canyon beyond). **Controller first
  (Andrew, 2026-11-07):** the keyboard layout is on the back burner; design and judge controls on the pad. The
  lead's keyboard proposal, for later: one key per pad button, F interact, R use, V punch, 1-4 kit, T talk.
- **`hold-batch` landed** (584e7693, the Mac): a wave every 50 s (15 s warning, 10 waves, the last beaten wins);
  keep 240 (idle wizards lose in wave 6 at 4.7 min, fire+water wins in about 12 min); the keep is roofless and raiders
  strike a crystal at the heart that cracks in three stages; four corner towers attune fire, water, earth, air. Six
  HandsTests goldens moved on purpose. Not Andrew-judged; the clip is on the Mac (asked to send it to him).
- **`castle-cliff-canyon` landed** (65a80e90, the PC). Andrew: the canyon is great; the castle stays put (no wing), the
  land level from its door to the cliff edge, a training place outside. `pad.more` in buildings.json, `World.LayCrag`
  drops of 20-30 m east and north, `stable#1` with a paddock, a road option `flat` at the gate (moves the raiders'
  route start; Hold probes pass). Core 1739/1739, EditMode 1660/1660, castlecliff 18/18 with a flat-walk check. 455
  rocks, no measurable frame cost. Weak: the canyon-floor "looking up" shot, trees on the hill behind the cliff top.
  Clip sent to Andrew; not judged. `LookScopeTests.AtAllocatesNothing` flaked once in a landing (in `spell-balance`).
- **Assigned (Andrew: "give Mac and u2 more work"):** u2 `spell-balance` (fireball down, meteor kept big but costly,
  Dust Storm and "flaw" worth casting, the LookScope flake). The Mac `hold-bodies` (three more raider kinds from the
  animal bodies, villagers off the road during a Hold run).
- **`hold-bodies` landed** (42e25f49, the Mac). **`spell-balance`** (u2, 8ec64687 after the lead fixed a tuple assert
  Unity's NUnit can't pass) is not landed: EditMode 1665/1666 before the fix; hold/aimed/crystal probes failed or timed
  out while Andrew ran another game, and the dev comparison was stopped for his play. Rerun them on branch and dev.
  Release alpha 0.1.12 (Andrew asked, the go to merge dev into main) waits on it. Andrew's friend has the PlayTonight
  zip (`host-ux/unity/Builds/SpellChemistryValley-PlayTonight-c68b8df.zip`).
- **Playtest 2026-11-08** (`docs/playtests/2026-11-08-human-results.md`). Assigned: PC `peer-vanish` (B1 the other
  player vanishes, L1 LandRocks NRE, B3 punch feedback); Mac `pad-hands-menu` (M1, M2, no always-on element icons);
  u2 `player-mods`. Queued: `hold-east-path`. Owed to Andrew: option sheets for casting pace and big spells (C1, C2)
  and the nine-valley world, biomes, rivers, desert Hold, Punchy creature sync (W1-W4).
- **Overnight 2026-11-08 (Andrew: "keep working through these all night"):** the order is: land `peer-vanish`;
  restart u2 on `wip/u2-player-mods` once the PC's two-peer run is over (the watcher stopped it at 1.22 GB free);
  rerun spell-balance's probes on the branch and dev, then land it; PC `hold-east-path`; land the Mac's
  `pad-hands-menu` and u2's `player-mods` (PC compiles). Then cut alpha 0.1.12 from main (the PC Windows, the Mac
  `--mac-only` into the same draft, after its job). Option sheets go to Andrew in the morning, not acted on.
- **Overnight progress:** landed `peer-vanish` (dafa11a6), `spell-balance` (dd9c09f1, probes pass on a quiet PC),
  `pad-hands-menu` (1f9088fb), `player-mods` (9b2dfacb, u2 core + PC view). Option sheets on dev:
  `docs/design/casting-pace-options.md`, `docs/design/world-nine-options.md` (Andrew decides). **alpha 0.1.12:** main =
  dev at 3321b336 (version bump); the PC builds `--windows-only` into the draft; the Mac does `--mac-only` and Linux
  after `hold-east-path`. Draft only: publishing waits for Andrew. u2 on `punchy-sync` (resumed from its wip; the
  watcher stopped it twice when landing gates ran beside the Editor: don't land while u2 and an Editor both run).
  The 28 empty chat records are `of` follow-ups, not a bug; "I don't see a new request" is Claude misjudging real
  prompts (open).
- **u2's IP changes when the watcher restarts it** (172.26.211.206 -> 172.26.215.195 on 2026-11-08); `start.cmd`
  said "Briefed u2" but no runner started, an hour lost. After any u2 restart, check `peek.sh u2` shows a runner
  `started` line; if not, `linux.sh runner u2 <ip>` with the IP from `(Get-VMNetworkAdapter -VMName u2).IPAddresses`.
- Landed overnight too: `probe-cleanup` (fcb67af7: the pause menu IMGUI fix; menu, shapeshift, combo, aimlooks pass;
  #166 closed). alpha 0.1.12 draft: Windows uploaded and install-tested (Andrew's PC is now on 0.1.12), notes on it,
  NET CONFIG 10564187343791070933; the Mac's half pending. PC now on `hitch-hunt` (L3).
- **`mcp-blue-fire` landed** (1701fbed, u2, #167): probes misread the MCP's short draft reply. lookfx 101/103: two
  element-colour checks (J-052) still fail, since `set_look` on an element now colours its spells too; #167 stays open.
- **`hold-lockstep-fast` landed** (c3c8baac, the Mac): the Hold lockstep test runs only the 12 chunks round the Hold
  (as in the game), same 3500 ticks and per-tick checksums; EditMode 49.8 s (was past 180 s), core suite 3.5 -> 2.4 min.
  EditMode can now be fully green on the PC; on the Mac 8 ApiKeyTests still fail (Windows-only vault). Briefs the lead lands must `git rm` their own claim on the branch.
- **Lost 1.5 h on u2 (2026-11-07):** the watcher stopped it for low memory at 13:10; `start.cmd u2 <brief>` at
  13:21 said "the mailbox runner is running", but no runner was alive and the brief sat in the inbox until
  `linux.sh runner u2 <ip>` at 14:50. Unproven guess: `runner.mjs`'s lock holds the old pid, a new process reuses it
  after the boot, and the runner exits as "already running". Check `pgrep -af runner.mjs` after every start.
  The watcher also went to 0 at about 14:40, when the morning's idle PC worker session (pid 38652) was stopped.
  It's restarted, but now runs under the lead's session.
- Stale `Unity.exe` pid 22552 (since Oct 29, 9 MB) makes `status` say "no Editor answers"; not proven ours, left.
- **Work log (ed5f0c40):** `tools/workers/note.sh <who> <phase> <what>` appends to `~/.agent-bridge/work.log`;
  `status` shows each worker's latest line and when each claimed branch was last pushed. `unity-task` logs per
  phase. Not yet: u2's `runner.mjs` and the Mac don't write to it (u2's progress shows through its branch push).
- `status` said nothing about Unity on 2026-11-07: a `Unity.exe` was running but no Editor answered the CLI. It
  now says so.

## Who runs where

| Where | Account | What it's for |
|---|---|---|
| **DesktopUnity, the lead** (the PC, interactive; commits docs and lands from the `coordination` worktree) | Max | Coordination (`unity-lead`), plus the only Windows GPU: host passes, player builds, release candidates, all through `unity-task` subagents |
| **unity-game-macbook** (Remote Control) | Max | Compute with Unity and a GPU (EditMode, probes, profiling); the headless Windows build as a fallback. Its route doesn't confirm reading; check its branch if quiet |
| **Windows VMs** w1 (Pro), w3 (Max); w2 and w4 are rescued and off, for Andrew to remove | | Rules plus Unity batch compile. Idle since Oct 30; w1 didn't fit beside the PC's Editor on 2026-11-02 |
| **Linux VMs** u1 (Pro), u2 (Max) | | Core and sandbox only, about 10x cheaper for rules work. 1 GB start |

- **Workers take briefs straight from the mailbox:** `start.cmd <vm> <brief.md>` boots and briefs;
  `brief.cmd <vm> <file> [-FollowUp <id>]` briefs a running VM. `runner.mjs` in the VM runs it headless,
  and `bridge-watch.sh` brings the report to the PM's mailbox (`bridge.sh wait pm` as a background
  task). Briefs live in `docs/handoffs/briefs/`. Watch workers with `tools/workers/peek.sh`.
- **Since D15 (2026-11-06) workers land their own work and take briefs from the queue**
  (`docs/handoffs/briefs/queue/`, format in its README): `runner.mjs` claims the next brief a machine can do
  when idle (queue on for u2 only), and the worker lands with `land.sh here`. Only a refusal reaches the PM.
  Feature intake is `/backlog-groom intake <idea>` (Sonnet). A runner picks up new code only when restarted
  (`linux.sh runner`; kill the old one first); the watcher too (`bridge-watch.cmd`).
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
- Controls are designed and judged on the controller first; the keyboard follows later (2026-11-07).
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

- **Workers (2026-11-07, afternoon):** the PC is free (stamp-looks landed); u2 is idle (queue empty). The Mac is offline until about 3 pm (Andrew), then it can take Unity briefs beside the PC. (Back and idle since 16:00.)
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
   **Maps as data with terrain through the MCP (Andrew, 2026-11-04, #69):** designed (`docs/design/maps-as-data.md`, nine phases, about five weeks); phase 1 landed (5638da4d; Windows with graphics checked, valley unchanged; `BuildWindowsFromCommandLine` untested on it, so watch the first release build); phase 2 landed (real two-peer run passed, no desync); questions answered (J-073). Phase 4 landed; start-up baseline on the PC 7.37 s on dev, 7.49 s with `ground/` (3 runs, not cold-disk, no real screen): phase 5 may add 2 s. Phase 3 landed (ba5a2326; PC run: no desync on three peers, worst r32 frame 30.3 ms bar one 115 ms undo, 1 m/s ease; canyon then dragon not run; four full-detail chunks under one stamp never run; #142 props; #143 and #144 fixed by `stamp-guards`, dcd1e76a, new places not yet seen against the drawn map; #145 Editor memory, maybe a leak). `land-shapes` landed (43658bcb: canyon, cliff, rough 40, rock; three peers on the new shapes and TerrainRock PSO warm-up not run; rebuild the yard). `props-follow-land` landed (1c682dc9, #142). `named-acts` landed (c81b6382) and the TerrainRock PSO (46897dfb) after the combined PC run; real Claude used `call_act` over MCP, the chat box itself untried. Open: #146 canyon refused by seed, #147 crate slide, #148 small stamps, #149 dropped rows say done, #150 rows do not share a spot; #151 stamp frame over 100 ms on the Mac (measure on the PC). `acts-honest` landed (da3184e4, #149, #146, #150; not drawn, PC owes a look: campsite at a real place, folded floor on several seeds, spot-search hitch, a real Claude on a short row). PC profiling done (#151, #153-155); playtest findings #156-159. Phase 5 landed behind its switch (9372ba25; 34be621d compiled only in core until the PC opens it); Phase 5 judged safe on the PC (#137 closed; land mode's look not judged). #151 fixed (a3824945). Maps phase 6 waits. Do not ship `Builds/PcDev` or `PcRel` (they contain `PcPerf`). Phase 8 must give a joiner's `RulesMcpServer` the host's map. Same-network Host and Join have no menu or flag: the PC needed a throwaway driver.
   Primitives: the "make a centaur" real-prompt test (#73). Strikes onto CommandChannel with #83. #87 flaky dragon budget test. Building on water (#60).
3. **The Hold (Andrew, 2026-11-05, #152, J-074, J-075; `docs/design/the-hold.md`):** the first game loop, on the hill west of Hollowmere. Phases 1 to 5 landed, on screen too (hold-ui 1858577c), with castle-seat and the points lockstep test (3b5fa972). Two-peer bot runs last far longer than the 20 min target (fire+fire, fire+water hit the 40 min cap). `Builds/PcDev` is rebuilt at e341d13 for Andrew's playtest, `docs/playtests/2026-11-07-human-plan.md` (V1-V3 voice, T1-T5 Hold). Queued: #165, #156 core half, #147/#148 (needs Unity).
4. Gaits by rule (`builders.md`), after bodies (J-068).
- Cleanup: items 1, 2 (combos; defense and room in flight), 3, 5, 7 (items, entities, reactions; spells left), 8, 9 done; 4 dropped (J-069); 6 waits on #69.
- Tests: #162 landed (56f7a3e5): parallel core tests, gates in `tools/workers/gates.sh`. PC gates 121 s, tests 85 s; dotnet peaks at 6.8 GB, so keep other heavy work off a landing. The Linux VMs are up (u2 on Max). Tiers and slimming not needed yet. Decoupling waits until the Hold is done (Andrew, 2026-11-05).
- Backlog and priority: the board, https://github.com/users/anweather/projects/3 (epics, `area:` and `p1`-`p3` labels); `docs/BACKLOG.md` is the pointer and `docs/backlog-archive.md` keeps the design detail (more than one map is #69).

## Andrew's board

https://claude.ai/artifact/KhW6DyMwmA1ncXCxLxbBgY: what needs his verdict, what landed, who's on what.
Publish it only when there's a decision for Andrew (2026-11-06; publishing after every landing cost too much
context): bring it up to date with `tools/workers/board.mjs`, then publish `.pm/board.html` with `url`.

## Open questions for Andrew

- On the board: soot and black ice read as a pale blue film; darker and more solid? Creatures clip; walking-rules clip judged (no gesture, J-069).

## Telemetry

- PM cost: up to 69% of one session's context re-reads were mechanical (2026-11-02). Landing and the board are scripted (`land.sh auto`, `board.mjs`); measure the next batch before trying a cheaper main PM.

- Every worker commit carries cost lines; `docs/telemetry/` holds the reports (latest 2026-11-06:
  coordination 53% of spend, cache reads 70% of feature cost; D15 is the answer). A self-landed merge
  also writes a Coordinator line from the worker session (e341d133: 694 min), which double-counts.
- Skills: `agent-cost-telemetry`, `agent-cost-report` (in `~/.claude/skills`), `screenshot-budget`.
- Known gaps: the Mac has no commit hook; merge lines double-count the host's work; Pro transcripts
  aren't on disk.
