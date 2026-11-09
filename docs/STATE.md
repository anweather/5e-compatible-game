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
- **The Mac's Editor hung at 06:08 MST** (pid 1031, likely a hidden save-scene modal after building the yard with
  `Temp/map` = nine); it asked Andrew to look. `apikey-tests-mac` is written, not compiled. Rule for briefs: build the
  yard on the valley; point `Temp/map` at the nine only for the probe run.
- Nothing waits on a compile. host-ux is clean (an untracked `unity/Assets/_Recovery/` stays); Editor pid 20380 open.

## Who runs where

| Where | What it's for |
|---|---|
| **DesktopUnity, the lead** (the PC; commits docs and lands from `.claude/worktrees/coordinator`) | Coordination (`unity-lead`); Unity jobs go to `unity-task` subagents in `.claude/worktrees/host-ux`, one at a time |
| **SpellValleyMac** (Remote Control; SendMessage, no read receipts) | A second Unity Editor: probes, sweeps, bisects, macOS and Linux builds. Self-lands with `land.sh auto ... --compiled`. Commit hook on (`Worker: mac`) |
| **u2** (Linux VM, Max) | Core, rules, tools, data, measuring under dotnet. Takes `Needs: core` briefs from `docs/handoffs/briefs/queue/`, or `tools/workers/start.cmd u2 <brief path>`. Its Unity-C# branches come to the PC to compile |

Issues #170 #173 #164 #55 #83 closed 2026-11-09 (the lead may close issues now).

Right now: **the PC** idle (host-ux detached at a535d458, clean). **u2** queued `drowned-chain-core` (the Chain's folder from `second-map`, checked; phase 8's core half). **The Mac** on `smoke-nine` (smoke's casts on the nine fail
cast_fire-bolt, fire_started, levitate_lifted), `apikey-tests-mac` (ApiKeyTests fail 8/8 on the Mac; make them
hermetic), then the far ring's looks (the sea horizon's hard line, tundra plateaus as flat white wedges), all told
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
- Publishing a release needs Andrew's explicit go; always run the PC-joins-Mac test first (0.1.12 at d1d142c7).
- Never kill a Unity Editor; open host-ux by full path; leave pid 22552 (Hub).
- Deleting unmerged branches, force-pushing and changing the plan are Andrew's. Never type sign-ins or tokens.
- Controller first. The TS sandbox is frozen. Caves deferred. Ask one question at a time, logged in JUDGMENT_CALLS.
- GitHub issues are the lead's: file with a `Discovery-Cost:` line, close when fixed. No weekly CI review.

## Next, in order

(The Scripts runtime asmdef was measured 2026-11-09 and dropped: 6% faster, J-081.)
1. **Maps phase 8: a runtime map switch rebuilds terrain** (`docs/design/maps-as-data.md`). Core half to u2 first if
   there is one; the view on the PC.
2. **Mana**, after Andrew plays quick taps (J-078). Until then, ask him only if he offers to play.
Also open: a walkable rock look for steep generated faces needs a cell character (paper first); the ground-level view
of the volcano is hidden by a nearer hill (a viewpoint or landmark placement question, not briefed).

## Open questions for Andrew

- None open.

## Telemetry

Latest report `docs/telemetry/2026-11-08.md`: coordination 55% of spend, cache reads 73%, the PC 4.7x u2 per feature.
`feature-costs.mjs --since 2026-11-08` returns nothing while `--since 2026-11-07` works (a date bug, unfiled).
