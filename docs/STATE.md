# State (2026-11-08, night)

The one page the lead reads first (`unity-lead` skill). History is in `docs/coordination-log/2026-11.md` (the full
STATE before this handoff is at its end); calls are in `JUDGMENT_CALLS.md`.

## Now

**The goal: Andrew playtests the 3 x 3 world (the nine)** (`docs/design/world-squares.md`, J-080; rings deferred,
all nine generated at load). It is playable on dev as of 487f537e. One blocker left: steep land is walls.

Landed 2026-11-08 (all on dev):
- maps phase 7 18f59b04 (sea from land, Bake Map Data with an inputs hash, `-map`); `world-plan` 3c4d62e1;
  `test-speed-core` 05786f5c (core suite about 1 m 10 s now); `cell-memory` 0ffdc178 (Cell 128 to 64 B, the last
  world freed after Play); `view-rebuild` 6313314e (the nine start in about 6 s, rebuild under 2 s); `chunk-gen`
  d4e22253; `chunk-gen-polish` 3a3b328d (smooth valley seams, wandering rivers, mountains, a spawn per square);
  `play-nine` 487f537e (bake at start, biome ground, Host map and Start in choices, joiners load the host's map,
  waystones, respawn at the nearest reached; #169 fixed).
- The Mac: `probe-speed` 5995cce1 (`probe.sh a b c`, `sweep --half 1|2`), `roomopen-sync` e39d9a2f (#171 was the
  probe, not a desync), `mac-telemetry` 19df6c79, earth probe 8b9aeb44.
- Clip for Andrew (sent): `.claude/worktrees/host-ux/unity/Builds/demo-videos/play-nine-valley-to-reedbeds.mp4`.
- play-nine's two-peer run: two builds over loopback, players=2, no desync, 25-27 ms mean / 33-36 ms p95, 5.5-5.8 GB
  per process. Its scripted walk never left the valley (stopped by the town); the Editor clip shows the crossing.

Waiting:
- **The PC:** compiling u2's `late-join-core` (6d10ea7a; #173 #164 #55 #83; StandKeeper into the core) with full
  EditMode and `online roomopen roomruin smoke`. Land it with `land.sh auto late-join-core --compiled --deletes`
  if green (its brief file `queue/late-join-core.md` must leave the queue with it).
- **u2:** on `core-boundaries` (claimed, J-081: CI caching, split test projects, Unit/Simulation categories,
  World.React split, dependency table). Briefed `nine-slopes` next, the playtest blocker: ChunkGen writes `#` on
  every steep slope and `#` plays as a stone wall (about 35k cells, nearly the whole valley seam). If u2 is deep in
  core-boundaries, nine-slopes still comes first.
- **The Mac:** on #172 (sweep failures): glowmoss bisect (a regression since 0.1.12), rowhouses probe fix ready, then
  defense, feel, roll, roofs, hitch, productsview. Andrew reads the Mac himself: don't relay its reports.

## Who runs where

| Where | What it's for |
|---|---|
| **DesktopUnity, the lead** (the PC; commits docs and lands from `.claude/worktrees/coordinator`) | Coordination (`unity-lead`); Unity jobs go to `unity-task` subagents in `.claude/worktrees/host-ux`, one at a time |
| **SpellValleyMac** (Remote Control; SendMessage, no read receipts) | A second Unity Editor: probes, sweeps, bisects, macOS and Linux builds. Self-lands with `land.sh auto ... --compiled`. Commit hook on (`Worker: mac`) |
| **u2** (Linux VM, Max) | Core, rules, tools, data, measuring under dotnet. Takes `Needs: core` briefs from `docs/handoffs/briefs/queue/`, or `tools/workers/start.cmd u2 <brief path>`. Its Unity-C# branches come to the PC to compile |

- The watcher (`tools/workers/bridge-watch.sh`, one only, started 2026-11-08 16:30) pauses u2 with Save-VM below
  1.5 GB free and resumes it at 6 GB. Reports land in `~/.agent-bridge/pm/`; read by hand (no listener running).
- Landing gates drop to 3 test workers under 10 GB free (`tools/workers/gates.sh`).
- Testing is layered (`docs/TESTING_LAYERS.md`): a unity-task runs Unity-side tests and touched probes; full EditMode
  only when `Chemistry/` changes; the sweep runs everything daily.

## Standing rules (Andrew)

- **u2 first, paper first, clear after big features** (2026-11-08, unity-lead skill): core halves go to u2; open
  looks start as a paper prototype or stills board; after a big landing, hand off and remind Andrew to clear.
- Publishing a release needs Andrew's explicit go; always run the PC-joins-Mac test first (0.1.12 published
  2026-11-08 at d1d142c7).
- Never kill a Unity Editor (one-off exception logged in J-080); open host-ux by full path; leave pid 22552 (Hub).
- Deleting unmerged branches, force-pushing and changing the plan are Andrew's. Never type sign-ins or tokens.
- Controller first. The TS sandbox is frozen. Caves deferred. Ask one question at a time, logged in JUDGMENT_CALLS.
- GitHub issues are the lead's: file with a `Discovery-Cost:` line (missed on #169-174), close when fixed.
- No weekly CI review (Andrew, 2026-11-08).

## Next, in order

1. **Land `late-join-core`** when the PC's compile reports green (tested merged with dev in host-ux).
1b. **Compile and land `core-boundaries`** (u2 done at fac1c8e2, 133 files, mostly Chemistry/: full EditMode).
2. **`nine-slopes` (u2):** compile and land it, then a unity-task walks out of the valley through each open side in
   the Editor (a clip). No Windows build for Andrew and no waiting on his play (J-082).
3. **The far ring on the PC** (phase 6's rest: landmarks behind haze, the known ring). The paper is done (507e0f72,
   `tools/world-paper/` Horizon panel); its numbers and four fixes are in `world-squares.md` section 4 "On paper".
   Core half (known chunks' 9 × 9 heights, `seenFrom` as a radius, new values) to u2; the view half to the PC.
Then: `core-boundaries` landings (step 4 changes Chemistry/, PC compiles); a Scripts runtime asmdef (measure compile
time first, J-081; no longer waits on the playtest); #170 barrels; mana after Andrew plays quick taps; maps phase 8
(runtime map switch rebuilds terrain).

## Open questions for Andrew

- None open. The play-nine clip is with him; his verdict on the nine's look goes in J-080.

## Telemetry

Latest report `docs/telemetry/2026-11-08.md`: coordination 55% of spend, cache reads 73%, the PC 4.7x u2 per feature.
The Mac's commits carry cost lines since 19df6c79. `feature-costs.mjs --since 2026-11-08` returns nothing while
`--since 2026-11-07` works (a date or timezone bug, unfiled).
