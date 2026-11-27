# Chem.Land: the dependency map, and why step 5 stopped at it

Step 5 of `docs/design/architecture-review.md` (section 6): the land out of the core into its own assembly,
`Chem.Land`, so a hills change builds and tests alone. Done 2026-11-26 by a cloud builder on `origin/dev` at
82bd453f, branch `claude/arch-5-chem-land`. **Outcome: stopped after the dependency map, as the brief says to when
the cycles need more than ~15 files changed beyond moves.** A second cloud run did section 6's plan; what it did is
the next section, and the rest of this file is the map as it was drawn.

## Done

- **5a (runtime split, 2026-11-26).** The prototype patch applied as it was (section 2's moves and forwarders), plus
  way 1 of section 3: `GeneratedMap.cs`, `Places.cs` and `MapIdentity.cs` moved to `Runtime/Land/`; the bake out of
  `GeneratedMap` into `MapBake.cs` (Sim; the old `GeneratedMap.Bake.cs` renamed, its GUID kept): `MapBake.EnsureRows`,
  `EnsureBaked`, `EnsureBakedInto`, and `Bake`, `WriteRowsOnly`, `SpawnPlaces` as extensions on `GeneratedMap`
  (`map.Bake(dir)` reads as before). `WorldPlaces.Generate` became `GeneratedPlaces.Generate` (Sim, with `WaterRuns`):
  it reads Play's files. The file names a bake writes moved down as consts (`MapFile.BuildingsName`, `CourseName`,
  `PuzzlesName`; `WorldBuildings.FileName` and the others are those). `core/Chem.Land` (netstandard2.1) compiles
  `Runtime/Land/*.cs`, and Game.Core references it. Callers changed: `Scripts/World/ChemGrid.cs` (the three bake calls),
  `Scripts/World/MapLand.cs` (`LandChunks.FromSource`), a comment in `Editor/Tests/WideWorldTests.cs`, and 10 test files.
  The sweep is the same 2263 results, by name, before and after; every land and hash golden passed unchanged.
  Unity's side: `Runtime/Land/Chem.Land.asmdef`, `Chem.Land` in the references of `Chemistry.asmdef`,
  `Chemistry.Mcp.asmdef` and `Chemistry.Tests.asmdef`. **Not yet compiled in Unity.**
- **5b (the tests, 2026-11-26).** `Chem.Land.Tests` references `Chem.Land` alone. 18 land fixtures split, each into its
  land part (the file as it was) and a `*WorldTests.cs` part holding the tests that need `World`, `Rules`,
  `ChemistryTests.R`, `MapCheck` or a bake: Bake, BiomeSketch, ChunkGen, FarRing, GroundPack, Hills, HillsForest,
  HillsRivers, HillsRoads, LandmarkIntent, MapIdentity (with `MapCheckRecordTests`), Map, RoadGrade, ScaleIsland,
  SeaMask, Village, WorldPlan, WorldTwelve. `BakeTests` keeps only helpers (a static class); its `WaterRunsTests`
  moved whole, by class, so its names didn't change. The split was found by the compiler (each test with an error
  against the land alone moved, then the helpers it used were qualified, `HillsTests.HillsDir`), and 56 test names
  changed by it, `Chem.Tests.HillsTests.ItPlaysFromItsRowsAlone` becoming `Chem.Tests.HillsWorldTests.ItPlaysFromItsRowsAlone`
  and so on; nothing else was renamed, and every one has the outcome it had. A land-only `LandFolders` (the repo's
  folders, as `ChemistryTests.Folders` finds them) replaces `ChemistryTests.Folders`/`WorldDir`/`RulesDir` in the land
  parts. The world parts, and the 11 files that are a World throughout (GeneratedSource, LaidRow, LandStamp, MapStamp,
  DrownedChain, Terrain, Places, LandmarkPicture and the Sparse, WorldBuild, MapSize benches), went to a new
  `Chem.LandWorld.Tests` (Land and Shared), **not Chem.Sim.Tests**: so a land change can run them without the whole
  simulation's 626 tests, and a Sim change doesn't run them. Sim no longer references Land: its two helpers moved to
  Shared (`BuiltWorlds.Build`, was `SparseWorldBench.Build`; `ChemistryTests.GameWorldDir`), and `ModSwitchBench`
  (Explicit; it bakes the nine) is LandWorld's. Counts: Land 290 passed (was 497 + 1 skipped), LandWorld 208 + 1
  skipped, the rest unchanged; the one new test is LandWorld's fixture-kinds check. A Land test file now runs Land and
  LandWorld (and the smoke), not Sim, Lockstep and Rules.Mcp.Tests.
