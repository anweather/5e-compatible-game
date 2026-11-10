# State (2026-11-09, morning)

The one page the lead reads first (`unity-lead` skill). History is in `docs/coordination-log/2026-11.md` (the full
STATE before this handoff is at its end); calls are in `JUDGMENT_CALLS.md`.

## Now

**The 3 x 3 world (the nine) plays, and the far ring shows.** Andrew doesn't need a Windows build or a playtest yet:
keep working without his play (J-082). dev is at 5b79241c.

Landed 2026-11-09 (all on dev):
- `late-join-core` 0372298f (a late joiner's replay equals the host's; #173 #164 #55 #83). `core-boundaries` dc26ac97
  (Unit/Simulation categories; every fixture needs one, `EveryFixtureIsUnitOrSimulation`); core-boundaries-1
  e8ad931c (CI paths, NuGet cache, tool test projects, dependency table). J-081: step 4 closed, S3776 stays a warning.
- `nine-slopes` 3b3cbac4: steep generated land is dirt, not stone walls; the wizard walks out east and north (north
  only by the west corridor near x 84: the valley's own hills, by design). Clip `nine-slopes-east.mp4` (sent).
- The far ring: paper 507e0f72 (`tools/world-paper/` Horizon panel; numbers in `world-squares.md` section 4);
  `far-ring-core` 663c226a (KnownRing, `world/horizon.json`; 144 known chunks 35-48 ms on a worker); `far-ring-view`
  1eec423c (known-chunk meshes, haze, far landmarks, sea plane; camera far plane 15 km, cell dressing culled at
  500 m); `nine-volcano` 0cc4d8dd (`musts` in the nine's `map.json`: "Old Grumbler" in the middle square, generator
  5, NineGolden 0x7c681170); `far-volcano` 5b79241c (a steel-blue cone with a crater notch, stretched only past
  sight so the hand-off doesn't pop). Clips sent: `far-ring-nine.mp4`, `far-volcano.mp4` (from ~200 m up: at the
  spawn's ground a nearer 156 m hill hides it).
- #170 `barrels-170` 333dfe05: smoke passes in full on a fresh yard (first bad was 6313314e view-rebuild). **Rebuild
  the yard before trusting smoke after a switch** (a stale yard gave false bisect results).
- The Mac: glowmoss ee82a35b, rowhouses 3e57216a, defense 46bb2999, feel ac3250cd, roll 66e99339, roofs 6811c427
  (a real bug: laid rows' walls given to scene houses), productsview 7ba08d6e, stamplooks bb0b9d1c, roomprogress
  fa2451d7, progress-restore 9c28329b, map-proof-tests 97164fc2 (`[OnTheValley]`, `ChemGrid.OnMap`). Sweep on
  7ba08d6e: 111/123; dragon, housecam, scopelooks, airpeers pass on newer dev; the rest are the Mac's Claude setup.
- Numbers now: EditMode 1862 on the PC; valley smoke ~11.8 ms mean; the nine's farringclip ~31 ms mean, 38 ms p95.

Waiting:
- **The Mac was back online at ~07:55 and re-told its three jobs.** Earlier its Editor hung at 06:08 MST (pid 1031, likely a hidden save-scene modal after building the yard with
  `Temp/map` = nine); it asked Andrew to look. Rule for briefs: build the
  yard on the valley; point `Temp/map` at the nine only for the probe run.
- Nothing waits on a compile. host-ux is clean (an untracked `unity/Assets/_Recovery/` stays); Editor pid 20380 open.

## Who runs where

| Where | What it's for |
|---|---|
| **DesktopUnity, the lead** (the PC; commits docs and lands from `.claude/worktrees/coordinator`) | Coordination (`unity-lead`); Unity jobs go to `unity-task` subagents in `.claude/worktrees/host-ux`, one at a time |
| **SpellValleyMac** (Remote Control; SendMessage, no read receipts) | A second Unity Editor: probes, sweeps, bisects, macOS and Linux builds. Self-lands with `land.sh auto ... --compiled`. Commit hook on (`Worker: mac`) |
| **u2** (Linux VM, Max) | Core, rules, tools, data, measuring under dotnet. Takes `Needs: core` briefs from `docs/handoffs/briefs/queue/`, or `tools/workers/start.cmd u2 <brief path>`. Its Unity-C# branches come to the PC to compile |

Issues #170 #173 #164 #55 #83 closed 2026-11-09 (the lead may close issues now).

Right now: **the PC** on `perf-fix-3` (Fable 5.2): #185 an undrawn chunk's 47-134 ms frame, #186 a look change redraws only drawn chunks (J-086), #184 physics on the nine's walk; all view work in `ChemGrid.Visuals.cs`. **u2** on `rule-switch-by-chunk` (#190, J-086: a switch sanitizes active cells now and other chunks on activation; started 2026-11-09 16:15). `biome-from-sketch` landed 066b1d44 (J-085; plan version 3, generator 7; compiled on the PC at ff25e2d7: EditMode 1902/1903, the one failure a wall-clock dragon test #192; smoke 30/30 on both maps, the nine 14.9/20.0 ms in the Editor; a player on the nine 30 s, 0 exceptions). The nine now has dry country over about half of it, against the valley's north and east: green meets brown at the north-east corner (stills sent to Andrew; his call whether to tune the nine's humidity or sketch). Filed: #191 the far ring paints dry country white where the near ground is brown, #193 seam steps up to 3.1 m east and 1.6 m north (older than this branch). The full EditMode run now takes about 14 min on the PC (#187 149 s, ChunksMadeInTwoOrdersAreTheSame 61 s). `perf-fix-2` landed 647f169b (#183 #178 #181 closed): #183 was a player bake of the nine dropping `fullDetail` (bake format 2 now; `PinnedGround.Lay` refuses a size mismatch); #178 was the pack smoke's billboard renderer holding an unused Sphere mesh, 5-10 ms a wake (`ChemGrid.TrimFx`). Checked at 797e277d on the PC: EditMode 1891, smoke 30/30, online 38/38, roomopen 96/96, faunaresize 40/40 (the rule-switch fix's two-peer check). Player, the nine: Fireball 6 s 13.1/29.0 -> 11.9/16.9; standing 12.7/21.5 (p95 still over); walk 27.4/55.3. Open perf: #185 an undrawn chunk 47-134 ms, #184 physics on the walk, #188 the Fireball's landing frame 58-92 ms (Contour.Film.Outline), #189 its cast frame 37-43 ms, #186 a look-changing switch (and the first mod revert on the nine: ~120 frames of 100-900 ms, RedrawEveryChunk), #155 mods composed in the tick (Wildfire on: 92-103 ms), #182 culling (the Mac). The Editor grew 7.3 -> 9.7 GB over two nine sessions (#145). `perf-fix-1` landed a2285404 (#179 #180 closed; one cause: DrawSome drew all 729 chunks after a build, each tree or brush cell adding a BoxCollider; now only chunks within the known radius are drawn ahead). Player, the nine standing 21.6/28.3 -> 12.7/22.2, 10.3/15.9 once drawn; the nine's walk still 27.4/55.3 (#184 physics climbs to 8-14 ms on the walk; #185 entering an undrawn chunk costs 47-134 ms frames). EditMode 1885 on the PC. The profile landed ec0cf5cd (`docs/perf/2026-11-09-profile.md`): player valley standing 8.2/13.6, walk 11.9/20.8; the nine standing 19.5-20.6/26.3-27.6, walk 31.6/85.0. Costs filed #178 RenderGas Instantiate, #179, #180, #181 rule switch hitch, #182 culling. The p95 "regression" was filming noise. Phase 8's two-peer player build (b9aad8e, 2026-11-09): the joiner followed the host to the Chain, players=2, ~3 min all desync=False, host 6.5 ms mean / joiner 7.5 ms; a changed copy of the Chain is refused (wording has a doubled "The", and the log prints it twice). Not checked: a cast seen on both sides (a full-screen Windows Security window, PickerHost, takes every click; needs Andrew or a scripted cast). Port is fixed at 7777 (no port flag). `map-switch-view` landed 171ac83a: the switch already existed (`ChemGrid.PlayMap` reloads the scene on host or join, not while online); added `mapswitch` (no leaks; ~3.8 s a switch, one 2.6 s frame) and `online` with the host on the Chain (checksums agree); clip `map-switch-to-drowned-chain.mp4` (sent). Later: shorten the 2.6 s frame; the Chain has no crate or glow ball, so those checks skip there. `drowned-chain-core` landed 9cb66049 (baked land, 8 tests, EditMode 1870 on the PC). `tier-dials` landed 99692d2c: `FarRing.SetView(drawn, known)`, player flags `-drawn N` / `-known N`, active as a rule switch, `tools/probe.sh tiersweep` (nine only); on the nine no dial moved frame time past noise (today 2/4/7: 24.2 ms mean, 30.2 p95; table in world-12x8-scope.md "Measured"). Per-setting memory needs a player build. **u2** on `biome-from-sketch` (J-085). `world-12x10-plan` landed b817ca53 (12 x 10 EW plan golden 0x3e847c28, generator 6, KnownRing 2.0 s over 9720 chunks, seam continuous; `ChemGrid.Maps` leaves out `"bake": false` maps; pictures `docs/design/world-12x10/`; #176 the sea floor step). shores-paper landed 0725e4b7 (Shores panel; marsh, delta, lake at its own level, open coast; sea floor step 30 m -> 0.16 m; core ~7-9 u2 days, not briefed; marsh is 30% of the 12 x 10's shore, maybe too much). **The Mac** on `smoke-nine` (now; smoke's casts on the nine fail
cast_fire-bolt, fire_started, levitate_lifted), apikey-tests-mac landed e1b442f1 (EditMode 1858 + 1 skip on the Mac);
smoke-nine landed d3647cda (the casts pass on the nine); now the far ring's looks (the sea horizon's hard line, tundra plateaus as flat white wedges), far-looks landed 85180586; things-nine landed 98df234f (#175 closed: pinned world squares draw from their own ground/ pack; PinnedGround.Lay); `timing-tests-177` landed c4305161 (#177 #174 closed: timing tests take the best of three rounds, Category Timing, the creature-view one Explicit for the sweep; a probe refuses during a test run); `perf-rule-switch-181` landed ece6d649 (ApplyRules no longer passes over every cell: 0.2-0.4 ms; a sim-only switch 13-22 ms on the nine in the Mac's Editor without its Debug.Log; goldens unchanged). **Not yet checked with two peers:** the Mac's `online`, `roomopen` and `faunaresize` fail on unchanged dev ef9539b4 too, so the PC runs `online` and `roomopen` on dev right after `perf-fix-2`, then #181 closes. Left: mods composed inside the tick, 140-250 ms (#155 stays open; needs the host to announce a switch ticks ahead, a D4 protocol change, not started); a look-changing switch 400-490 ms (#186). `perf-culling-182` landed 32604782 (#187 #182 closed: the 12 x 10 ring test skips itself under Mono, .NET keeps the proof; a hidden chunk's Terrain component is off on a generated map, 729 -> 72 enabled, CullAllTerrains 8.2 -> 2.9 ms in the Mac's Editor; no player number yet). Left of culling: ~33,000 per-cell dressing renderers (#194, after perf-fix-3, same code). One smoke run in three on the nine had a boulder settle a cell off (#195). Now `perf-fireball-frames` (#195 check, #192 the dragon timing test, #188 the Fireball's landing frame, #189 its cast frame; brief `docs/handoffs/briefs/perf-fireball-frames.md`). Was: the nine drew the pinned valley from its land (GroundFromLand), not the valley's ground/ pack, so the valley's things roll on a different ground; fix (a): pinned chunks draw from the pinned map's ground/, the seam joined on the generated side (#175; #174 likely the EditMode runner's scene restore during Play, commented), all told
2026-11-09.

- The watcher (`tools/workers/bridge-watch.sh`, one only) pauses u2 below 1.5 GB free and resumes it at 6 GB. While
  the Editor runs a big job, u2 flaps paused/resumed every minute: keep u2 idle then, or leave the mailbox listener
  off until the unity-task reports.
- u2's branches pass `dotnet test` and can still fail in Unity: two misses on 2026-11-09 (a fixture without a
  category; `Does.Contain(object)`, which Unity's NUnit lacks). Both are now in `Chemistry/CLAUDE.md`.
- Stale u2 WIP branches (`wip/u2-*`, 6) and `landmark-valley-look` (Andrew, 21 ahead) are unmerged; leave them.
- Testing is layered (`docs/TESTING_LAYERS.md`); landing gates drop to 3 test workers under 10 GB free.

## Standing rules (Andrew)

- **u2 first, paper first, clear after big features** (2026-11-08): core halves go to u2; open looks start as a
  paper prototype or stills board; after a big landing, hand off and remind Andrew to clear.
- No waiting on Andrew's playtest for now (J-082).
- The 60 fps pass's fixes run on Fable 5.2 while its limit is reset (Andrew, 2026-11-09): pass `model: "fable"` on
  each perf `unity-task` (the agent has no model of its own, so it otherwise follows the lead's session).
- Publishing a release needs Andrew's explicit go; always run the PC-joins-Mac test first (0.1.12 at d1d142c7).
- Never kill a Unity Editor; open host-ux by full path; leave pid 22552 (Hub).
- Deleting unmerged branches, force-pushing and changing the plan are Andrew's. Never type sign-ins or tokens.
- Controller first. The TS sandbox is frozen. Caves deferred. Ask one question at a time, logged in JUDGMENT_CALLS.
- GitHub issues are the lead's: file with a `Discovery-Cost:` line, comment, close when fixed (Andrew allowed `gh issue create`/`comment`/`close`, 2026-11-09). No weekly CI review.

## Next, in order

(The Scripts runtime asmdef was measured 2026-11-09 and dropped: 6% faster, J-081.)
(Maps phase 8 is done but the cast seen on both peers in a player build: blocked by a Windows Security window.)
1. **A profile and optimisation pass for 60 fps** (16.7 ms mean and p95; Andrew 2026-11-09), once far-looks and the
   12 x 10 plan settle. Profile the nine and the valley in a player build and the Editor (the Editor shows farringclip
   ~31 ms, tiersweep 22-28 ms; a player build on the Chain ran 6.5-7.5 ms), find the top costs, fix them. far-looks
   (landed 85180586) ran farringclip at 32.6 ms mean but 51.0 ms p95 (was ~38), one run: check that first. Also a
   long slanted strip under Old Grumbler's far silhouette (farringclip frame 700), not known whether new.
2. (Done: biome from the world, `biome-from-sketch` 066b1d44, J-085.)
3. **Mana**, after Andrew plays quick taps (J-078). Until then, ask him only if he offers to play.
Also open: the world sketch in the core (paper landed 577a9533, `tools/world-paper/` World sketch panel; world-squares.md "Draw first": the sketch should drive the land's biome, ~2-3 u2 days, not briefed); a walkable rock look for steep generated faces needs a cell character (paper first); the ground-level view
of the volcano is hidden by a nearer hill (a viewpoint or landmark placement question, not briefed).

## Open questions for Andrew

- None open.

## Telemetry

Latest report `docs/telemetry/2026-11-08.md`: coordination 55% of spend, cache reads 73%, the PC 4.7x u2 per feature.
`feature-costs.mjs --since 2026-11-08` returns nothing while `--since 2026-11-07` works (a date bug, unfiled).
