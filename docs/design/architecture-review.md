# 5e: architecture review and decoupling plan

Approved by Andrew 2026-11-26 (steps 1-3 started as cloud jobs). Written 2026-11-25. Read from `origin/dev` at 6f86730c with `git show` and `git ls-tree`; nothing built, run or
changed. Counts come from `git grep` and `wc -l`, so treat them as rough sizes. Pattern followed: the boat game's
architecture review (in the boatgame repo, branch u1-sailing-test) and the steps that came out of it.

Goal (Andrew, 2026-11-26): every piece built and tested on its own with little coupling, so a feature lands as
fast as it did at the start, without the test and verification load we carry now.

---

## 1. What the boat game did, and what carries over

| Boat game | What it bought | In 5e |
|---|---|---|
| A measured review first: core share per module, a module-to-module matrix, the cycles, the biggest files | The order of the work came from numbers, not taste | This draft (sections 2-3) |
| `core/World/World.csproj` *links* the game's own plain files, plus a 646-line `core/UnityMath/` shim (Vector2/3/4, Quaternion, Mathf, Color, a Netcode stub) | Plan code that uses Vector3 builds and tests in dotnet with no rewrite | **Missing.** 5e's core is engine-free by asmdef (`noEngineReferences`), which is stronger, but view plans that use Unity maths have nowhere to go |
| Ports only where there are 2+ real adapters (`IBody`, `ISeaSurface`, `ICrew`, the clock, the save store, the network's "am I the keeper") | Little ceremony, real seams | 5e has one good one already: `LockstepPorts.cs` (transport and world port, adapters NGO, Punchy, loopback) |
| B169: one `Seeded` hash in place of ~25 copies, every caller's bits kept, proved over 10^5 inputs | Fewer copies, the world unchanged | 5e already has one seeded RNG in `World`; the lesson is the proof method (bit-equality, then delete) |
| B172: the weather's rules out of the `Sky` MonoBehaviour into the core; B173: the save as a plain snapshot the core tests | Rules tested in seconds; the MonoBehaviour keeps only the drawing | The same move fits 5e's mesh builders and HUD logic (section 3, item 8) |
| T17: `dotnet test core` skips the `Long` category by default (`default.runsettings`): 109 tests, 7 min down to 40 s; `long.runsettings` for the rest | The author's loop fast again | 5e's `Unit` filter (508 tests, 6 s) is the same idea, but gates and CI still run the whole suite |
| T16: `QuickRun`, the game's own boat on the core's sea without Unity, its numbers set beside the Unity run's | Whole-scenario checks with no Editor | 5e has `Hold.Runner` and `Playtest.Runner`; no probe has a dotnet twin yet |
| Cloud builders took B169, B172, B173 (dotnet only); the PC ran the slow checks once for a batch of three | Heavy checks paid once per batch, not per change | 5e's `land.sh` refuses any `unity/**.cs` without a Unity compile, so cloud jobs can't land most core work (section 3, item 5) |

What does not carry: boat had no asmdefs at all; 5e already has a core package and lockstep ports, so 5e starts
further along on the core and further behind on the view and the gates.

---

## 2. 5e's module map today

| Assembly | Where | Files / lines | References | Built and tested without Unity? |
|---|---|---|---|---|
| `Chemistry` (namespace `Chem`) | `packages/com.anweather.chemistry-core/Runtime` | 178 / 48.5k | Newtonsoft only; `noEngineReferences: true` | Yes: `core/Game.Core` (netstandard2.1, C# 9, same as Unity) |
| `Chemistry.Mcp` | `unity/Assets/Prototype/Chemistry/Mcp` | | Chemistry | Yes, linked into `Game.Core` |
| `Chemistry.Tests` | `unity/Assets/Prototype/Chemistry/Tests` | 124 | Chemistry, Mcp, NUnit | Yes: `core/Game.Core.Tests`, ~1,900 tests, one project |
| `Assembly-CSharp` (no asmdef) | `unity/Assets/Prototype/Scripts` (485 files in one flat folder), `Net/` (47), `Playtest/` | ~590 / ~100k, of which 159 `Probe*.cs` = 28.8k | everything, Netcode, Starter Assets by reflection | Only `Playtest/Logic/*.cs` (6 files, linked into `Game.Core`) |
| `Assembly-CSharp-Editor` | `Scripts/Editor`, `Editor/Tests` (62 files), `Playtest/Editor` | | everything | No: ~440 tests, ~4 min in the Editor (Mac) |
| `core/` runners | `Hold.Runner`, `Playtest.Runner`, `Rules.Mcp`, `Chat.Bench` (+ tests) | 5 test projects | Game.Core | Yes |

Direction today: rules JSON → Chemistry → (Mcp) → Assembly-CSharp (view + Net + probes) → Editor. Only the
first arrow is enforced by a compiler. Inside Assembly-CSharp, "the view reaches networking only through its
doors" (`Scripts/CLAUDE.md`) is a convention, and 45 view files name Net types.

---

## 3. The worst couplings, with evidence

1. **The whole view is one assembly.** `Scripts/CLAUDE.md`: "This folder has no `.asmdef`: it compiles into
   `Assembly-CSharp`, so CI can't build it." 650 files, 113k lines, 485 of them in the folder's root. Any edit
   recompiles all of it; nothing stops a cycle; the only checks are the console's warnings and the probes.
2. **The probes live inside the game.** 159 `Probe*.cs` files, 28.8k lines (25% of `Scripts/`), compile into
   `Assembly-CSharp` beside the code they test. `ProbeScenarios.cs` is the most-changed code file on dev: **173
   commits in 30 days**, one registry every new probe edits (a merge-conflict hub). Probes reach
   `NetworkManager.Singleton` directly (`ProbeAirPeers.cs:27,66,121`, `ProbeFaunaResize.cs:81`,
   `ProbeGlowMoss.cs:59`).
3. **ChemGrid is the hub of the view.** 21 partial files, 5.1k lines (`ChemGrid.cs`, `.Visuals`, `.Effects`,
   `.Shaping`, `.Rules`, `.Movers`, `.Whirls`, ...), named in **211** other view files, `ChemGrid.Current` read 86
   times (`ChemGrid.Current.cs:26`). `ChemGrid.cs`, `ChemGrid.Visuals.cs` and `ValleyChemistry.cs` were changed
   65, 51 and 68 times in 30 days. The narrow face `CurrentGrid` (world, rules, effect door) is the right start:
   most readers need only that.
4. **The core is one assembly with a God object.** `World` is 44 partial files, 14k lines: cells and ticks, but
   also Hold, Golf, Course, Dragon, Raiders, Defense, Weather, RoomSky, Save, Pack (`World.Hold.cs` ...
   `World.Weather.cs`). One `Chemistry` asmdef, one `Game.Core.Tests` (~1,900 tests, 3 m 21 s wall on u2 without
   `Sweep`, `docs/TESTING_LAYERS.md`), so a change to a road's grade runs the dragon's tests. Land generation is
   already split-ready: none of `Hills*.cs`, `ChunkGen*.cs` (10 files) names `World`, and only `WorldPlan.cs` of
   the eight `WorldPlan*` does.
5. **The landing gate treats engine-free C# as Unity C#.** `tools/workers/land.sh:68` and `:125` refuse any path
   matching `^unity/.*\.cs$` without `--compiled`. `Chemistry/Tests` and `Chemistry/Mcp` are under `unity/` but
   are compiled by `dotnet build core` as netstandard2.1 / C# 9 (`core/Directory.Build.props`), so a core-only
   test change still waits for the PC. Then `gates.sh` runs the whole core suite, format, jscpd and four node
   suites for **every** landing, whatever it touched, and `land.sh` reruns them up to three times
   (`for try in 1 2 3`) when dev moved, which at **2,856 commits in 30 days** it often has.
6. **The view holds the network directly.** `SpellCaster.cs:39` (`NetChem net`) and `:373`,
   `RuleSwitcher.cs:26,44`, `PhysicsProp.cs:56` (`NetProp net`), `ItemHolder.cs:188` (`NetChem.PeerName(0)`);
   45 view files name a Net type; `IsSpawned`/`IsServer`/`IsHost` 28 times in 19 view files;
   `NetworkManager.Singleton` 49 times in 19 files, 15 outside `Net/`. Solo play still compiles and loads Netcode
   paths, and nothing in the view can be tested without NGO.
7. **Scene searches and singletons as the wiring.** 244 `Find*ObjectByType` calls in 131 non-Editor files (29 for
   `ValleyFauna`, e.g. `Footprints.cs:63`; 15 `PhysicsProp`; 14 each `ValleyChemistry`, `ThingSpawner`);
   `PauseMenu.Instance` 64 reads plus `PauseMenu.AddSection` (`PauseMenu.cs:89,117`) as the menu registry;
   `MapLand.Current` 37 (`MapLand.cs:32`), `MapTerrains.Instance` 31 (`MapTerrains.cs:40`), `FarRing.Instance`
   19 (`FarRing.cs:28`); `EventLog.Add` 188 calls. Each one is a hidden dependency a test has to build a scene for.
8. **Plans and numbers buried in MonoBehaviours.** Lines that touch a Unity API (mesh, material, transform,
   terrain, camera, time) against file size: `RowMesher.cs` 34 of 922, `CellWaterMesh.cs` 41 of 1,204,
   `ValleyGround.cs` 21 of 1,189, `FarRing.cs` 67 of 543. About 20 `Scripts/` files use no Unity at all
   (`TreeBudget`, `CastSoundTable`, `CellsByChunk`, `GroundFromLand`, `StepTimes`, `SwimStyle`, `MenuNavigator`,
   `CodePicker`, `StartTrees`, `PinnedGround`, ...) yet build only in Unity. 31 of the 62 `Editor/Tests` files
   (`BuildingMesherTests`, `ContourSurfaceTests`, `FarMeshesTests`, `MenuNavigatorTests`, `PadGlyphsTests`,
   `Creature*Tests`, `TagRulesTests`, ...) touch no scene or asset API, but run only in the 4-minute EditMode pass.

What is good, and must stay: the engine-free core enforced by the compiler, deterministic by design (integer
state, the world's seeded RNG, ordered chunk iteration, `CellHash` goldens); the lockstep ports; `ChemEffects`
as the one door from physics into cells; the `Unit`/`Simulation`/`Sweep` categories and the editmode split.

---

## 4. The target

**Modules, each its own assembly and its own test project that builds alone.** Arrows point at what a module may use.

| Module | Assembly (Unity) / project (dotnet) | Holds | Uses |
|---|---|---|---|
| Rules | `Chem.Rules` | `RuleTables`, `Rules.*`, mods, `ModValidator`, `RuleSet`, schema | Newtonsoft |
| Land | `Chem.Land` | `Hills*`, `ChunkGen*`, `WorldPlan*`, themes, map files, `MapCheck`, noise | Rules |
| Sim | `Chem.Sim` (today's `Chemistry`) | `World` data, cells, chunks, tick, react, spells, things, `TickBatch` | Rules, Land |
| Play | `Chem.Play` | Hold, Golf, Course, Raiders, Defense, Dragon as systems on `World` (D9) | Sim |
| Lockstep | `Chem.Lockstep` | `LockstepSession`, wire, channels, ports, loopback | Sim (through the world port) |
| Mcp | `Chemistry.Mcp` | the rules MCP | Rules, Sim |
| View plans | `Prototype.Plans` (Unity: references UnityEngine; dotnet: links a `core/UnityMath` shim, boat-style) | mesh plans (`RowMesher`, water, far ring), budgets, HUD and menu logic, input as data | Sim, Play |
| View | `Prototype.View` | MonoBehaviours that draw, play sound and read devices | Plans, Sim; a `IPeers` port, never Net |
| Net | `Prototype.Net` | NGO adapters: `NetChem`, `NetProp`, `NetItem`, `NetWizard`, the `IPeers` adapter | View, Lockstep |
| Probes | `Prototype.Probes` (runtime, dev builds) | every `Probe*.cs`, scenarios registered by attribute | anything; nothing uses it |
| Editor | `Prototype.Editor` | builders, `Editor/Tests` that need scenes | anything |

**Ports only at seams with two real adapters:**
- `IPeers` (am I solo, host or client; my peer name; send this intent): solo adapter and NGO adapter. Removes the
  `NetChem` fields in `SpellCaster`, `RuleSwitcher`, `PhysicsProp`, `ItemHolder` and the scattered `IsServer`.
- The world port and transport (already there, `LockstepPorts.cs`).
- `CurrentGrid` grown into the view's one way to the world (world, rules, effect door, chunk events); drawing
  code takes the grid it is given, not `ChemGrid.Current`.
- Plain registries in place of scene searches (`ValleyFauna`, `PhysicsProp`, `ThingSpawner` join on enable);
  the menu's sections as data a page asks for. No interface per singleton, no DI container, no event bus.

**A Unity-free home for every plan and number:** a MonoBehaviour keeps `Update`, the `Mesh`/`Terrain` calls and
the scene; the plan (arrays, counts, choices) is plain C# in Plans or the core and is tested in dotnet.

**Constraints that hold throughout:**
- Lockstep determinism: integer state in the core, the world's seeded RNG only, ordered iteration, no Unity in
  shared state (D4, D9). Every move that touches the core keeps the `CellHash`/`ChunkHash` goldens byte for byte;
  a moved hash or RNG call is proved by bit-equality before the old one goes (the B169 method).
- J-086, cost follows the player: no new port or registry loops over every chunk or `World.Cells`; anything that
  reacts to a switch, a look or a save works on active chunks (and drawn chunks for looks) and catches up on
  activation. A module's API is keyed by chunk.
- Plans run as Mono in Unity and .NET in dotnet: keep the Mono pass (`editmode.sh chem`) in the nightly.

---

## 5. Test tiering

A change runs its own module's tests plus a short smoke; the heavy gates run once a night or before a release.

| Tier | When | Runs | Target time |
|---|---|---|---|
| 0. Author | every edit | the touched module's test project, `Unit` first (`dotnet test core/Chem.Land.Tests`) | seconds |
| 1. Landing | `land.sh`, per branch | `dotnet build` of the touched projects and their dependents (warnings fail), format of changed files, the tests of touched modules **and modules that use them** (from a path-to-project map), plus a fixed smoke: the `CellHash`/`ChunkHash` goldens, one two-peer lockstep run, `EveryExampleModPassesTheChecks`. A view change adds: a Unity compile, the EditMode tests of its own assembly, `probe smoke`. A retry after dev moved reruns only if dev's new commits touch the same modules | under 2 min core, under 5 min view |
| 2. Nightly sweep | the idle Unity machine, and after a big batch | everything: full `dotnet test core` with `Sweep`, full EditMode, `editmode.sh chem` (Mono), every probe, two-peer runs, the hitch budget, jscpd | hours, unattended |
| 3. Release | `docs/RELEASING.md` | `smoke`, `online`, the release probes, install from the draft | as today |

CI (`quality.yml`, `duplicates.yml`) keeps running the whole dotnet suite and jscpd after every push: it costs
nobody's loop and stays the net between nightlies. A nightly failure goes to the day's landings by bisect, as
`docs/TESTING_LAYERS.md` layer 4 already says.

---

## 6. Steps, by payoff over risk

Each lands alone. "Cloud" means dotnet and node only, no Unity, no secrets. "Unity" means a machine with the Editor
must compile it (one compile can serve a batch of steps, as the boat's PC did for B173, T21, B175).

1. **Let dotnet's build count as the compile for engine-free C#.** `land.sh:68,125`: paths under
   `unity/Assets/Prototype/Chemistry/{Tests,Mcp}/` (and later every engine-free asmdef folder) no longer need
   `--compiled`; `dotnet build core` proves them (netstandard2.1, C# 9, `noEngineReferences`). Plus a test in
   `land.test.mjs`. Payoff: cloud jobs can land all core work. Risk: low (the nightly Unity compile catches a
   miss). **Cloud.**
2. **Split `Game.Core.Tests` into per-module test projects** (Rules, Land, Sim, Play, Lockstep, Mcp, Playtest) by
   `Compile Include` globs over the same files; no code moves. A test that lands in the wrong project fails to
   build, which is the coupling made visible. Also make plain `dotnet test core` skip `Sweep` by default (a
   `default.runsettings`, boat's T17), with `sweep.runsettings` for the nightly and CI. **Cloud.**
3. **Gates by module.** A path-to-project map (a new modules.json beside gates.sh) and `gates.sh` running the touched
   projects, their dependents and the smoke set; jscpd leaves the landing (CI and the nightly keep it);
   `land.sh`'s retry reruns only when dev's new commits share a module. Tests for the map in `gates.test.mjs`.
   Update `docs/TESTING_LAYERS.md`. **Cloud.**
4. **Probes into their own assembly.** `Prototype.Probes.asmdef` over the `Probe*.cs` files (moved to
   `Scripts/Probes/`), referencing the game; `ProbeScenarios` built from a `[ProbeScenario]` attribute so a new
   probe touches only its own file. Payoff: a quarter of the view out of the game's compile, the 173-edit hub
   gone. Risk: low-medium (the probes reach internals; some `internal` becomes `public` or an `InternalsVisibleTo`).
   **Unity.**
5. **Land out of the core into `Chem.Land`.** `Hills*`, `ChunkGen*`, `WorldPlan*`, themes, map files into a
   second asmdef in the package and a `Chem.Land` dotnet project with its own tests. The hills work (the most
   active area) then builds and tests alone. Proof: the land goldens and `WorldTwelveTests` unchanged. **Cloud**
   for the dotnet side; **Unity** once to confirm the package's second asmdef.
   *2026-11-26: stopped at the dependency map, `docs/design/chem-land-split.md`: the runtime splits once
   `GeneratedMap`'s bake moves to Sim, but most Land tests also run a World, and the gates would still run
   everything. That file proposes 5a-5c.*
   *5a done 2026-11-26 (cloud): `Runtime/Land/` is `Chem.Land` (asmdef and `core/Chem.Land`), `GeneratedMap` in it
   and its bake in Sim (`MapBake`); the sweep's 2263 results unchanged by name. Awaits a Unity compile.*
   *5b done 2026-11-26 (cloud): `Chem.Land.Tests` builds on `Chem.Land` alone (290 tests); the land fixtures' World
   halves and the land-on-world files are `Chem.LandWorld.Tests` (209); Sim no longer references Land.*
   *5c done 2026-11-26 (cloud). **Andrew's ruling (2026-11-26): yes to the `stop` rule.** A Land change runs
   `Chem.Land.Tests` (and the land-on-world tests, `Chem.LandWorld.Tests`) plus the Unit smoke for everything above
   `Game.Core`; the full suite still runs in CI after every push to dev. modules.json's `runtime-land` is
   `"projects": ["Chem.Land"], "stop": ["Game.Core"]`; `modules.mjs check` takes the same cut. The gates for a Land
   runtime file went from 7 m 20 s to 3 m 48 s, for a Land test file from 6 m 07 s to 3 m 45 s
   (`docs/TESTING_LAYERS.md`). Step 5 is done but for its Unity compile.*
6. **View plans in plain C#.** A `core/UnityMath` shim (Vector2/3, Mathf, Color: only what the plans use) and a
   `Prototype.Plans` assembly: first the 20 Unity-free `Scripts/` files and the subjects of the 31 plain
   `Editor/Tests` files, with those tests moved to a dotnet project; then the plan halves of `RowMesher`,
   `CellWaterMesh` and `FarRing` (arrays out, the `Mesh` call stays). Each file moves with its test. **Cloud** for
   the plain files; **Unity** to compile the asmdef split, batched.
   *Done 2026-11-26 (the Mac, with a Unity compile): `Scripts/Plans/` is `Prototype.Plans` (Unity: Chemistry, Chem.Land
   and UnityEngine's maths; the game, its Editor tools, tests and the probes reference it) and `core/Prototype.Plans`
   (the same files on the `core/UnityMath` shim: Vector2/3, Mathf, Color/Color32, Debug.Log, the
   RuntimeInitializeOnLoadMethod mark). 21 files moved (`TreeBudget`, `CastSoundTable`, `CellsByChunk`, `SwimStyle`,
   `MenuNavigator`, `CodePicker`, `StartTrees`, `PinnedGround`, `FrameStats`, `RuleIds`, `WeatherMods`, `GroundSwitch`,
   `Credits`, `TagRules`, `TreeImpostorMath`, `ProceduralCast`, `SoundMastering`, `DragonFlight`, `BuildVersion`,
   `LoadedMaps`, `FarMeshes`), with 6 test files (56 tests) to `Scripts/Plans/Tests/`, which `core/Prototype.Plans.Tests`
   runs and the Editor's default EditMode group still runs under Mono. The far ring's plan half is `FarMeshes` (its
   one `Mesh` call is `FarMeshesMesh.ToMesh`, in the game). Left in place because each names a game type or an
   engine API past the shim: `GroundFromLand` (`ValleyGround`), `StepTimes` and `BiomeGround` (`StartupTimeline`),
   `SpawnPoints` (`ChemGrid`), `PairGlyphs` (`HudDraw`), `CastBeat` (`CastFeel`, a ScriptableObject), `ApiStandIn` and
   the plain `Net/` files (Net's, for step 7), `CreatureBodies` and `FolkJobs` (Punchy), `BuildingMesher` and
   `ContourSurface` (meshes, materials); `PadGlyphs`, `PadLog` and `MenuControls` read the Input System. `RowMesher`
   (`MeshKit`, `WindowPanes`, its result holds `Mesh`es) and `CellWaterMesh` (built on a `ChemGrid`, materials and
   GameObjects) need a real split, not a move, so they wait; `FarRing` is a MonoBehaviour.*
7. **An `IPeers` port and a `Prototype.Net` assembly.** Solo and NGO adapters; `SpellCaster`, `RuleSwitcher`,
   `PhysicsProp`, `ItemHolder` and the 19 files with `IsServer`/`IsSpawned` go through it; then `Net/` becomes its
   own asmdef so a view-to-Net reference is a compile error. Proof: `probe online`, `roomopen`, `roomruin`, the
   core's lockstep tests. Risk: medium (the order of sends). **Unity.**
   *Done 2026-11-26 (the Mac: Unity batch compiles, EditMode tests, Win64 and Linux64 player-script compiles; `probe
   online`, `roomopen` and `roomruin` not run, they need an open Editor). The port: `IPeers` (`Role` solo, host or
   client; `Me`, the room name; `Player`, the core's; `CaughtUp`; `Resetting`; the sends `RequestCast`,
   `RequestCombo`, `RequestRoom`, `RequestStrike`, `RequestSwing`, `StampEffect`, `RequestDefense`,
   `RequestStrikeCommand`, `StampFound`, `SummonDragon`, `StampReset`), found with `Peers.Of(grid)` (or `Peers.InPlay`
   for the grid in play); adapters `SoloPeers` and `NetChem` (`NetChem.Peers.cs`, solo until its session is up, its
   sends its own methods). Nothing installs it: NetChem is a component on the grid. `SpellCaster` (and `.Combos`),
   `RuleSwitcher`, `ItemHolder`, `ChemEffects`, `DefenseField`, `WizardStrike`, `ThingSpawner`, `PerfScenario`,
   `OnlineReset`'s doors and `ChemGrid`'s `Movers`, `Whirls`, `Triggered`, `Rules`, `LookScopes` go through it, each
   check and send in its old place and order. The assembly: `Scripts/Net/` is `Prototype.Net` (`NetChem`,
   `NgoLockstepTransport`, `NetSession`, `NetCommandLine`, `NetTag`, `NetDemo`, `NetDemoDirector`, `ResetCountdown`,
   the countdown HUD that was `OnlineReset`'s); the game doesn't reference it (`PeersTests.TheGameDoesNotReferenceNet`),
   the Editor tools, probes and tests do. Moved out of `Net/` with their `.meta`s: the chat, Claude, rules MCP,
   speech and API-key files to `Scripts/Chat/` (they never named the session), and `NetWizard`, `NetProp`, `NetItem`,
   `NetCourt` to `Scripts/Online/` in the game, as the network halves of their prefabs (their three uses of
   `NetChem`, `OnlineReset.Happened`, the names by client id and `StampFound`, now go through the port). Two things
   changed beyond routing: `OnlineReset.Summon`/`Ask` and `PerfScenario` find the peers through `ChemGrid.Current`
   instead of a scene search for `NetChem` (the same object in every scene that has one), and `OnlineReset.cs`'s
   `.meta` stayed with the MonoBehaviour (`ResetCountdown`), so the new static `OnlineReset` has a new GUID (nothing
   serializes it). `ThingSpawner`'s `NetworkManager.IsListening` checks are as they were.*
8. **Registries for the scene searches, and a narrower `ChemGrid`.** `ValleyFauna`, `PhysicsProp`, `ThingSpawner`
   lists joined on enable (chunk-keyed, J-086), in place of the 244 `Find*` calls; the menu's sections as data;
   readers of `ChemGrid` that only need world, rules and effects take `CurrentGrid`. Done file by file as work
   passes through. Risk: low each. **Unity.**
   *Done 2026-11-26 (the Mac: Unity batch compiles, EditMode tests 559 (557 passed, 2 skipped; 480 before, the 79 new
   are `RegistryTests`), Win64 and Linux64 player-script compiles; the probes not run, they need an open Editor). The
   registries: `Registry<T>` (`Registry.cs`), joined in Awake and left in OnDestroy, handing out what Find* did (active
   GameObjects only, a disabled component included, never a destroyed one), all emptied as Play starts
   (`Registries.ForgetAll`, J-023). `ValleyFauna`, `ValleyChemistry`, `ThingSpawner`, `SkyLight`, `WeatherFx`,
   `DefenseField`, `NetSession` (`InPlay`); `PhysicsProp`, `SpellCaster`, `Carryable`, `Village`, `NetWizard`,
   `SpellProjectile`, `FrostBeam`, `MeteorFall` (`All`, and `AllByInstanceId` where a caller sorted by instance id);
   `Seat` already kept its own list. `PhysicsProp` is also keyed by chunk (placed every physics step; `Near` reads
   only the chunks asked for, a ring around them and the props in no chunk), and BallCourt's ball and the playtest
   bot's nearest prop ask by chunk (J-086). Non-Editor `Find*` calls went from 233 in 128 files (today's dev; 244 when
   this was written) to 108 in 74; what's left is one or two per type (`Light` 8, `VillageHouse` 5, which searches
   inactive objects too, `MapTerrains` 5, ...) and the searches that also run in the Editor (`Village`'s sky,
   `LandBaker`). The narrower `ChemGrid`: `CloudSky`, `SkyLight` and `Village` keep `ChemGrid.Current`'s face, not the
   grid, and two reads (`MapTerrains`, the map-switch probe) skip `.Grid`; the other readers draw on the grid (cell
   positions, heights, events) or take it by `GetComponent`, and the serialized `ChemGrid grid` fields are set by the
   builders, so they stay. The menu's sections as data is not done: `PauseMenu.AddSection` takes IMGUI draw
   callbacks (21 of them), so making them data means rewriting each section's drawing, not a mechanical move.*
9. **Game modes out of `World`.** Hold, Golf, Course, Raiders, Defense, Dragon from `World.*.cs` partials into
   systems over `World` data in `Chem.Play` (D9), one mode per step, each with its goldens unchanged and the Mono
   pass in that night's sweep. Largest step; do it as each mode is next worked on. **Cloud.**
   *Done 2026-11-27 (cloud), one commit a mode on `cloud/play-modes-9`; awaits a Unity compile and that night's Mono
   pass. Chem.Play was a folder, `Runtime/Play/`, in the Chemistry assembly, not its own asmdef (it is now: the next note): World owned and ticked
   the systems, so a Play assembly over Sim would be a cycle until the tick calls them through a seam. Each system is
   made in both World constructors (World.cs, OpenWorld.cs) and runs at its old place in TickStage.Rest or the dragon
   stage; World keeps thin forwarders (`World.Play.cs`) only for what Scripts, the probes, Mcp and Hold.Runner call.
   The 2241 dotnet results (Sweep included) unchanged, every golden unchanged, no file under Scripts/ touched.*
   *Chem.Play an assembly (2026-11-27, the Mac, branch `play-assembly`): `Runtime/Play/` is `Chem.Play` (its asmdef;
   `core/Chem.Play`), above `Chemistry`, which doesn't reference it. The seam is `IWorldModes` (Sim): the tick's Rest
   stage after the raiders (`TickAfterRaiders`: the defense field, then the Hold, as before), a combo's Moment
   (`StartMoment`), `Apply(DefenseCommand)` and `Apply(CourseCommand)` (what `World.ApplyDefense`/`ApplyCourse` and the
   tick batch call), and `MadeFor(world)`, the same modes for another world. It holds behaviour only: the modes'
   state is the world's data (D9) and stays in Sim with its text, hash and save (`World.Play.cs`: the Hold's site,
   field and road, run, plate and battered walls; the defense state and its hash, the join snapshot's "defense" part;
   the spell zones with `ZoneAgainst`, the course and its runs; `WaveRoll`, `Flying`), so a save and every checksum
   are the same code as before, with or without the modes. A world gets them once with `PlayModes.Install(world)`
   (Chem.Play; the view's `ChemGrid.SourceWorld`/`MapWorld`, `Hold.Runner`, `Playtest.Text`, the Play tests), and a
   joiner's world takes them from the one it replaces (`World.InstallModesOf`, in `LockstepWorld.Load` and
   `ChemGrid.LoadSnapshot`), so every peer runs the same systems in the same order; no statics, no registration
   order. A world without them is the chemistry alone, and throws where a mode would have changed something (a
   running defense field or Hold, a stand on the Hold's plate, a Moment with a site, a start command): proved by
   `HoldTests.AWorldWithoutItsModesWontQuietlySkipTheHold`, `AWorldMadeToPlayTicksTheHold`,
   `AJoinersWorldGetsTheModesOfTheOneItReplaces`, and `PlayModesTests` (EditMode) for the game's start world.
   Stayed in Sim, and why: `RaiderSystem` (spells, strikes, reactions, flashes, waves, boulders, buildings and the
   save all reach raiders), `DragonSystem` (the world's reset, run by the change audit in the dragon stage, its state
   in the save and every join and lockstep hash), `GolfSystem` (a ball is an act anyone places, spells move it, the
   dragon puts it back, holing it is a trigger, and the MCP's rooms, compiled into Game.Core, place it), the modes'
   data files (`Golf.cs`, `Course.cs`, `HoldRun.cs`, moved up from Play/ with their GUIDs) and `SetHold` (a map's
   site, laid by `GeneratedSource.MapWorld` like its buildings). Forwarders moved to Chem.Play: `world.Play()` (the
   `PlayModes`: `.Hold`, `.Defense`, `.Course`, which were `world.HoldSystem` and so on) and, as extension methods,
   `HoldWarning()`, `HoldRespawn()`, `HoldNextWaveIn()` (properties before), `HoldNext`, `PathCostAt`,
   `CourseRespawn`; `World.HoldWaveSize`/`BarricadesOf` are `HoldSystem.WaveSize`/`BarricadesOf`. The data reads
   (`world.Hold`, `HoldSite`, `HoldRunning`, `HoldLeft`, `Defense`, `DefenseLeft`, the dragon's and golf's) stay on
   the world. `SaveTests` (its played world runs the defense field) moved to `Chem.Play.Tests`, names unchanged.
   Proof: the sweep (`-s core/sweep.runsettings`) has dev's 2333 results by name and outcome (2298 passed, 35 not
   run), plus the three new HoldTests; `SaveTests`' 33 count in Play now. Every golden unchanged, and the same samples on dev and the branch (`HoldRun` bots, CellHash,
   CastHash, RngState and the dragon's hash): 24 seeded Hold runs (four hand sets, one and two peers, three seeds)
   every 20 s up to wave 10, 580 lines, and four dragon runs (seeds 3 and 8, summoned and by a budget of 400, up to
   two flights each over a running Hold, raiders downed) every 2 s for 240 s, 480 lines. Gates (the Mac, warm): a Play file (`Runtime/Play/CourseSystem.cs`) 177 s -> 52 s
   (Chem.Play.Tests and Hold.Runner.Tests, the rest the smoke), a Sim file (`Runtime/World.cs`) 160 s -> 169 s
   (everything, as before; noise). Unity (the Mac): no compile errors and no warnings in the changed files, EditMode
   573 (571 passed, 2 skipped; the new one is `PlayModesTests`), the player scripts compile for StandaloneWindows64
   and StandaloneLinux64 with `Chem.Play.dll` in them. Not run: the probes, and `tools/editmode.sh chem` (the
   Chemistry tests under Mono).*
   *Course: `CourseSystem`; data in `Play/Course.cs`. Forwarder: CourseRespawn (WizardDown).*
   *Defense: `DefenseSystem`; the path scratch it shared with the Hold is `WayField` (World.Ways), so PathCostAt still
   reads what either last worked out. Forwarders: Defense, DefenseLeft, ApplyDefense (DefenseField), PathCostAt.*
   *Golf: `GolfSystem`; data in `Play/Golf.cs`; GroundUnits stays on World (raiders and weather use it). Forwarders:
   Golf, ApplyGolf, GolfWaiting (GolfCourse, ChemGrid), the GolfBall/GolfCup consts (Mcp).*
   *Hold: `HoldSystem`; a run's data in `Play/HoldRun.cs`. Proved bit-equal beyond the goldens: 24 seeded Hold runs
   on dev and on the commit, CellHash/CastHash/RngState/DragonHash every 20 s up to wave 10, 588 lines the same.
   Forwarders: HoldSite, Hold, HoldRunning, HoldLeft, SetHold, HoldWarning, HoldRespawn, HoldNextWaveIn, Flying,
   HoldNext, ClimbRoute/FlyRoute (the Hold's view, lock-on, WizardDown, probes, Hold.Runner).*
   *Raiders: `RaiderSystem`; Roll stays on World (strikes, boulders, progress roll too). The same 588 lines.
   Forwarders: Raiders, RaiderAt, SpawnRaider, ShovedStatus/ThrownStatus (SpellCaster, Desk, the view, probes).*
   *Dragon: `DragonSystem` holds the flight; the starting state, the audit, Freshen and the save stay in
   World.Dragon.cs (buildings, saves and the view use them with no flight, and they reach into the chunk store).
   Proved on two seeds, summoned and by budget (6 flights, raiders downed): 480 sampled lines the same. Forwarders:
   Dragon, DragonFlights, ApplyDragon, PassLine, BreathBox, CaughtByBreath, ShelteredFromBreath.*

10. **Prototype.Game into feature assemblies (2026-11-27, the Mac with the Editor).** After steps 4-9 the game itself is
   the monolith: 367 files, 61k lines, and one type-level cycle of 185 files (38k lines) through `ChemGrid`, so a
   change anywhere recompiles all of it plus Probes, Net, Editor and the tests. The split, bottom up (each arrow
   points down): Punchy (own asmdef) ← **Kit** (materials, meshes, sounds, HUD drawing, input, the menu frame,
   `Registry`, perf counters, the log half of `EventLog`, leaf fx) ← **World** (`ChemGrid.*`, cells, dressing,
   effects, peers, props, things) ← **Land** (terrain, far ring, ocean, water, sky, weather) ← **Village** (valley
   chemistry, houses, rows, trees) and **Player** (wizard, casting, spells, items; one assembly, they lean on each
   other) ← **Fauna** ← **Modes** (Hold, Defense, Dragon, Golf, courts, progress) ← **Chat** ← **Shell** (menus,
   perf stages, playtest, showcases, updates). 56 back-references cut, the big ones by four seams:
   - **Grid add-ons:** `ChemGrid` no longer `AddComponent`s Hold, progress, waystones, row houses or the mods panel;
     a feature adds itself to the grid from its own assembly (`GridAddOns.Register` at load).
   - **A caster face in World** (`ICaster` + its registry) for what the grid, weather and peers ask of a
     `SpellCaster`; the local player's slot through `IPeers`.
   - **Ground and water faces in World** that `MapTerrains` and `Ocean` register with.
   - **`EventLog` split:** the log stays in Kit, the HUD host (vitals, cast HUD, radial) moves to Player.
   Seams first, inside the one assembly, each landed alone; then the asmdefs, bottom up. Proof each time: a Unity
   compile, EditMode, the player-script compiles, the probes (`smoke`, `online`, `roomopen`, `roomruin`, the mode
   probes). The measure: a one-file change's compile-and-reload time in a leaf, before and after.

   **The pattern this sets for new work:** a feature is a folder with its own asmdef that references what it needs
   below it and nothing references it. It joins the game through the seams (grid add-ons, registries, `IPeers`,
   the menu's sections, `[ProbeScenario]`), never by editing `ChemGrid`, `PauseMenu` or another hub; its plain logic
   goes in Plans or the core with dotnet tests; its probe goes in Probes.

Later, as work passes through: split the 485-file `Scripts/` root into folders by module before the asmdefs land
(moves only, with their `.meta` files); a dotnet twin of `probe smoke` on `Hold.Runner` (boat's T16) so the
smoke itself runs in the cloud.

Out of scope here: the frozen sandbox (J-042); rendering, sound and input, which keep one adapter each and need
no port.