- **5c (the `stop` rule, 2026-11-26; Andrew said yes the same day).** `runtime-land` is `"projects": ["Chem.Land"],
  "stop": ["Game.Core"]`, its `after` gone: a `Runtime/Land/` change runs Chem.Land.Tests and Chem.LandWorld.Tests
  (both reach Chem.Land without Game.Core), and the rest get the Unit smoke. `modules.mjs`'s `withDependents` takes a
  stop list (reached, not followed), `choose` passes the module's, and `check` asks of a file only what its module's
  cut runs (a test project compiling the file itself is always asked). gates.test.mjs: the Land runtime change, a Land
  file with a Sim file (everything), and the cut and the check on a made-up map. Gates, 4-core VM: 3 m 48 s for
  `Runtime/Land/HillsRoads.cs`, 3 m 45 s for `ChunkGenTests.cs` (step 3 measured 7 m 20 s and 6 m 07 s).
  **Left for a Unity machine:** the compile below.

**What a Unity compile must check** (nothing here has been through the Editor):

- `Runtime/Land/Chem.Land.asmdef` (and the `Runtime/Land.meta` folder) imports as its own assembly, `Chem.Land`, with
  `noEngineReferences`, Newtonsoft and `Prototype.Analyzers`, and `Chemistry` no longer compiles those files.
- `Chem.Land` in the references of `Chemistry.asmdef`, `Chemistry.Mcp.asmdef` and `Chemistry.Tests.asmdef`; the
  `InternalsVisibleTo` in `Runtime/Land/AssemblyInfo.cs` names `Chemistry` and `Chemistry.Mcp` (`LandCodec`,
  `BuildingTables`, `WorldPlaces.Doc`/`Write`...).
- The view (Assembly-CSharp, by `autoReferenced`): `Scripts/World/MapLand.cs` (`LandChunks.FromSource(w, h, landInto)`),
  `Scripts/World/ChemGrid.cs` (`MapBake.EnsureRows`/`EnsureBaked`/`EnsureBakedInto`), `Scripts/Editor/PrototypeBuild.cs`
  (`GeneratedMap.Baked`, unchanged but now in Chem.Land), `Editor/Tests/WideWorldTests.cs` (a comment).
- Chemistry.Tests: the 18 new `*WorldTests.cs`, `LandFolders.cs` and `BuiltWorlds.cs` (each with a new `.meta`) compile
  in the one Editor assembly beside the files they split from; `BakeTests` is now a static helper class.
  `tools/editmode.sh chem` once, for the Mono run of the moved tests.

Short version: the runtime half splits cleanly once one decision is made (where `GeneratedMap` goes). The tests
don't split: only 6 of the 36 Land test files (about 50 of 498 tests) compile against the land alone. Almost every
hills, ChunkGen and WorldTwelve file loads its map through `GeneratedMap` and also runs two peers on a `World` in
the same fixture. On top of that, the gates as written would still run everything for a Land runtime change,
because Game.Core references Chem.Land (section 5).

Everything below comes from trial compiles: the candidate files built alone as netstandard2.1 / C# 9, with the
compiler listing every missing type. That's how "uses" is measured here, not grep.

---

## 1. What Land can be: the part that compiles alone

