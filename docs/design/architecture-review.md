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
6. **View plans in plain C#.** A `core/UnityMath` shim (Vector2/3, Mathf, Color: only what the plans use) and a
   `Prototype.Plans` assembly: first the 20 Unity-free `Scripts/` files and the subjects of the 31 plain
   `Editor/Tests` files, with those tests moved to a dotnet project; then the plan halves of `RowMesher`,
   `CellWaterMesh` and `FarRing` (arrays out, the `Mesh` call stays). Each file moves with its test. **Cloud** for
   the plain files; **Unity** to compile the asmdef split, batched.
7. **An `IPeers` port and a `Prototype.Net` assembly.** Solo and NGO adapters; `SpellCaster`, `RuleSwitcher`,
   `PhysicsProp`, `ItemHolder` and the 19 files with `IsServer`/`IsSpawned` go through it; then `Net/` becomes its
   own asmdef so a view-to-Net reference is a compile error. Proof: `probe online`, `roomopen`, `roomruin`, the
   core's lockstep tests. Risk: medium (the order of sends). **Unity.**
8. **Registries for the scene searches, and a narrower `ChemGrid`.** `ValleyFauna`, `PhysicsProp`, `ThingSpawner`
   lists joined on enable (chunk-keyed, J-086), in place of the 244 `Find*` calls; the menu's sections as data;
   readers of `ChemGrid` that only need world, rules and effects take `CurrentGrid`. Done file by file as work
   passes through. Risk: low each. **Unity.**
9. **Game modes out of `World`.** Hold, Golf, Course, Raiders, Defense, Dragon from `World.*.cs` partials into
   systems over `World` data in `Chem.Play` (D9), one mode per step, each with its goldens unchanged and the Mono
   pass in that night's sweep. Largest step; do it as each mode is next worked on. **Cloud.**

Later, as work passes through: split the 485-file `Scripts/` root into folders by module before the asmdefs land
(moves only, with their `.meta` files); a dotnet twin of `probe smoke` on `Hold.Runner` (boat's T16) so the
smoke itself runs in the cloud.

Out of scope here: the frozen sandbox (J-042); rendering, sound and input, which keep one adapter each and need
no port.
