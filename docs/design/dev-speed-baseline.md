# Feature speed: the baseline before the decoupling (2026-11-26)

Andrew asked for a "before" of the whole development cycle so the decoupling plan
(`docs/design/architecture-review.md`) can show whether it made feature work faster. This is it, taken from the
cost trailers every commit carries (`tools/workers/commit-cost.mjs`: `Worker-*`, `Host-*`, `Coordinator-*`).

**Cut-off:** dev at `6f86730c` (2026-11-25), the last commit before step 1 of the plan landed (`90e4a0b8`).

## Per week, features merged into dev

A feature is a merge on dev's first-parent line (or a straight commit carrying cost trailers), as
`tools/telemetry/feature-costs.mjs` counts it. Features with no code lines and the one 361.80M-ite outlier
(`branch`, 2026-11-23) are left out. ite = input-token equivalents (new input + 1.25 × cache write + 0.1 × cache read +
5 × output).

| Week (Mon) | Features | Median agent minutes per feature | Median cost per feature (k ite) | Median ite per code line | Failed tool calls |
|---|---|---|---|---|---|
| 2026-10-27 | 120 | 22 | 1,111 | 3,264 | 2.8% |
| 2026-11-03 | 126 | 54 | 1,934 | 7,139 | 2.2% |
| 2026-11-10 | 54 | 65 | 2,375 | 6,712 | 1.9% |
| 2026-11-17 | 11 | 52 | 1,698 | 7,823 | 2.1% |
| 2026-11-24 | 21 | 43 | 2,264 | 11,481 | 1.8% |