These 45 files, plus 8 small types moved down (section 2), compiled as their own assembly with no `World`, no
`Rules` and nothing above them. Game.Core then built against them with `-warnaserror` and no warnings beyond S3776.
That was the prototype in `chem-land-split.prototype.patch` (applied in 5a and deleted; it's in git at 8c103303):

- Hills: `HillsForest`, `HillsMassif`, `HillsRivers`, `HillsRoads`
- ChunkGen: `ChunkGen`, `.Hills`, `.Known`, `.Land`, `.Rivers`, `.Spawns`
- World plans: `WorldPlan`, `.Finish`, `.Grid`, `.Library`, `.Roads`, `.Sketch`, `.Solver`, `.Spawns`; `WorldSketch`
- Themes: `ThemeBlueprint`, `ThemeField`, `ThemeGeometry`, `ThemeRegistry`, `ThemeSchema`
- Biomes and noise: `BiomeField`, `BiomeMath`, `WorldBiomes`, `WorldNoise`, `ValueNoise`, `WorldHash`
- Map files and land: `MapFile`, `LandFile` (`LandChunks`), `MapStamps` (reading only), `GroundPack`, `SeaMask`,
  `TerrainGrid`, `CellContour`, `RowPad`, `RoadGrade`
- Places in the plan: `Village`, `LandmarkIntent`, `FarLandmarks`, `KnownRing`
- Moved down with them: `Buildings.cs` (whole), `GameFolders.cs` (whole)

Of modules.json's `runtime-land` globs, these **stay in Sim** because they use `World`, `Cell` or `IChunkSource`.
They're the land's adapters onto a world, not the land itself:

| File | What it needs above Land |
|---|---|
| `World.Land.cs`, `BakedSource.cs` | `partial class World` (a partial can't cross assemblies) |
| `GeneratedSource.cs` | `IChunkSource`, `Cell`, `World` (MapWorld), `Rules` |
| `CellHeightfield.cs` | `World`, `Cell`, `CellPaint`, `Rules.Sim` |
| `OpenWorldGround.cs`, `OpenWorldPlan.cs` | `Cell`, `IChunkSource`, `ChunkKey`, `ChunkSwitch`, `LockstepSession`, `LockstepChannel<>` |
| `MapCheck.cs` | `WorldHold`, `WorldThings`, `WorldBuildings` (checks every module's map files) |
| `Places.cs`, `MapInputs.cs` | `CourseLayout`, `GolfCommand`, `WorldPuzzles` (Play's map files) |
| `MapIdentity.cs` | `GeneratedMap` |
| `GeneratedMap.cs`, `GeneratedMap.Bake.cs` | see section 3: the one hard cycle |

---

## 2. Every reference from Land to non-Land, and how each breaks

"Easy" means it was done in the prototype and built: moves, or a forwarder that keeps every caller as it is.

| Land file | Used | Defined in (module) | Break | Kind |
|---|---|---|---|---|
| 14 files (ChunkGen, LandFile, SeaMask, ...) | `Chunks` (Shift, Size, ChunkOf...) | `Chunk.cs` (Sim) | the static class to `Land/Chunks.cs`; `Chunk`/`ChunkBlock` stay | move |
| `ChunkGen`, `.Known`, `MapStamps` | `LandStamp` | `World.Land.cs` (Sim) | the class to `Land/LandStamp.cs`; the World partial stays | move |
| `KnownRing`, `GeneratedMap` | `StreamingParams` | `RuleTables.cs` (Rules) | the class to `Land/StreamingParams.cs` (sim.json still reads it) | move |
| `LandFile` | `LandCodec` (internal) | `ChunkCells.cs` (Sim) | to `Land/LandCodec.cs` | move + IVT |
| 13 files | `WorldHash` | `WorldHash.cs` (Sim) | the file to Land (no dependencies) | move |
| `LandmarkIntent`, `RoadGrade` | `BuildingRow.Box`, `BuildingRoad` | `Buildings.cs` (Sim) | the file to Land: it compiles alone | move |
| `MapIdentity` (later) | `GameFolders` | `GameFolders.cs` (Rules) | the file to Land: it compiles alone; the land's tests need it to find `world/` | move |
| `TerrainGrid` | `ChunkStreaming.Within` | `ChunkStreaming.cs` (Sim) | the body to `Chunks.Within`; `ChunkStreaming.Within` forwards (its callers unchanged) | forwarder |
| `MapFile` | `World.LandLowest`, `LandHighest` | `World.Land.cs` (Sim) | the consts to `MapFile`; World's become `= MapFile.LandLowest` (same values, compiled in) | const |
| `MapFile` | `GeneratedMap.PinnedSquares` | `GeneratedMap.cs` | the body to `MapFile.PinnedSquares`; GeneratedMap's forwards | forwarder |
| `LandFile` (`LandChunks.FromSource`) | `IGeneratedSource` | `GeneratedSource.cs` (Sim) | `FromSource(w, h, landInto)`, a delegate in place of the interface; 2 callers change (`Scripts/World/MapLand.cs`, `GeneratedSourceTests.cs`) | plain data |
| `MapStamps.LayInto(World)` | `World`, `Rules` | — | `LayInto` and `Bare` to a Sim extension class (`MapStampsLaying`): `stamps.LayInto(world)` reads the same, 0 callers change | extension |
| `ChunkGen` | `MapStamps.FromJson` | `MapStamps.cs` | the file to Land once `LayInto` is out (above) | move |
| `MapFile` | `GeneratedMap`, `TerrainGrid` | — | TerrainGrid to Land (above); GeneratedMap: section 3 | — |

Internals the core above uses (an `InternalsVisibleTo` in `Runtime/Land/AssemblyInfo.cs` for `Chemistry`, `Game.Core`
and `Chemistry.Mcp`, the names those assemblies have today): `LandCodec`, `BuildingTables.Tables/Parse/Json`,
`BuildingRow.Resolved/SpanOf/Shared/SharedTables`, `TerrainGrid.ChunkSpan`. Nothing went from internal to public.

Unity's side of the prototype: `Runtime/Land/Chem.Land.asmdef` (Chemistry.asmdef's settings: `noEngineReferences`,
`overrideReferences` with Newtonsoft, `autoReferenced`, Prototype.Analyzers), a folder `.meta`, and `Chem.Land` added to
Chemistry.asmdef's references. Chemistry.Mcp.asmdef and Chemistry.Tests.asmdef use Land types, so both need
`Chem.Land` too. The view (Assembly-CSharp) picks it up by `autoReferenced`.

**Rules to Land:** `BuildingTables`/`BuildingRow` (Rules.Buildings, Rules.Mods, ModAdds, ModPatch, ModValidator,
RuleTables), `StreamingParams` (RuleTables), `GameFolders` (ModValidator), `WorldHash` (Rules.Triggers). Each is a use
in the allowed direction once those types sit in Land. **Land to Rules: none.** The land needs no `Rules` at all, so
no `Chem.Rules` assembly is needed for step 5. (A Chem.Rules would be its own split anyway: `Rules` is one partial
class over 22 files, among them Play's `Hold.cs`, `Defense.cs`, `Hands.cs` and `Combos.cs`.)

---

## 3. The hard cycle: GeneratedMap

`GeneratedMap.Load(dir)` turns a map folder's world file into its solved plan and its `ChunkGen` (`.Gen`, `.Hills`).
Every hills test, ChunkGen, FarRing, ScaleIsland and WorldTwelve starts there, so for the land to test alone,
GeneratedMap has to be in Land. It can't be today, because the same class bakes a whole map folder:

- `Bake`, `WriteRowsOnly`, `EnsureRows`, `EnsureBaked`, `EnsureBakedInto` write `places.json` through
  `WorldPlaces.Generate`, which reads Play's files (`CourseLayout.Load`, `GolfCommand.Load`, `WorldPuzzles.Load`).
- A pinned map's reading takes its place ids from `WorldPlaces.Load(dir)`.
- `Baked` and the pinned copy name `WorldPlaces.FileName`, `WorldBuildings.FileName`, `CourseLayout.FileName` and
  `WorldPuzzles.FileName`; the bake's water bodies are `WaterRuns` (in `Places.cs`).

Ways through, smallest first:

1. **The bake out of GeneratedMap into Sim.** `GeneratedMap.Bake.cs` and the five bake/rows statics become a Sim class
   (for example `MapBake.EnsureRows(dir, outDir)`), so GeneratedMap.cs keeps load, plan, Gen, InputHash and pinned
   reading. The file names move down as consts (`WorldPlaces.FileName = MapFile.PlacesName`, ...). The pinned
   place ids need `Place`/`WorldPlaces.Load` in Land: `Places.cs` minus `Generate`'s mode part, which goes to Sim.
   Cost: GeneratedMap.cs, GeneratedMap.Bake.cs, Places.cs, WorldBuildings.cs, WorldPuzzles.cs, World.Course.cs,
   World.Golf.cs, and the callers of the bake statics: 17 files (`Scripts/World/ChemGrid.cs`, `Editor/Tests/WideWorldTests.cs`,
   `MapCheck.cs`, `GeneratedSource.cs`, and 13 Chemistry test files). It changes a public API that the view and the
   tests call.
2. **Play's map files down into Land** (`CourseLayout`, `GolfCommand`, `WorldPuzzles` readers, with their `World`
   halves split out as `MapStamps` was). GeneratedMap stays whole. It drags Play's data below Sim, and the trial
   compile shows `SpellDef` and `Rules` behind them, so it snowballs. Not recommended.

Either way, the runtime side is about 25 files changed beyond moves (the 9 of section 2 plus way 1's 17, less overlap).

---

## 4. The tests: why Chem.Land.Tests can't reference Chem.Land alone

Each of the 36 files in `ChemTestsLand` was compiled against the prototype's Chem.Land, with a stub for
`ChemistryTests.Folders`/`RulesDir`/`WorldDir`:

| Group | Files | Tests |
|---|---|---|
| Land alone | BiomeFieldTests, ContourTests, RowPadTests, ThemeTests, WorldGenTests, WorldPlanPicture (+ WorldPlanTests once `LandmarkIntentTests.GameWorldDir` and `LandmarkPicture` move to a land-only helper) | 52 |
| Land with one or two World tests in them | RoadGradeTests (one `World`), SeaMaskTests (one `World`), VillageTests (`ChemistryTests.R` once), GroundPackTests (`FallWatch`, `MapInputs`), MapIdentityTests (`MapCheck`, `MapIdentity`, a `World`), MapTests (`MapCheck`, `WorldBuildings`, `WorldThings`) | 64 |
| GeneratedMap, and two peers on a World in the same fixture | HillsTests, HillsForestTests, HillsMassifTests, HillsRiversTests, HillsRoadsTests, ChunkGenTests, FarRingTests, ScaleIslandTests, WorldTwelveTests, BiomeSketchTests, BakeTests, LandmarkIntentTests (`R`) | 98 |
| A World throughout (the land on a world) | GeneratedSourceTests, LaidRowTests, LandStampTests, MapStampTests, DrownedChainTests, TerrainTests, PlacesTests, SparseWorldBench, WorldBuildBench, MapSizeBench, LandmarkPicture | 114 (and the benches) |

(Counts are `[Test]` attributes; NUnit runs 498 with the cases. These are first-pass compiler errors, so a file can
need more than its row says.)

So the untangle is about 18 test files split into a land part and a world part (`HillsRoadsTests` and
`HillsRoadsWorldTests`, for example, sharing nothing but the map's folder). A land-only folder helper replaces
`ChemistryTests.Folders` in the land part, because `ChemistryTests` (in Chem.Shared.Tests) needs Game.Core. The world
parts need a project that references Game.Core: Chem.Sim.Tests, or a new `Chem.LandWorld.Tests`. Today the helpers
also run the other way: Chem.Sim.Tests references Chem.Land.Tests for `LandmarkIntentTests.Village`,
`ChunkGenTests.NineGolden`/`HashOfAll`, `ScaleIslandTests.CopyInputs`/`PlaceIds` and `BakeTests.TwoSquares`.

---

## 5. The gates still run everything, even after the split

`modules.mjs choose` runs a module's projects **and every project that references them**, followed through, and
also follows `compiledBy(file)`. With Game.Core referencing Chem.Land, a `Runtime/Land/` file reaches Game.Core and
so every test project. That's the same as today, and it's true: Sim, Play, Lockstep and the MCP do use the land. The
split alone narrows nothing for `runtime-land`. Only a Land *test* change narrows, once Chem.Sim.Tests no longer
references Chem.Land.Tests.

Getting "a land change runs Chem.Land.Tests (and the land-on-world tests)" needs a rule in the map, for example a
module field `"stop": ["Game.Core"]`: dependents aren't followed past Game.Core, so the projects beyond it get only the
smoke. `check()` would take the same cut per module. Whether that's safe enough is a judgment call for Andrew. The
smoke is `TestCategory=Unit`: `TerraformTests`' CellHash goldens are Unit, but `WorldTwelveTests`, `ChunkGenTests`
and the Hills fixtures are Simulation, so the land goldens would rely on Chem.Land.Tests itself (where they belong)
and on CI's full run after every push.

---

## 6. Suggested order, each step landing alone

- **5a (cloud):** the runtime split of sections 2 and 3, way 1 (GeneratedMap in Land, the bake in Sim). Test files
  stay where they are, with Chem.Land.Tests still referencing Game.Core through Chem.Shared.Tests. The land goldens
  show it changed nothing. Then a Unity compile of the second asmdef.
- **5b (cloud):** split the 18 mixed test files (method moves into `*WorldTests.cs` files), add a land-only folder
  helper, turn Chem.Land.Tests's reference into Chem.Land alone, and move the world parts and Sim's helpers.
- **5c (cloud, needs Andrew's call):** the `stop` rule of section 5 in modules.json/modules.mjs and gates.test.mjs,
  with runtime-land's `projects` becoming `["Chem.Land"]` and its `after` gone.

Baseline for whoever does it, the full `dotnet test core -s core/sweep.runsettings` at 82bd453f, by project (passed,
1 skipped in Land): Chem.Land.Tests 497 + 1, Chem.Sim.Tests 626, Rules.Mcp.Tests 449, Chem.Play.Tests 245,
Chem.Rules.Tests 190, Chem.Shared.Tests 87, Chem.Lockstep.Tests 80, Chem.Playtest.Tests 41, Playtest.Runner.Tests 10,
Hold.Runner.Tests 2, Chat.Bench.Tests 2: 2230 in all. Chem.Land.Tests alone took 2 m 38 s on 4 cores.
