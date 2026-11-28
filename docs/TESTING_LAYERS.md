# Testing in layers (the Swiss cheese model)

Andrew, 2026-11-08: "We need a Swiss cheese model. Not every layer needs to test everything." Each layer catches
some faults and lets others through; the next layer's holes are in different places. A test runs at the layer
where it's cheapest to catch what it catches, and once, not at every layer.

Why this exists: on 2026-11-08 a PC Unity job spent about half its time testing. A full EditMode run (about 14
minutes, past the CLI's 600 s timeout) reran the whole Chemistry suite under Mono, which `dotnet test core` had
just run in 3 minutes, and the landing gates then ran it a third time. Long probe lists ran for changes that couldn't
reach them.

| Layer | Who | Runs | Doesn't run |
|---|---|---|---|
| 1. **Author** | the worker writing the change (u2, the Mac, a PC job) | New or changed tests for what it changed; `dotnet build core -c Release -warnaserror` and the tests nearest the change while working | The whole suite on every edit |
| 2. **Unity check** | a `unity-task` on the PC, or the Mac | Compile with 0 errors; the Unity-side tests (`Scripts/Editor/Tests`, `Scripts/Shell/Playtest/Editor`, `Scripts/Probes/Editor/Tests`, and `Scripts/Plans/Tests` under Mono) and the touched fixtures; `smoke` and the probes the change can reach; a clip only when asked. **`tools/editmode.sh chem` (the Chemistry tests under Mono) only when `Chemistry/` code changed** | Core tests under Mono for view-only changes; unrelated probes |
| 3. **Landing gates** | `land.sh` | Core build, format, **the touched modules' core tests** (.NET, by `tools/workers/modules.json`: their projects, the projects that reference them, every other project's `Unit` fixtures) but the `Sweep` category, the workers and chat tests | Unity, probes, the `Sweep` category, duplicate code (CI and the sweep), the core tests a change can't reach |
| 4. **Sweep** | the idle Unity machine, at least once a day and after a big batch of landings | `tools/editmode.sh sweep`: all of EditMode, duplicate code (jscpd), the Chemistry tests under Mono and the `Timing` tests (`--include_explicit true`), the core's `Sweep` category (`dotnet test core -s core/sweep.runsettings --filter TestCategory=Sweep`), every probe, a two-peer run, the hitch budget, on dev as it stands: what the layers above skip and what only appears when branches meet | Nothing skipped; it's the net |
| 5. **Release** | the release steps (`docs/RELEASING.md`) | `smoke`, `online`, the release's own probes, install from the draft, the PC joining a Mac host | Anything a sweep already covered on that commit |

The holes, on purpose:

- **Whole runs peer by peer** (the core's `[Category("Sweep")]`: a Hold run to a loss, five waves with three peers and
  a late joiner): the sweep runs them; the landing keeps the short ones (`TwoPeersWithDifferentPointsStayInLockstepThroughAWave`,
  `TheRunIsInTheChecksum`, `TwoWorldsMatchOverSeveralWaves`). A test goes in the category only if a shorter one of the
  same thing lands. Plain `dotnet test core` skips the category too (`core/default.runsettings`); CI's quality workflow
  runs everything (`-s core/sweep.runsettings`).

- **Timing tests on a busy Editor** (#177, 2026-11-09: two EditMode timings failed after a long suite and with a stray
  run alongside): a test that times something takes the best of a few rounds, each after a settled heap, and has
  `[Category("Timing")]`. A Unity-only one is also `[Explicit]`, so a full EditMode run under load skips it and the sweep
  runs it on the idle machine (`--include_explicit true`; only the timing tests: `unity command test run --mode EditMode
  --filter Timing --filter_type category --include_explicit true`). Named alone it needs the flag too. A core one stays
  in the landing suite, where .NET times it on every landing, with a looser limit under Mono.