What it says: from the first week after 0.1.2 to the last week before the plan, a feature took about **2–3× the
agent time** (22 → 43–65 minutes, median) and **3.5× the cost per line of code** (3.3k → 11.5k ite).
Over the whole span, coordination (the coordinator's merge lines) was 37% of all spend.

## Landing gates

`tools/workers/gates.sh` on the full suite (cloud, 4 cores, 2026-11-26): **7 m 21 s** for any change, docs
included, plus up to 3 reruns when dev moved during the gates. `dotnet test core` alone: 334 s (one
`Game.Core.Tests`, before step 2).

After steps 1–3 (for comparison as the plan goes on): a docs or tools change 1 m 00 s in the cloud (48 s on the
Mac); a Land test change 6 m 07 s; a Runtime change 7 m 20 s. Step 5 is meant to bring a Land change down.

## Taking the "after"

```sh
node tools/telemetry/feature-costs.mjs --ref origin/dev --since <date the plan's last step landed> --csv after.csv
```

Then the same weekly medians from the CSV (columns `date`, `code`, `ite`, `minutes`, `tools`, `failed`), with the
same exclusions (no code lines; a single feature over 100M ite). Compare like with like: a week of feature work, not
a week of refactoring. Gate times: `time tools/workers/gates.sh <log>` with `GATES_FILES` set to a docs file, a
Land test, a Runtime Land file and a Sim file.

Caveats: agent minutes are wall time of the sessions between commits, so a session left waiting counts; the mix
of work changed (week 1 had many small UX passes, later weeks larger scale work), which by itself raises the
per-feature numbers. Cost per code line is the steadier measure.

## The Unity loop and the gates on the Mac: before step 10's assemblies (2026-11-27)

Andrew asked for a before and after of the developer experience and the test times for each kind of feature, around
step 10 (Prototype.Game into feature assemblies). All on the Mac (the M-series dev box), the Editor open on the main
checkout and otherwise idle, dev at `7dc22b48` (seams 1-4 in, the assemblies not yet split).

**Save to a ready Editor** (`tools/telemetry/unity-loop.sh`: a trailing newline added and taken away, then a new public
type added and taken away; seconds from the save until the Editor has compiled and reloaded; 4 body samples and 2 API
samples a file):

| File (kind of feature) | Body edit | API edit | Assemblies an API edit rebuilds |
|---|---|---|---|
| `ChemGrid.cs` (the grid) | 12.1-19.1 s (12.5 typical) | 14.4-14.9 s | 9: the game, its Editor and tests, Net, the probes (3), Assembly-CSharp (2) |
| `HoldShow.cs` (a game mode's view) | 10.6-13.3 s | 14.3-14.6 s | the same 9 |
| `SpellCaster.cs` (the wizard) | 12.2-13.2 s | 13.3-14.5 s | the same 9 |
| `ValleyFauna.cs` (fauna) | 11.6-13.8 s | 12.8-15.1 s | the same 9 |
| `ControlsMenu.cs` (a menu) | 12.7-13.6 s | 14.0-14.7 s | the same 9 |
| `Chat/GameChat.cs` (chat) | 11.3-13.4 s | 13.2-14.7 s | the same 9 |
| `Probes/ProbeRoomProgress.cs` (a probe) | 8.0-10.4 s | 9.0-10.1 s | 3: the probes' own |
| `Plans/TreeBudget.cs` (a plan) | 7.9-10.1 s | 13.7-15.2 s | 9: Plans and everything above it |

A body edit rebuilds only its own assembly (Unity skips dependents when the public surface is unchanged); the domain
reload is 3.3-4.4 s of every one, and the floor for any edit is about 8 s. **Play mode:** 4.8-4.9 s to enter (7.2 s the
first time after a compile) to a running grid 30 frames in, 1.1 s to leave. **EditMode suite** (`tools/editmode.sh`,
567 tests): 65 s.

**Landing gates** (`time tools/workers/gates.sh`, the Mac, nothing else running):

| Change | Before the plan (`6f86730c`, every gate for every change) | Now (`7dc22b48`, gates by module) |
|---|---|---|
| A docs file | 206 s | 75 s |
| A Land runtime file | 206 s | 86 s |
| A Land test file | 206 s | 85 s |
| A Play (game mode) file in the core | 206 s | 181 s |
| A Sim file (`World.cs`) | 206 s | 182 s |
| A view file (`HoldShow.cs`) | 206 s | 74 s (+ the Unity checks above) |

(The cloud's 4 cores took 7 m 21 s for the same "before"; the Mac is about twice as fast.) The 75 s floor is the build,
format and the workers' tests, which every change runs.

## After step 10's assemblies (2026-11-27), fresh Editors

The first "after" run was taken in the same Editor as the "before", hours and dozens of reloads later, and its domain
reload had crept from 3.5 s to 7.4 s; so both were taken again, each in a freshly started Editor on the same Mac:
`7dc22b48` (before the split) and `5aa3d7e0` (after). Mean seconds from save to a ready Editor, 4 samples each
(`tools/telemetry/unity-loop.sh`, ROUNDS=2):

| File (kind of feature) | Body edit before → after | API edit before → after | An API edit rebuilds, after |
|---|---|---|---|
| `ChemGrid.cs` (World, the base) | 12.1 → 10.3 | 13.1 → 13.6 | every layer above World, Net, the probes, the Editor tools and tests |
| `HoldShow.cs` (Modes) | 11.8 → **7.6** | 13.6 → **11.0** | Modes, Chat, Shell, Net, the probes, tools, tests |
| `SpellCaster.cs` (Player) | 12.3 → 9.4 | 13.6 → 12.4 | Player, Fauna, Modes, Chat, Shell, ... |
| `ValleyFauna.cs` (Fauna) | 11.8 → 8.9 | 13.2 → 11.2 | Fauna, Modes, Chat, Shell, ... |
| `ControlsMenu.cs` (Shell, a menu) | 12.5 → **8.9** | 14.3 → **10.5** | Shell, Net, the probes, tools, tests |
| `Chat/GameChat.cs` (Chat) | 12.8 → **8.8** | 14.5 → **10.4** | Chat, Shell, ... |
| `Probes/ProbeRoomProgress.cs` | 9.9 → 10.0 | 10.2 → 9.6 | the probes' three (unchanged) |
| `Plans/TreeBudget.cs` | 9.4 → 10.2 | 14.9 → 14.9 | (unchanged) |

What it says: an edit in a feature layer is **25-35% faster** (about 12 s → 8-9 s for a body edit, 14 s → 10-11 s for
an API edit); an edit in the base (`ChemGrid`) gains little, because everything is above it. The floor is the domain
reload (about 3 s in a fresh Editor, the same before and after) plus Unity's refresh, which no assembly split moves:
probes and plans were already their own assemblies and didn't change. Domain reload grows with how long the Editor has
been open, so **restarting the Editor after a long session** is worth as much as the split (an hours-old Editor took
13-17 s for the same edits).

Unchanged by the split: Play mode 4.7-4.8 s to enter (6.6-6.7 s the first time), 1.1 s to leave; EditMode 62 s (572
tests after, 567 before). One EditMode test, `TreeImpostorTests.ImpostorsAreOnUnlessTurnedOff`, fails in both when the
suite runs right after Play mode (the run leaves `MapTerrains.TreeDistance` changed and domain reload is off), not on
its own: a test-isolation bug older than the split.

**Gates after** (`5aa3d7e0`): docs 83 s, Land runtime 82 s, a Play file 162 s, a Sim file 161 s, a view file 65 s, all
within noise of before: the split is view code, and the gates are the core's. The core's Play and Sim changes are now the
slow landings (they run nearly every test project); Play out of the Sim assembly (step 9's leftover) is the next lever
there.

**The structural change, which the timings don't show:** a feature layer can't reach up or across any more (the compiler
refuses), so a new feature is a new folder and asmdef on top that touches no hub (`Scripts/CLAUDE.md`, "Assemblies").
The per-feature agent cost (`feature-costs.mjs`, the first section) is the measure of that, once a week or two of feature
work has landed on this layout.

## After Chem.Play (2026-11-27)

The game modes' systems (Hold, defense, course, `WayField`) are their own assembly, `Chem.Play`, over Sim behind the
`IWorldModes` seam (`docs/design/architecture-review.md` step 9; merged at `cd990cc1`). Gates on the Mac, warm build:

| Change | Before the plan | After step 10 (`5aa3d7e0`) | After Chem.Play (`cd990cc1`) |
|---|---|---|---|
| A Play (game mode) file in the core (`Runtime/Play/CourseSystem.cs`) | 206 s | 162 s | **62 s** (Chem.Play.Tests, Hold.Runner.Tests, the rest's Unit smoke) |
| A Sim file (`World.cs`) | 206 s | 161 s | 185 s (every test project, as before; noise) |

A game-mode change in the core now lands at about the docs floor plus its own tests. A Sim change still runs everything,
since everything is above Sim; raiders, golf and the dragon stayed in Sim (they're the reset, the save and what spells
act on), so their changes are Sim changes.

## After the long pole (2026-11-27)

A Sim change runs every test project side by side, so it lands at the pace of the slowest. That was
`Chem.Lockstep.Tests`, about a minute behind the rest, and inside it one fixture: `LateJoinTests` (83 s of its 84 s
alone), whose tests replay a host's run for each joiner and snapshot one after another (the slowest, the snapshot
joiner, was 110 s under the full suite). Its replays, and `OnlineValleyTests`' three machines, now run side by side,
each on a world made on its own thread; the assertions are the same, asserted in the same order afterwards
(`docs/TESTING_LAYERS.md`, "Keeping the core suite fast"). Nothing moved to `Sweep`, no test or assertion changed.

On the Mac (12 cores; an Editor running its own tests in the main checkout, so the load was 6 to 50 throughout):

| | Before | After |
|---|---|---|
| `Chem.Lockstep.Tests` alone | 84 s | **28 s** |
| A Sim file (`World.cs`), `gates.sh`, back to back (load ~40) | 220 s | **151 s** |
| ... its projects: Lockstep, LandWorld, Rules.Mcp, Sim, Play | 180, 124, 113, 112, 93 s | 97, 113, 106, 103, 86 s |
| A Sim file, single runs | 189 s (load ~8) | 148 s (load ~15), 149 s, 184 s (load ~50) |
| A Play file (`Runtime/Play/CourseSystem.cs`) | 62 s (above) | 68 s (Lockstep runs only its Unit smoke there: unchanged) |
| The whole suite, Sweep too | 180 s, 2336 tests | 143 s, the same 2336 names and outcomes |

The pole is gone: every project now finishes within about 15 s of the others, and what's left is the machine's CPU
shared by twelve test hosts, not one serial chain. The next projects alone are 20-43 s (Land 29, LandWorld 43,
Rules.Mcp 38, Sim 31, Play 20), so the ~100 s each under the gates is contention; a quieter Mac should land a Sim file
in the 110-120 s the goal named, but this machine was never quiet enough to show it. A `GeneratedSourceTests` peer pair
was tried and left: its two worlds step in turn with a checksum between, and a world packs only on the thread that
made it.

## Edit floor (2026-11-27)

Where the 8-9 s of a body edit in a feature layer goes, and what came off it (branch `reload-floor`). The Mac, a fresh
Editor on the yard, the load average under 12 (another worktree's dotnet tests had it at 30-60 earlier, which doubled
every number: compare only quiet runs). Unity's own split, from `Library/Bee/fullprofile.json` and the Editor log's
"Domain Reload Profiling" (the diagnostic switch `EnableDomainReloadTimings` names each `[InitializeOnLoad]` and each
object woken after the reload):

| Phase (a body edit of `HoldShow.cs`) | Time | Whose |
|---|---|---|
| Refresh, until the build starts | ~0.4 s | Unity |
| Csc of the edited assembly (warm compiler server, analyzers included) | 0.4 s | Unity; the analyzers are ~half of a cold csc (Sonar 85% of theirs) |
| IL post-processing: the edited assembly **and all 10 above it**, at once | 1.3-1.8 s (0.2 s for one alone) | Unity: Bee re-runs ILPP for every dependent of a changed DLL, though only the edited one is copied |
| Domain reload: tear down the old domain (`CreateAndSetChildDomain`) | 0.53 s | Unity |
| Load assemblies, type caches | 0.3 s | Unity |
| `[InitializeOnLoad]`s and their methods | 0.7 s | packages: Burst 147 ms, Input System 115, Multiplayer Play Mode 92, the CLI's pipeline 78, Services Deployment 51; ours 1-2 ms in all |
| Objects woken after the reload (`AwakeInstancesAfterBackupRestoration`) | 0.84 s | Editor windows (6 dock areas, an open **Project Settings window ~0.35 s**, Asset Store window 0.07 s), and the yard's `[ExecuteAlways]` views |
| ...of which ours | ~0.45 s | `Ocean` 220 ms (map load 30-100, sea mask 130, mesh 100), `GeneratedProp` ×276 ~100 ms (palms), `VillageHouse` ×19 ~35 ms |
| After the reload, the yard check (`YardAutoBuild`) | 65-120 ms | ours: read and parsed the 5 MB yard scene twice |
| The rest (scene repaint, the CLI's readiness poll) | ~1 s | Unity |

**Cut** (no change in what the game does or builds; EditMode and the probes as before):

- `YardAutoBuild` skips its scene check after a reload when the scene, the network prefab list, the ground template
  and its own code are as they were when it last passed this session.

**Result** (measured with a second cut, since dropped: `GeneratedProp` and `VillageHouse` kept their meshes through an
edit-mode reload, ~140 ms of the gain, but it was ~200 lines of stashing for a gain the loop can't see, so it was
reverted; the yard check skip alone is 65-120 ms): the domain reload was ~150 ms shorter (2.95-3.15 s → 2.80-2.97 s, mean of 8 per file). Save to ready
Editor, same Editor, same load, ROUNDS=2 (`unity-loop.sh` reports in 0.1 s but polls every 0.5-1 s, so the gain is
under its resolution):

| File | Body edit before → after | API edit before → after | Reload before → after |
|---|---|---|---|
| `Modes/HoldShow.cs` | 8.1 → 8.8 | 10.6 → 10.7 | 2.95 → 2.80 s |
| `Shell/ControlsMenu.cs` | 8.6 → 8.6 | 9.4 → 8.9 | 3.02 → 2.89 s |
| `World/ChemGrid.cs` | 9.8 → 9.8 | 13.6 → 12.4 | 3.15 → 2.97 s |

**Not cut, and why:** the `Ocean` (220 ms) loads the map and its shore texture, which `MapLand` releases before every
reload, so keeping it would mean keeping the map too. The rest of the floor is Unity's or a package's: the dependents'
IL post-processing (Unity's build graph), the package `[InitializeOnLoad]`s, and the analyzers (they're the only check
the view code gets; scoping them off the Editor's compile would lose it). Free for anyone: **close the Project Settings
window** while editing scripts (its search service costs ~0.35 s a reload when it's open), and restart an Editor that's
been open for hours.