- **A core test too slow for Mono** (#187: the 12 x 10's whole known ring, 1 s under .NET, over 180 s in the Editor):
  it stays in the .NET suite, which the landing gates and CI run, and skips itself under Mono with the reason
  (`Assert.Ignore` when `Type.GetType("Mono.Runtime") != null`), so a full EditMode run shows it as skipped rather
  than timing out. Only for a test whose proof isn't about Mono: one guarding Mono's rounding stays, made cheaper.
- **Mono versus .NET rounding** (seen 2026-11-08 on the seeded heights): layer 2 runs Mono only for core changes;
  the sweep runs it for everything.
- **A probe a change "couldn't reach" but did:** the sweep runs every probe on dev.
- **Two branches that pass alone and break together:** the sweep, and the next Unity check that merges dev.
- **A core test the module map says a change can't reach** (layer 3 runs by module): CI's quality workflow runs the
  whole suite on every push to dev, and the sweep runs it again. A path in no module runs everything at the landing.
- **Duplicate code** (jscpd): CI's duplicates workflow on every push, and `tools/editmode.sh sweep`; the landing skips it.
- **u2's core-only landings have no Unity check:** the next Unity check or sweep compiles them.
- **Engine-free C# under `unity/` lands without a Unity compile.** `land.sh` asks for `--compiled` (a machine with
  Unity compiled it) for any `.cs` under `unity/` except the files directly in `Chemistry/Tests/` and `Chemistry/Mcp/`
  (its `engine_free` line): `dotnet build core` compiles those the way Unity does (netstandard2.1, C# 9), so the
  landing gates prove them. The package `packages/com.anweather.chemistry-core/` never needed it. A branch that mixes
  them with other Unity C# still needs `--compiled`. What only Unity sees (code behind `#if UNITY_5_3_OR_NEWER`, the
  asmdefs' references) waits for the next Unity check or sweep. The view's plans (`Scripts/Plans/`, step 6 of
  `docs/design/architecture-review.md`) aren't engine-free: dotnet builds them against `core/UnityMath`, a stand-in for
  the few engine types they use, and only Unity proves them against UnityEngine, so they still need `--compiled`.

A failure in the sweep goes to whoever landed the change that broke it, found by bisecting the day's landings.

## Running EditMode (`tools/editmode.sh`)

Measured on the Mac 2026-11-10: a full EditMode run was 1987 tests in 42.6 minutes, and 98% of that (1555 tests, 2476 s)
was `Chemistry.Tests`, the same tests `dotnet test core` runs under .NET (the `core/Chem.*.Tests` projects compile that folder).
Every Chemistry test method the Editor ran also exists in the `dotnet test core` list, so none is Unity-only. The
Unity-only tests (`Prototype.Game.Tests`: `Scripts/Editor/Tests`, `Scripts/Shell/Playtest/Editor`; and `Prototype.Probes.Tests`: `Scripts/Probes/Editor/Tests`) are 424 tests;
with the view plans' 56 (`Prototype.Plans.Tests`: `Scripts/Plans/Tests`, which `core/Prototype.Plans.Tests` also runs under
.NET, so the Editor's run is their Mono pass) the default group is 480 tests in about 4 minutes. So the Editor's default is
that group, and the Mono pass over the Chemistry tests is asked for:

| Command | Runs | Time (Mac) | When |
|---|---|---|---|
| `tools/editmode.sh` (`unity`) | the Unity-only tests (`Prototype.Game.Tests`, `Prototype.Probes.Tests`) and the plans' (`Prototype.Plans.Tests`), ~470 | ~4 min | any change under `Scripts/` or `Net/`; layer 2 |
| `tools/editmode.sh chem` | `Chemistry.Tests` under Mono, ~1570 | ~40 min (PC ~14), background | a change to `Chemistry/`; the sweep |
| `tools/editmode.sh all` | both, as a bare `unity command test run --mode EditMode` | ~45 min | rarely |
| `tools/editmode.sh sweep` | all, plus the Explicit `Timing` tests | ~45 min | the sweep, on an idle Editor |

What the Editor no longer re-proves by default is only that Mono gives the same answer as .NET (the 2026-11-08 rounding
case); `chem` and the sweep keep that. CI's quality workflow still runs every Chemistry test under .NET on every push; the landing gates run the touched
modules' (below).

## The core's categories

Every core fixture is one of two kinds, and a test in each project (`EveryFixtureIsUnitOrSimulation`) fails one that's
neither:

- **`Unit`**: a rule, a condition, an effect, read or applied on at most a small made-up world; tens of milliseconds a
  test. `dotnet test core --filter TestCategory=Unit` runs them alone: 508 tests in 6 s wall on u2 (2026-11-09).
- **`Simulation`**: a world's ticks, chunks, peers, land generation, the rules MCP's workbenches. 1411 tests, 174 s
  wall on u2 without the `Sweep` ones.
- **`Sweep`** stays as it was, on single tests inside Simulation fixtures: the long runs, the sweep's (layer 4).

A fixture's kind was set from what it builds and its tests' mean time in a full run (under 40 ms: Unit). A new fixture
picks one; when a Unit fixture grows a whole-world test, that test (or the fixture) becomes Simulation.

Layer 3 runs Unit and Simulation, all but `Sweep`, in the projects a change reaches, and the `Unit` fixtures alone in
the rest (the smoke: every project's, about 10 s), because the faults that reach dev are Simulation's (two branches'
worlds meeting) and Unit is cheap. `Unit` is the author's
loop (layer 1): run it on every edit, and the nearest Simulation fixture by name.

## Keeping the core suite fast

Measured 2026-11-09 on u2 (2 cores, 6 workers): the whole suite was 6 m 15 s wall and 11 m 54 s of CPU, the 15
slowest tests nearly all Hold runs on the full 576 x 576 valley. The landing suite is now 3 m 21 s wall and 6 m 07 s
of CPU; on a 6-core host that's about half of the 3-5 minutes it was. What made the difference, and what to keep doing:

- **Step only what the test reaches.** `HoldTests.Field` keeps active only the chunks round the castle and the
  spawners (16 of 81): every Hold, Hands and HoldProgress test runs on it, 4 m 26 s of them became 56 s.
- **Build once what a test only looks at.** `HoldProgressTests.OfferedToLook` builds each seed's beaten wave once for
  the fixture; a test that changes the world builds its own.
- **As few ticks as the check needs**: start at the wave that matters, stop at the first sighting.
- **Prove a shortened test still fails** on the bug it guards, by breaking the code once before landing.
- **Replay side by side what only meets at the end.** Fixtures run side by side but a fixture's tests run one after
  another, so a project is never faster than its slowest fixture. `LateJoinTests` replays every joiner and snapshot of
  one host run at once (`Replayed`), and `OnlineValleyTests` runs its host, client and joiner each on its own thread;
  the differences are asserted in the old order afterwards, so the assertions are the same. A world packs and unpacks
  its chunks only on the thread that made it (`World.OnOwnThread`), so **each replay makes its world on the thread that
  runs it**; peers stepped in turn with an assertion between ticks (`GeneratedSourceTests`' two sources) can't be split
  this way. This took `Chem.Lockstep.Tests` from 84 s to 28 s alone on the Mac (2026-11-27).

## The core's test projects, by module

The Chemistry tests (`unity/Assets/Prototype/Chemistry/Tests/`, one assembly in Unity) are split for dotnet into a
project per module (`docs/design/architecture-review.md` step 2): which project compiles which file. The land's
fixtures are split in two since step 5b (`docs/design/chem-land-split.md`): `HillsTests.cs` builds on the land alone,
`HillsWorldTests.cs` holds its tests that run a World.
`core/Chem.Tests.props` holds the lists; Sim takes every file not listed elsewhere, so a new file runs exactly once
(in Sim) until it is listed. Run the touched module's project while working (layer 1):

| Project | Holds | Tests | Wall alone |
|---|---|---|---|
| `Chem.Shared.Tests` | `ChemistryTests` (the rules, folders and rooms every other test uses), `BuiltWorlds` (a map made into a World), the kinds check's base | 87 | 5 s |
| `Chem.Rules.Tests` | rule tables, mods' fields, components, items, looks, fauna, the spell list and radial, player mods and stats | 191 | 8 s |
| `Chem.Land.Tests` | the land alone, on `Chem.Land` (no `Game.Core`): `Hills*`, `ChunkGen`, `WorldPlan*`, biomes, landmarks, themes, map files, the land goldens (`NineGolden`, the hills', `WorldTwelve`'s) | 290 | 76 s |
| `Chem.LandWorld.Tests` | the land on a World: each land fixture's `*WorldTests.cs` half (peers on the hills, bakes, `MapCheck`), `GeneratedSource`, stamps laid, terrain, places, the land benches | 209 (208 without Sweep) | 94 s |
| `Chem.Sim.Tests` | cells, chunks, ticks, spells and combos, things, weather, the `CellHash`/`ChunkHash` goldens | 603 (step 9's Chem.Play: `SaveTests` moved to Play) | 49 s |
| `Chem.Play.Tests` | the Hold (with `HoldLockstepTests`), the hands, golf, the Gauntlet, puzzles, defense, the dragon, saves (`SaveTests`: a saved world plays the defense field); on `Chem.Play` and `Chem.Shared.Tests` | 281 (278 without Sweep) | 47 s |
| `Chem.Lockstep.Tests` | the lockstep session and wire, late joins, rooms, resets, the open world over the loopback | 80 | 110 s (28 s on the Mac since its replays run side by side, 2026-11-27) |
| `Chem.Playtest.Tests` | the playtest bot (`Scripts/Shell/Playtest/Editor/*Tests.cs`) | 41 | 2 s |
| `Prototype.Plans.Tests` | the view's plans (`Scripts/Plans/Tests/*.cs`, on `Prototype.Plans` and the `core/UnityMath` shim; step 6): the tree budget, the menu navigator, pinned ground, tag rules, build versions, the far ring's meshes | 57 | 1 s (the Mac) |

Measured 2026-11-26 on a 4-core cloud VM, Release, 6 workers, `-s core/sweep.runsettings`. The whole of `dotnet test
core` (the eleven projects side by side, the tools' four included) was 380 s there, against 334 s for the one
`Game.Core.Tests` it replaces: more test hosts share the cores. A module alone is the saving, not the whole run.
(The two land rows were measured after step 5b the same way; the whole run, twelve projects, was 393 s.)

```sh
dotnet test core/Chem.Sim.Tests                              # one module
dotnet test core/Chem.Land.Tests --filter TestCategory=Unit  # its quick fixtures
dotnet test core -s core/sweep.runsettings                   # everything, Sweep too (CI, the sweep)
```

The test projects reference each other only for helpers, and each reference is a coupling: every one but Land
references Shared (`ChemistryTests.R`, `Folders`, `Room`), which references `Game.Core`; Land references `Chem.Land`
alone, with its own `LandFolders` (the repo's folders without `ChemistryTests`), so a land test can't use a World or
the rules; LandWorld references Land (`LandmarkIntentTests.Village`, `ChunkGenTests.Nine`, `ScaleIslandTests.CopyInputs`,
`HillsTests.HillsDir`...) and Shared; Sim references Rules (`SpellBalanceTests`), and no longer Land (5b: what it used,
`SparseWorldBench.Build` and `GameWorldDir`, is `BuiltWorlds.Build` and `ChemistryTests.GameWorldDir` in Shared, and the
mod-switch bench, which bakes the nine, is LandWorld's); Lockstep references Sim (`ComboTests.Blank`). Where a file sits away from its subject, that is why: `HeightfieldThreadingTests` is in Sim
(it uses `FogTickBench`), `HoldLockstepTests` in Play (it uses `HoldTests`), `AirTests` in Play (`HoldTests.Field`).
`Rules.Mcp.Tests` references Sim (`ComboTests.Blank`, `BuildingTests`); the other tools' tests only Shared. `Prototype.Plans.Tests` references `Prototype.Plans`
alone (with its own `PlansFolders` and kinds check), not Shared.
There is no Chemistry Mcp project: no file in `Chemistry/Tests/` is chiefly the rules MCP's (`PlayerModsTests` drives a
`Workbench` for player mods, and sits in Rules), so the Mcp module's tests are `core/Rules.Mcp.Tests`.

## The landing gates, by module

`gates.sh` runs the core tests a change can reach, not all of them (`docs/design/architecture-review.md` step 3). The map
is `tools/workers/modules.json`; `tools/workers/modules.mjs` reads it with `core/Chem.Tests.props` and the
`core/*/*.csproj` files:

- A changed path (`git diff origin/dev...HEAD`, plus what isn't committed) belongs to the first module whose globs match
  it; the Chemistry test files go by the props' lists (`ChemTestsLand` is the `tests-land` module), read from the props,
  never copied.
- A module's `projects` are code: they run, and so does every project that references them, transitively. A library
  runs its dependents, so `Game.Core` (the whole Runtime, `Chemistry/Mcp`, `Playtest/Logic`) runs everything. A
  module's `reads` are data: `rules/` and `world/` run every project that loads the rules (not `Playtest.Runner.Tests`),
  `docs/GLOSSARY.md` and `docs/MODDING.md` the two that read them, any `.cs` under `unity/Assets/Prototype` runs
  `Rules.Mcp.Tests` (`HiddenThingTests` reads them all).
- The rest's `Unit` fixtures run as the smoke (about 10 s for all eleven projects on the 4-core VM).
- Docs and tools alone run no dotnet tests (the build and format still run, and the node tests always do).
- **A path in no module runs everything**, as do a map that doesn't load, no changes found, and `GATES_ALL=1`. So do
  core's shared files (`Chem.Tests.props`, `Directory.Build.props`, the runsettings), which are in no module on purpose.
- `node tools/workers/modules.mjs check` (a workers test, so every landing runs it) fails when a file a project compiles
  wouldn't run that project by the map; `node tools/workers/modules.mjs choose <path>...` says what a change would run.

**The land is its own assembly; the rest of the Runtime is one** (step 5, `docs/design/chem-land-split.md`).
`Runtime/Land/` is `Chem.Land`; every other `Chem.*` project links the rest of `Runtime/` through `Game.Core`, which
references `Chem.Land`. So a Runtime change above the land runs every project but `Chem.Land.Tests` (which builds on
`Chem.Land` alone); the map's other Runtime areas (`runtime-rules`, `runtime-play`...) say in `after` which project
each would narrow to as an assembly of its own.

**The game modes are their own assembly too** (step 9's leftover, 2026-11-27): `Runtime/Play/` is `Chem.Play`, above
`Game.Core`, and only `Chem.Play.Tests`, `Hold.Runner` (with `Hold.Runner.Tests`) and `Playtest.Text` reference it. So
`runtime-play` is `"projects": ["Chem.Play"]` with no `stop`: a Play change runs `Chem.Play.Tests` and
`Hold.Runner.Tests` and gives the rest the smoke, because nothing below `Chem.Play` can reach it. The modes' state, its
save and the seam (`World.Play.cs`, `IWorldModes.cs`) are Sim's and run everything. On the Mac (warm build, `gates.sh`
wall): a Play file (`Runtime/Play/CourseSystem.cs`) 177 s before, 52 s after; a Sim file (`Runtime/World.cs`) 160 s
before, 169 s after (the same set; noise).

**The `stop` rule (Andrew, 2026-11-26).** A Land change (`runtime-land`: `Runtime/Land/**`) runs `Chem.Land.Tests` and
`Chem.LandWorld.Tests` (the land on a World), and everything above `Game.Core` gets only the `Unit` smoke: the module's
`"stop": ["Game.Core"]` reaches `Game.Core` but doesn't follow its dependents. Sim, Play, Lockstep and the MCP do use
the land, so this is a judgment, not a proof: the land goldens (`NineGolden`, the hills', `WorldTwelve`'s) are in the
land's own tests, `TerraformTests`' `CellHash` goldens are `Unit` (in the smoke), and **the full suite still runs in
CI after every push to dev**, which catches what the cut lets by. `modules.mjs check` takes the same cut (a project
past a stop isn't required of a file in that module, but a test project that compiles the file always is), and
`gates.test.mjs` tests it. A Land test file runs Land and LandWorld (LandWorld references Land), not Sim. The adapters
onto a world (`World.Land`, `GeneratedSource`, `MapBake`, `MapCheck`...) are Sim's and run everything.

When dev moves during the gates, `land.sh` merges again and reruns the gates only if dev's new commits reach what this
landing tested (`modules.mjs overlap`: a test project both run, a tested module both touch, or either running
everything); otherwise it pushes.

Measured 2026-11-26 on the 4-core cloud VM (build and format included, warm build cache):

| Change | Core tests run | `gates.sh` wall |
|---|---|---|
| docs only | none | 1 m 00 s |
| a Land test file (`ChunkGenTests.cs`, `HillsRoadTests.cs`) | Land, Sim, Lockstep, `Rules.Mcp.Tests` + the smoke | 6 m 07 s |
| a Runtime file (`HillsRoads.cs`) | all | 7 m 20 s |
| after step 5 (5c, the same VM): a Land runtime file (`Runtime/Land/HillsRoads.cs`) | Land, LandWorld + the smoke | 3 m 48 s |
| after step 5: a Land test file (`ChunkGenTests.cs`) | Land, LandWorld + the smoke | 3 m 45 s |
| before this step (every change: all, plus jscpd) | all | 7 m 21 s |
