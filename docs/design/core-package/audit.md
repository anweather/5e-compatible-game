# Core package audit (Ticket 1)

Status: the audit only. Nothing was moved or changed. Read against dev at 41e6d765 on 2026-11-12, by the rules in
[kickoff.md](kickoff.md) and the amendments in [README.md](README.md): phase 1 only, and the package lives in this repo
(`packages/com.anweather.chemistry-core/`). Ticket 2 waits for review.

## 1. Executive summary

- **What `core/Game.Core.csproj` compiles:** 198 files and about 50,800 lines, all from `unity/`:
  - `Chemistry/*.cs` (160 files), namespace `Chem`;
  - `Chemistry/Mcp/*.cs` (32 files), namespace `Chem.Mcp`;
  - `Scripts/Playtest/Logic/*.cs` (6 files), namespace `Playtest`.
- **No file uses UnityEngine, Unity.Entities, Unity.NetCode or Punchy.** The Unity assembly is already
  `noEngineReferences`. The only NuGet package the core needs is Newtonsoft.Json (105 files).
- **The proposed boundary:** two assemblies in the package.
  - **`Chem.Core`** is the cell simulation and the rules: cells, chunks, the tick, reactions, spells, rooms, mods,
    saves, snapshots, hashes. It also holds the map readers (land, things, buildings, places, stamps, bakes), because
    the World loads through them.
  - **`Chem.WorldGen`** is the generator: WorldPlan, ChunkGen, GeneratedMap, the themes, biomes, sea and landmarks. It
    depends on `Chem.Core`, and nothing in `Chem.Core` depends on it, after three small adaptations (section 3).
  - **Outside the package:** `Chem.Mcp` (the in-game rules chat and its tools, which the game ships) and
    `Playtest/Logic`. Also the host's files: GameFolders, PlayerSave, PlayerId, ModCatalog, ModMove, and HoldProgress's
    progress file.
- **The cut between the generator and the rest is already clean.** A type scan of every non-generator file, with
  comments and strings stripped, finds only three uses of generator types (section 3).
- **The cut between the simulation and our game is not.** Hold, Golf, Dragon, Defense, Course, Strike, Hands, Progress,
  Raiders, Waves and the rest are `partial` parts of `World` and `Rules`. They can't go to another assembly without a
  refactor, which the kickoff rules out. So in pass 1 they move with `Chem.Core` unchanged, marked as game features.
- **The package can't yet be used without our repo's layout.**
  - Every loader takes a folder (`Rules.Load(rulesDir)`, `WorldThings.Load(dir)`, …).
  - `GameFolders` finds `rules/` and `world/maps/<id>/` by walking up from a path.
  - Nearly every loader already wraps a text entry point (`Rules.FromJson`, `WorldThings.FromJson`, …). So a host
    could supply text instead of folders with a thin layer and no behaviour change (section 5).
- **Baseline.** `dotnet build core -c Release -warnaserror` is clean. `dotnet test core` passes (counts in section 4).
  The PC's Unity EditMode run on 2026-11-12: unity 444/445 (1 skipped), chem 1627/1630 (3 skipped).
- **The biggest risk:** the game features are compiled into `World` and `Rules` themselves. Pass 1 can draw the
  package boundary, but the package will carry our game's rules machinery (the Hold, golf, the dragon…) until a later
  pass makes those features plug in from outside.

## 2. File-by-file classification

### How Game.Core is built

- **Target framework and language:**
  - `Game.Core` is netstandard2.1, AssemblyName `Game.Core`, RootNamespace `Chem`.
  - `core/Directory.Build.props` sets LangVersion 9.0 ("Unity's compiler supports C# 9 at most"), Nullable off and
    `EnableDefaultCompileItems` false.
  - It also sets: analyzers at `latest-recommended`, code style enforced in the build, warnings as errors (S3776
    excepted), XML docs on (CS1591 off).
  - The rule config comes from `unity/Assets/Default.globalconfig`.
- **Sources** are linked, not copied:
  - `Compile Include="../../unity/Assets/Prototype/Chemistry/*.cs"`
  - `…/Chemistry/Mcp/*.cs`
  - `…/Scripts/Playtest/Logic/*.cs`
  - Unity compiles the same files, so there is one copy.
- **NuGet:**
  - `Game.Core`: Newtonsoft.Json 13.0.3.
  - Every project, as private analyzers: SonarAnalyzer.CSharp 10.35.0.4138 and Microsoft.CodeAnalysis.NetAnalyzers
    10.0.402.
  - `Game.Core.Tests`: NUnit 3.14.0, NUnit3TestAdapter 4.6.0, Microsoft.NET.Test.Sdk 17.11.1.
  - `Rules.Mcp` and `Playtest.Text`: ModelContextProtocol 2.3.0 and Microsoft.Extensions.Hosting 10.0.13.
  - `Playtest.Runner`: System.Management 8.0.0 and System.Diagnostics.EventLog 8.0.1.
- **Unity's side:**
  - `Chemistry.asmdef`: name `Chemistry`, rootNamespace `Chem`, `noEngineReferences: true`, `overrideReferences`.
    Its only precompiled reference is `Newtonsoft.Json.dll`, from `com.unity.nuget.newtonsoft-json` 3.2.2. It
    references `Prototype.Analyzers`.
  - `Chemistry/Mcp/` has no asmdef of its own, so in Unity `Chem.Mcp` is compiled into `Chemistry`.
  - `Scripts/Playtest/Logic/` has none either: in Unity it's in Assembly-CSharp, in .NET it's in Game.Core.
  - `Chemistry/Tests/Chemistry.Tests.asmdef`: Editor only, `UNITY_INCLUDE_TESTS`, with nunit and Newtonsoft
    precompiled.
  - The other asmdefs (`Prototype.Analyzers`, `Prototype.AssetPacks.Editor`) don't touch the core.
  - No Entities, NetCode for Entities or Burst. `com.unity.netcode.gameobjects` 2.13.3 is used only by
    `Scripts/Net/`.
- **The test and tool projects (all net8.0):**
  - `Game.Core.Tests` links `Chemistry/Tests/*.cs` (113 files) and `Scripts/Playtest/Editor/*Tests.cs` (2). It adds
    `Parallel.cs`, `../TestParallel.cs`, `TestKinds.cs`, and `Friends.cs` (InternalsVisibleTo for the four tool test
    projects, which reuse the Chemistry tests' internal helpers).
  - `Rules.Mcp` (+ `.Tests`), `Playtest.Runner` (+ `.Tests`), `Playtest.Text`, `Hold.Runner` (+ `.Tests`) and
    `Chat.Bench` (+ `.Tests`, which links `Rules.Mcp.Tests/ChatFixtures.cs`) all reference Game.Core.

### How content is found and loaded

- **Rules:**
  - `Rules.Load(rulesDir)` reads every `rules/*.json` (19 files, about 4,400 lines) into a name → text dictionary, then
    calls `Rules.FromJson(IDictionary<string,string>)`.
  - `LookFamilies.Load(rulesDir)` and `ModBudgets.Load(rulesDir)` (through GameFolders) read single rules files the
    same way.
  - Mods come in as text (`ModPatch.Parse`) or from `rules/mods/` and the player's folder (`ModCatalog`, `ModMove`).
- **Maps:** each `world/maps/<id>/` file has a `Load(dir)` that wraps a `Parse`/`FromJson`/`Read(text)`:
  - `LandFile`/`LandChunks.Read`, `WorldThings.FromJson`, `WorldBuildings`/`Village`, `WorldPlaces.FromJson`;
  - `MapStamps.FromJson`, `WorldPuzzles.FromJson`, `CourseLayout.FromJson`, `WorldHold`;
  - `BakedSource.FromText`, `GolfCommand.Load`.
  - The generator's inputs read the same way: `ThemeRegistry`, `GroundPack`, `BiomeTable.Parse`, `SquareLibrary.Parse`,
    `LandmarkIntent.Parse`, `HorizonTable.Parse`, `FarLandmarks`.
- **`GameFolders`** (host): `DefaultMap = "valley"`.
  - From a rules path it works out `rules/`, `world/maps/<map>/`, `rules/mods/`, the player-data folder and the Claude
    folder.
  - `InRepo(start)` walks up to the repo; `ForGame(streamingAssets, project, editor, playerData, map)` is the player.
  - Core code that touches it: `ModValidator` (`ModBudgets.Load(string rulesDir) => Load(new GameFolders(rulesDir))`).
    Worldgen code: `MapIdentity`. Tool code: `Workbench`, `StandInValley`.
- **37 files touch the disk** (`File.`/`Directory.`). All are loaders, the bake writer (`GeneratedMap.Bake`), the
  player's files (`PlayerSave`, `ModCatalog`, `ModMove`) or `GameFolders`. None reads the disk during a tick.

### Determinism markers

- **Threads** in package files:
  - `GeneratedSource` (it makes chunks on workers);
  - `KnownRing`, `ChunkGen.Land`, `GeneratedMap(.Bake)` (parallel bakes);
  - `CellHeightfield` (view threading);
  - `Hold` (one `lock` around a variant cache).
  - The World guards itself against other threads (`OwnThread`, `World.Pack`).
- **Clocks:** `Stopwatch` and `Environment.CurrentManagedThreadId`, in `World`, `World.Pack`, `GeneratedSource` and
  `GeneratedMap`. All are for timings and the thread guard; none feeds state.
- No `System.Random` outside the world's seeded RNG. Reflection only in `Mcp/McpRpc`.

### The table

How to read the columns:

- **Uses** lists the clusters a file depends on, after the split proposed here:
  - core, game (the World/Rules feature partials), worldgen, game-view, host, mcp, playtest.
  - It comes from a type-name scan, checked by hand. Edges that were only name collisions (nested private types,
    `Sample`, `Chain`) are removed.
- **Newtonsoft:** the file uses JObject/JToken/JsonConvert.
- **Engine:** none in every row, so the column is left out.
- **Tests** counts the test files that name one of the file's types.
  - "none" means no test names them. Most such files are `World`/`Rules` partials, covered through the World's tests.
  - Mcp files with "none" are covered by `Rules.Mcp.Tests`, which goes through `Workbench`.
- Paths are under `unity/Assets/Prototype/Chemistry/`, except `Logic/`, which is `unity/Assets/Prototype/Scripts/Playtest/Logic/`.

#### → Chem.Core (80 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `BakedSource.cs` | 255 | Chem: `partial World`, `BakedSource`, `LegendCells` | core, game | — | — | map data | move unchanged | 5 (GeneratedSourceTests, RollingHashTests, …) |
| `BuildingsShown.cs` | 118 | Chem: `BuildingsShown` | game | JSON | — | reusable math (view) | move unchanged | 1 (UnseenTests) |
| `Cell.cs` | 207 | Chem: `Cell`, `Rare`, `LogKind` | core | — | — | core domain | move unchanged | 23 (BuildingTests, ChemistryTests, …) |
| `CellContour.cs` | 312 | Chem: `ContourMesh`, `ContourSpec`, `CellContour` | core | — | — | reusable math (view) | move unchanged | 1 (ContourTests) |
| `CellHeightfield.cs` | 737 | Chem: `CellHeightfield`, `ChunkState`, `CellCache` | core, game | — | threads | reusable math (view) | move unchanged | 6 (HeightfieldThreadingTests, HitchBench, …) |
| `CellPaint.cs` | 444 | Chem: `PaintBlock`, `CellPaint`, `Noise` | core, game | — | — | reusable math (view) | move unchanged | 4 (BiomeFieldTests, LandStampTests, …) |
| `Chunk.cs` | 203 | Chem: `Chunks`, `ChunkBlock`, `Chunk` | core, game | — | — | core domain | move unchanged | 25 (BiomeFieldTests, CatchUpTests, …) |
| `ChunkCells.cs` | 175 | Chem: `ChunkCells`, `LandCodec` | core | — | — | core domain | move unchanged | 2 (SparseWorldBench, WorldPlanPicture) |
| `ChunkStreaming.cs` | 348 | Chem: `ChunkSwitch`, `ChunkStreaming`, `ChunkPlanner` | core, game | — | — | core domain | move unchanged | 9 (CatchUpTests, ChunkStreamingTests, …) |
| `Colors.cs` | 131 | Chem: `Colors`, `ColourMixer` | core | — | — | reusable math (view) | move unchanged | 1 (LookScopeTests) |
| `Combos.cs` | 344 | Chem: `partial Rules/Checker`, `ComboDef`, `DpadSpellDef` | core, game | JSON | — | core domain | move unchanged | 8 (ChargedCastTests, ComboTests, …) |
| `Components.cs` | 132 | Chem: `partial Rules`, `ComponentSet` | game | JSON | — | core domain | move unchanged | **none** |
| `Effects.cs` | 97 | Chem: `partial World`, `EffectKind`, `EffectCommand` | core, game | — | — | core domain | move unchanged | 9 (ChemistryTests, ComboTests, …) |
| `JoinSnapshot.cs` | 121 | Chem: `JoinSnapshot` | core, game | JSON | — | core domain | move unchanged | 2 (LateJoinTests, SparseWorldBench) |
| `LandFile.cs` | 400 | Chem: `LandFile`, `Writer`, `Inflated` | core | JSON | disk | map data | move unchanged | 15 (BakeTests, ChemistryTests, …) |
| `LookFamilies.cs` | 271 | Chem: `LookDef`, `LookFamily`, `EffectScale` | core, game | JSON | disk | reusable math (view) | move unchanged | 2 (LookTests, LookToolsTests) |
| `LookScopes.cs` | 608 | Chem: `LookColour`, `PlayerSpot`, `LookMix` | core, game | JSON | — | reusable math (view) | move unchanged | 2 (LookScopeTests, WorldToolsTests) |
| `MapFile.cs` | 150 | Chem: `MapFile` | core, game, worldgen | JSON | disk | map data | adapt: its `GeneratedMap.PinnedSquares(mapDir)` call goes behind a core seam or moves to worldgen | 18 (BakeTests, ChunkGenTests, …) |
| `MapStamps.cs` | 133 | Chem: `MapStamp`, `MapStamps` | core, game | JSON | disk | map data | move unchanged | 7 (DrownedChainTests, GroundPackTests, …) |
| `MaterialGroups.cs` | 254 | Chem: `MaterialGroups` | — | JSON | — | core domain | move unchanged | 1 (LiveRulesTests) |
| `ModAdds.cs` | 120 | Chem: `ModAdd`, `ModAdds`, `IdTable` | core, game | JSON | — | core domain | move unchanged | **none** |
| `ModPatch.cs` | 140 | Chem: `ModPatch`, `ModException` | core, game | JSON | — | core domain | move unchanged | 60 (AirTests, BuildingTests, …) |
| `ModValidator.cs` | 857 | Chem: `ModCheck`, `ModProblem`, `ModBudgets` | core, game, host | JSON | disk, GameFolders | core domain | adapt: `ModBudgets.Load(rulesDir)` via GameFolders, and a reference to PlaytestChains, leave it | 31 (ActToolsTests, BallInCupTests, …) |
| `Placement.cs` | 56 | Chem: `Placement` | core | — | — | reusable math (view) | move unchanged | 1 (LayTests) |
| `Places.cs` | 577 | Chem: `Place`, `WorldPlaces`, `WaterRuns` | core, game | JSON | disk | map data | move unchanged | 13 (BakeTests, BallInCupTests, …) |
| `PropState.cs` | 149 | Chem: `PropEvents`, `PropState` | core, game | — | — | reusable math (view) | move unchanged | 2 (PropTests, RuleSwitchTests) |
| `RoadGrade.cs` | 107 | Chem: `RoadGrade` | game | — | — | map data | move unchanged | 1 (RoadGradeTests) |
| `RollingHash.cs` | 99 | Chem: `RollingHash`, `Fnv1aSum` | core, game | — | — | core domain | move unchanged | 8 (ChunkTests, GeneratedSourceTests, …) |
| `Room.cs` | 185 | Chem: `ChangeMode`, `RoomChange`, `RoomCommand` | core | — | — | core domain | move unchanged | 25 (ActToolsTests, BallInCupTests, …) |
| `RowPad.cs` | 87 | Chem: `PadArea`, `RowPad` | — | — | — | map data | move unchanged | 1 (RowPadTests) |
| `RuleSchema.cs` | 256 | Chem: `RuleFieldType`, `RuleField`, `RuleSchema` | game | — | — | core domain | move unchanged | **none** |
| `RuleSet.cs` | 130 | Chem: `ActiveMod`, `RuleSet` | core, game | JSON | — | core domain | move unchanged | 27 (CatchUpTests, ChargedCastTests, …) |
| `RuleTables.cs` | 1421 | Chem: `partial Rules`, `TerrainDef`, `CoverDef` | core, game | JSON | disk | core domain | move unchanged | 5 (FarRingTests, HoldBodiesTests, …) |
| `Rules.Check.cs` | 630 | Chem: `partial Rules/Checker` | core, game | JSON | — | core domain | move unchanged | **none** |
| `Rules.CheckComponents.cs` | 55 | Chem: `partial Rules/Checker` | — | JSON | — | core domain | move unchanged | **none** |
| `Rules.CheckFauna.cs` | 233 | Chem: `partial Rules/Checker` | game | JSON | — | core domain | move unchanged | **none** |
| `Rules.CheckItems.cs` | 90 | Chem: `partial Rules/Checker` | core, game | JSON | — | core domain | move unchanged | **none** |
| `Rules.CheckProducts.cs` | 49 | Chem: `partial Rules/Checker` | — | JSON | — | core domain | move unchanged | **none** |
| `Rules.CheckSchema.cs` | 53 | Chem: `partial Rules/Checker` | core | JSON | — | core domain | move unchanged | **none** |
| `Rules.Lapse.cs` | 83 | Chem: `partial Rules/Checker`, `MaterialLapse` | — | JSON | — | core domain | move unchanged | **none** |
| `Rules.Liquids.cs` | 204 | Chem: `partial Rules/Checker`, `LiquidDef` | core | JSON | — | core domain | move unchanged | **none** |
| `Rules.Mix.cs` | 122 | Chem: `partial Rules`, `RoomMix` | core | JSON | — | core domain | move unchanged | **none** |
| `Rules.Mods.cs` | 336 | Chem: `partial Rules` | core, game | JSON | — | core domain | move unchanged | **none** |
| `Sky.cs` | 171 | Chem: `WeatherKind`, `WeatherNow`, `LightLevel` | core | — | — | core domain | move unchanged | 10 (LiquidProductTests, LiveChunkTests, …) |
| `SpellGroups.cs` | 78 | Chem: `partial Rules/Checker`, `GroupDef` | — | JSON | — | core domain | move unchanged | **none** |
| `TempMaterials.cs` | 60 | Chem: `TempMaterial`, `TempMaterials` | core, game | JSON | — | core domain | move unchanged | 1 (TempMaterialTests) |
| `TerrainGrid.cs` | 259 | Chem: `ChunkRect`, `ChunkRects`, `TerrainGrid` | core | — | — | reusable math (view) | move unchanged | 11 (CourseTests, GolfTests, …) |
| `TickBatch.cs` | 214 | Chem: `CastCommand`, `RuleChange`, `TickBatch` | core, game | — | — | core domain | move unchanged | 30 (BallInCupTests, CatchUpTests, …) |
| `Village.cs` | 628 | Chem: `BuildingKind`, `WallKind`, `RoofKind` | core | JSON | disk | map data | move unchanged | 18 (BuildingGapsTests, BuildingTests, …) |
| `Wing.cs` | 176 | Chem: `Wing` | core, game | JSON | — | map data | move unchanged | 2 (BuildingTests, ModDependentsTests) |
| `World.CatchUp.cs` | 77 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Chunks.cs` | 628 | Chem: `partial World`, `RowSpan`, `RowCells` | core | — | — | core domain | move unchanged | **none** |
| `World.Combos.cs` | 255 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Entities.cs` | 67 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Fit.cs` | 123 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Flash.cs` | 69 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Lapse.cs` | 236 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Liquids.cs` | 227 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Machines.cs` | 236 | Chem: `partial World`, `MachineState`, `GateLink` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Mix.cs` | 169 | Chem: `partial World` | core, game | JSON | — | core domain | move unchanged | **none** |
| `World.Movers.cs` | 46 | Chem: `partial World`, `Mover` | — | — | — | core domain | move unchanged | **none** |
| `World.Night.cs` | 89 | Chem: `partial World`, `LightBlock` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Pack.cs` | 747 | Chem: `partial World`, `PackStrings`, `PackedCell` | core, game | JSON | timing | core domain | move unchanged | 2 (LateJoinTests, SeamTests) |
| `World.React.cs` | 579 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Room.cs` | 260 | Chem: `partial World` | core, game | JSON | — | core domain | move unchanged | **none** |
| `World.RoomSky.cs` | 56 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Save.cs` | 486 | Chem: `partial World`, `IChunkSource`, `SaveException` | core, game | JSON | — | core domain | move unchanged | 17 (BuildingTests, ChemistryTests, …) |
| `World.Shatter.cs` | 111 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Sim.cs` | 941 | Chem: `partial World`, `TickStage` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Spells.cs` | 663 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Spread.cs` | 87 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.StillWater.cs` | 143 | Chem: `partial World` | core | — | — | core domain | move unchanged | **none** |
| `World.Terraform.cs` | 203 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Time.cs` | 116 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.Weather.cs` | 371 | Chem: `partial World` | core, game | — | — | core domain | move unchanged | **none** |
| `World.cs` | 813 | Chem: `partial World`, `Whirl`, `Star` | core, game | — | timing | core domain | move unchanged | 5 (BigSpellTests, ChemistryTests, …) |
| `WorldBuildings.cs` | 250 | Chem: `WorldBuildings`, `VillageRows` | core, game | JSON | disk | map data | move unchanged | 10 (BuildingTests, DrownedChainTests, …) |
| `WorldHash.cs` | 43 | Chem: `WorldHash` | — | — | — | core domain | move unchanged | 3 (HeightfieldThreadingTests, RollingHashTests, …) |
| `WorldPuzzles.cs` | 142 | Chem: `Puzzle`, `WorldPuzzles` | core, game | JSON | disk | map data | move unchanged | 5 (CourseTests, DrownedChainTests, …) |
| `WorldThings.cs` | 338 | Chem: `ThingId`, `ThingSpawn`, `ThingChance` | core, game | JSON | disk | map data | move unchanged | 11 (ChemistryTests, DragonTests, …) |

#### → Chem.Core (rows); ProgressFile to host (1 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `HoldProgress.cs` | 92 | Chem: `UpgradeDef`, `UnlockDef`, `RunWizard` | game, host | — | — | original-game feature | adapt: `ProgressFile` (on disk, uses PlayerSave) goes to the host; the rule rows stay | 2 (HoldProgressTests, PlayerSaveTests) |

#### → Chem.Core (pass 1; partial World/Rules) (33 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `Breath.cs` | 120 | Chem: `BreathParams`, `Breath` | — | JSON | — | original-game feature | move unchanged | 1 (BreathTests) |
| `Buildings.cs` | 500 | Chem: `BuildingFill`, `BuildingPart`, `OpeningDef` | core | JSON | — | original-game feature | move unchanged | 13 (BuildingGapsTests, BuildingTests, …) |
| `Defense.cs` | 225 | Chem: `partial Rules`, `RaiderDef`, `DefenseParams` | core, game | JSON | — | original-game feature | move unchanged | 7 (ChemistryTests, DefenseTests, …) |
| `Downed.cs` | 59 | Chem: `DownedParams`, `Downed` | — | JSON | — | original-game feature | move unchanged | 3 (DownedTests, DragonTests, …) |
| `Fauna.cs` | 365 | Chem: `CoatDef`, `VaryDef`, `SlotDef` | game | JSON | — | original-game feature | move unchanged | 1 (GraftTests) |
| `FaunaGraft.cs` | 234 | Chem: `FaunaGraft`, `Flattening`, `Placing` | game | JSON | — | original-game feature | move unchanged | **none** |
| `Hands.cs` | 185 | Chem: `partial Rules/Checker`, `HandRowDef`, `HandsDef` | core | JSON | — | original-game feature | move unchanged | 6 (BigSpellTests, HandsTests, …) |
| `Hold.cs` | 419 | Chem: `partial Rules/Checker`, `AdjectiveDef`, `WaveThemeDef` | core, game | JSON | disk, threads | original-game feature | move unchanged | 9 (DrownedChainTests, HandsTests, …) |
| `Items.cs` | 141 | Chem: `partial Rules`, `ItemUses`, `ItemLightDef` | core | JSON | — | original-game feature | move unchanged | 1 (ItemTests) |
| `PlayerStats.cs` | 108 | Chem: `PlayerStats`, `PlayerParams` | — | JSON | — | original-game feature | move unchanged | 1 (PlayerStatsTests) |
| `Rules.Buildings.cs` | 284 | Chem: `partial Rules/Checker` | core, game | JSON | — | original-game feature | move unchanged | **none** |
| `Rules.Lay.cs` | 69 | Chem: `partial Rules/Checker`, `LayRow` | — | JSON | — | original-game feature | move unchanged | **none** |
| `Rules.Things.cs` | 68 | Chem: `partial Rules/Checker`, `ModThing` | — | JSON | — | original-game feature | move unchanged | **none** |
| `Rules.Triggers.cs` | 239 | Chem: `partial Rules/Checker`, `TriggerEvent`, `TriggerAt` | core | JSON | — | original-game feature | move unchanged | 3 (PlayerIdTests, TriggerTests, …) |
| `StandKeeper.cs` | 193 | Chem: `IStampedStrike`, `StampedStrike`, `StandKeeper` | core, game | — | — | original-game feature | move unchanged | 2 (LateJoinTests, StandKeeperTests) |
| `World.Acts.cs` | 367 | Chem: `partial World`, `ActDone` | core, game | JSON | — | original-game feature | move unchanged | **none** |
| `World.Boulders.cs` | 128 | Chem: `partial World`, `Boulder` | core, game | — | — | original-game feature | move unchanged | 1 (PropTests) |
| `World.Buildings.cs` | 399 | Chem: `partial World`, `BuildingsChange` | core, game | JSON | — | original-game feature | move unchanged | 1 (BuildingTests) |
| `World.Course.cs` | 284 | Chem: `partial World`, `CourseRect`, `SpellZone` | core, game | JSON | disk | original-game feature | move unchanged | 4 (CourseTests, DragonTests, …) |
| `World.Defense.cs` | 337 | Chem: `partial World` | core, game | — | — | original-game feature | move unchanged | **none** |
| `World.Dragon.cs` | 618 | Chem: `partial World`, `DragonParams`, `DragonPhase` | core, game | JSON | — | original-game feature | move unchanged | 9 (BuildingTests, ChunkTests, …) |
| `World.Golf.cs` | 378 | Chem: `partial World`, `GolfParams`, `GolfCommand` | core, game | JSON | disk | original-game feature | move unchanged | 8 (CourseTests, DragonTests, …) |
| `World.Hands.cs` | 334 | Chem: `partial World` | core, game | JSON | — | original-game feature | move unchanged | **none** |
| `World.Hold.cs` | 634 | Chem: `partial World`, `HoldPhase`, `HoldState` | core, game | JSON | — | original-game feature | move unchanged | 1 (HoldTests) |
| `World.Land.cs` | 711 | Chem: `partial World`, `LandStamp` | core, game | JSON | — | original-game feature | move unchanged | 4 (HeightfieldThreadingTests, LandStampTests, …) |
| `World.Lay.cs` | 110 | Chem: `partial World` | core, game | JSON | — | original-game feature | move unchanged | **none** |
| `World.Place.cs` | 237 | Chem: `partial World`, `Summon` | core, game | JSON | — | original-game feature | move unchanged | **none** |
| `World.Progress.cs` | 206 | Chem: `partial World` | core, game | JSON | — | original-game feature | move unchanged | **none** |
| `World.Raiders.cs` | 442 | Chem: `partial World` | core, game | — | — | original-game feature | move unchanged | **none** |
| `World.Strike.cs` | 299 | Chem: `partial World`, `StrikeParams`, `StrikeKind` | core, game | JSON | — | original-game feature | move unchanged | 9 (BallInCupTests, HoldLockstepTests, …) |
| `World.Things.cs` | 89 | Chem: `partial World` | core, game | — | — | original-game feature | move unchanged | **none** |
| `World.Triggers.cs` | 227 | Chem: `partial World`, `TriggeredCast` | core, game | JSON | — | original-game feature | move unchanged | **none** |
| `World.Waves.cs` | 126 | Chem: `partial World`, `Wave` | core, game | — | — | original-game feature | move unchanged | 7 (DefenseTests, HoldBodiesTests, …) |

#### → Chem.WorldGen (35 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `BiomeField.cs` | 590 | Chem: `BiomeClimate`, `BiomeBlend`, `BiomeRow` | core, worldgen | JSON | disk | world generation | move unchanged | 5 (BiomeFieldTests, BiomeSketchTests, …) |
| `BiomeMath.cs` | 171 | Chem: `BiomeMath` | core | — | — | world generation | move unchanged | 4 (BiomeFieldTests, BiomeSketchTests, …) |
| `ChunkGen.Known.cs` | 131 | Chem: `partial ChunkGen`, `KnownChunk` | core, game, worldgen | — | — | world generation | move unchanged | 7 (BakeTests, BiomeSketchTests, …) |
| `ChunkGen.Land.cs` | 548 | Chem: `partial ChunkGen`, `Shape`, `LandPoint` | core, worldgen | — | threads | world generation | move unchanged | 13 (BakeTests, BiomeSketchTests, …) |
| `ChunkGen.Rivers.cs` | 399 | Chem: `partial ChunkGen`, `LaidRiver`, `Segment` | worldgen | — | — | world generation | move unchanged | 43 (BakeTests, BigSpellTests, …) |
| `ChunkGen.Spawns.cs` | 74 | Chem: `partial ChunkGen`, `MapSpawn` | worldgen | — | — | world generation | move unchanged | 7 (BakeTests, BiomeSketchTests, …) |
| `ChunkGen.cs` | 425 | Chem: `partial ChunkGen`, `GeneratedChunk`, `PinnedSquare` | core, worldgen | JSON | — | world generation | move unchanged | 8 (BakeTests, BiomeSketchTests, …) |
| `FarLandmarks.cs` | 172 | Chem: `HorizonRow`, `HorizonTable`, `FarLandmark` | worldgen | JSON | disk | world generation | move unchanged | 3 (FarRingTests, ScaleIslandTests, …) |
| `GeneratedMap.Bake.cs` | 190 | Chem: `partial GeneratedMap`, `ChunkRows` | core, worldgen | JSON | disk, threads, timing | world generation | move unchanged | 10 (BakeTests, BiomeSketchTests, …) |
| `GeneratedMap.cs` | 567 | Chem: `partial GeneratedMap`, `BakeReport` | core, game, worldgen | JSON | disk, threads, timing | world generation | move unchanged | 10 (BakeTests, BiomeSketchTests, …) |
| `GeneratedSource.cs` | 340 | Chem: `IGeneratedSource`, `GeneratedSource`, `Held` | core, game, worldgen | JSON | disk, threads, timing | world generation | split: `IGeneratedSource` to its own core file; the class stays in worldgen | 2 (GeneratedSourceTests, ScaleIslandTests) |
| `GroundPack.cs` | 224 | Chem: `GroundTree`, `GroundChunk`, `GroundPack` | core | JSON | disk | world generation | move unchanged | 3 (DrownedChainTests, GroundPackTests, …) |
| `KnownRing.cs` | 208 | Chem: `KnownRing` | core, worldgen | — | threads | world generation | move unchanged | 2 (FarRingTests, WorldTwelveTests) |
| `LandmarkIntent.cs` | 894 | Chem: `LandmarkIntent`, `PlaceNames`, `LandmarkRows` | core, game, worldgen | JSON | disk | world generation | move unchanged | 5 (ChunkGenTests, FarRingTests, …) |
| `MapCheck.cs` | 269 | Chem: `MapCheck` | core, game, worldgen | JSON | disk | world generation | move unchanged | 7 (DrownedChainTests, GeneratedSourceTests, …) |
| `MapIdentity.cs` | 74 | Chem: `MapIdentity` | core, host, worldgen | — | disk, GameFolders | world generation | adapt: reads GameFolders | 2 (DrownedChainTests, MapIdentityTests) |
| `MapInputs.cs` | 49 | Chem: `MapInputs` | core, game | — | disk | world generation | move unchanged | 1 (GroundPackTests) |
| `SeaMask.cs` | 261 | Chem: `SeaMask`, `SeaNear` | core | — | — | world generation | move unchanged | 2 (SeaMaskTests, SparseWorldBench) |
| `ThemeBlueprint.cs` | 609 | Chem: `Continent`, `BpZone`, `BpLake` | core, worldgen | JSON | — | world generation | move unchanged | 9 (ActToolsTests, ChatKnowledgeTests, …) |
| `ThemeField.cs` | 312 | Chem: `ThemeField`, `LiveZone`, `LiveLake` | worldgen | — | — | world generation | move unchanged | 1 (ThemeTests) |
| `ThemeGeometry.cs` | 347 | Chem: `Pt`, `GeoBox`, `Harmonic` | — | — | — | world generation | move unchanged | 2 (ThemeTests, WorldGenTests) |
| `ThemeRegistry.cs` | 60 | Chem: `ThemeRegistry` | worldgen | JSON | disk | world generation | move unchanged | 1 (ThemeTests) |
| `ThemeSchema.cs` | 505 | Chem: `Theme`, `ThemeSchema` | — | JSON | — | world generation | move unchanged | 2 (HoldWaveTests, ThemeTests) |
| `ValueNoise.cs` | 52 | Chem: `ValueNoise` | — | — | — | world generation | move unchanged | 1 (ContourTests) |
| `WorldBiomes.cs` | 127 | Chem: `WorldBiomes` | worldgen | — | — | world generation | move unchanged | **none** |
| `WorldNoise.cs` | 96 | Chem: `WorldNoise` | core | — | — | world generation | move unchanged | 1 (WorldGenTests) |
| `WorldPlan.Finish.cs` | 435 | Chem: `partial WorldPlan` | core, worldgen | — | — | world generation | move unchanged | 6 (BiomeSketchTests, FarRingTests, …) |
| `WorldPlan.Grid.cs` | 95 | Chem: `PlanWrap`, `PlanGrid` | worldgen | — | — | world generation | move unchanged | 6 (BiomeSketchTests, FarRingTests, …) |
| `WorldPlan.Library.cs` | 453 | Chem: `EdgeKind`, `EdgeFeature`, `TemplateLandmark` | core, worldgen | JSON | disk | world generation | move unchanged | 5 (BiomeSketchTests, FarRingTests, …) |
| `WorldPlan.Roads.cs` | 661 | Chem: `partial WorldPlan` | core, worldgen | — | — | world generation | move unchanged | 6 (BiomeSketchTests, FarRingTests, …) |
| `WorldPlan.Sketch.cs` | 125 | Chem: `partial WorldPlan`, `SketchReading` | core, worldgen | — | — | world generation | move unchanged | 6 (BiomeSketchTests, FarRingTests, …) |
| `WorldPlan.Solver.cs` | 426 | Chem: `PlanSolver`, `Frame` | worldgen | — | — | world generation | move unchanged | 2 (PlaytestLogicTests, RunnerTests) |
| `WorldPlan.Spawns.cs` | 175 | Chem: `partial WorldPlan`, `PlanSpawn` | worldgen | — | — | world generation | move unchanged | 6 (BiomeSketchTests, FarRingTests, …) |
| `WorldPlan.cs` | 720 | Chem: `partial WorldPlan`, `PlanInputs`, `ForcedSquare` | core, worldgen | — | — | world generation | move unchanged | 6 (BiomeSketchTests, FarRingTests, …) |
| `WorldSketch.cs` | 178 | Chem: `WorldSketch`, `Shape`, `PlacedSketch` | worldgen | JSON | — | world generation | move unchanged | 8 (BiomeSketchTests, ChatKnowledgeTests, …) |

#### → remain (game, engine-free) (6 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `ElementHold.cs` | 24 | Chem: `ElementHold` | — | — | — | original-game feature (view) | remain | 1 (ChargedCastTests) |
| `FallWatch.cs` | 28 | Chem: `FallWatch` | — | — | — | original-game feature (view) | remain | 1 (GroundPackTests) |
| `HandsPick.cs` | 45 | Chem: `HandsPick` | — | — | — | original-game feature (view) | remain | 1 (HandsPickTests) |
| `RadialLayout.cs` | 149 | Chem: `RadialGroup`, `RadialLayout`, `RadialGeometry` | core, game, game-view | — | — | original-game feature (view) | remain | 1 (RadialTests) |
| `SpellList.cs` | 64 | Chem: `SpellTab`, `SpellList` | core, game | — | — | original-game feature (view) | remain | **none** |
| `SpellRadial.cs` | 327 | Chem: `RadialTuning`, `RadialInputKind`, `RadialInput` | game-view, worldgen | — | — | original-game feature (view) | remain | 1 (RadialTests) |

#### → remain (game / host) (5 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `GameFolders.cs` | 135 | Chem: `GameFolders`, `ClaudeFolder` | core, game | — | disk, GameFolders | host adapter | remain | 18 (ApiModelsTests, BuildingGapsTests, …) |
| `ModCatalog.cs` | 112 | Chem: `ModFile`, `ModCatalog` | core | JSON | disk | host adapter | remain | 2 (PlayerModsTests, RuleSwitchTests) |
| `ModMove.cs` | 100 | Chem: `ModMove` | host | — | disk | host adapter | remain | 1 (PlayerModsTests) |
| `PlayerId.cs` | 88 | Chem: `PlayerId` | game | — | — | host adapter | remain | 1 (PlayerIdTests) |
| `PlayerSave.cs` | 144 | Chem: `PlayerSave` | — | JSON | disk | host adapter | remain | 1 (PlayerSaveTests) |

#### → remain (Rules.Mcp tooling) (32 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `Mcp/ApiBudget.cs` | 131 | Chem.Mcp: `ApiPrice`, `ApiBudget` | — | JSON | disk, threads, timing | tooling | remain | 2 (ApiLoopTests, ApiModelsTests) |
| `Mcp/ApiLoop.cs` | 529 | Chem.Mcp: `WireAnswer`, `IChatWire`, `ApiLoop` | mcp | JSON | disk, threads | tooling | remain | 2 (ApiLoopTests, ClaudeRunTests) |
| `Mcp/ApiModels.cs` | 88 | Chem.Mcp: `KeyAnswer`, `KeyCheck`, `IKeyLister` | mcp | JSON | threads | tooling | remain | 1 (ApiModelsTests) |
| `Mcp/BodyShape.cs` | 166 | Chem.Mcp: `BodyShape`, `Limb`, `Quat` | game, mcp | — | — | tooling | remain | **none** |
| `Mcp/ChatHarness.cs` | 23 | Chem.Mcp: `IChatHarness` | mcp | — | — | tooling | remain | **none** |
| `Mcp/ChatProfile.cs` | 34 | Chem.Mcp: `ChatProfile` | — | — | — | tooling | remain | 1 (ApiModelsTests) |
| `Mcp/ChatRecord.cs` | 503 | Chem.Mcp: `ChatOutcome`, `ChatWho`, `ChatProgress` | mcp | JSON | disk, threads, timing | tooling | remain | 11 (ApiLoopTests, BuildingGapsTests, …) |
| `Mcp/ClaudeRun.cs` | 270 | Chem.Mcp: `ClaudeSaid`, `ClaudeRun` | mcp | JSON | disk | tooling | remain | 5 (ApiLoopTests, ApiModelsTests, …) |
| `Mcp/McpRpc.cs` | 200 | Chem.Mcp: `McpRpc` | core, mcp | JSON | threads, timing, reflection | tooling | remain | 6 (ApiLoopTests, ChatKnowledgeTests, …) |
| `Mcp/RuleFields.cs` | 229 | Chem.Mcp: `RuleFields` | core, game, mcp | JSON | — | tooling | remain | 2 (LiveStateTests, SpreadTests) |
| `Mcp/RulesTools.cs` | 377 | Chem.Mcp: `RulesToolAttribute`, `RulesTools` | core, game, mcp, worldgen | — | threads | tooling | remain | 3 (ChatKnowledgeTests, LiveStateTests, …) |
| `Mcp/Simulator.cs` | 234 | Chem.Mcp: `SimRun`, `Simulator` | core, game, mcp, worldgen | — | timing | tooling | remain | **none** |
| `Mcp/StandInValley.cs` | 140 | Chem.Mcp: `StandInValley` | core, game, host, worldgen | JSON | disk, GameFolders | tooling | remain | **none** |
| `Mcp/Workbench.Acts.cs` | 350 | Chem.Mcp: `partial Workbench`, `ActPlan`, `Spot` | core, game, mcp | JSON | — | tooling | remain | 1 (WorldPlanPicture) |
| `Mcp/Workbench.Bodies.cs` | 101 | Chem.Mcp: `partial Workbench` | game, mcp, worldgen | — | — | tooling | remain | **none** |
| `Mcp/Workbench.Buildings.cs` | 445 | Chem.Mcp: `partial Workbench`, `IWorldDesk` | core, game, mcp, worldgen | JSON | — | tooling | remain | 1 (SiteRefusalTests) |
| `Mcp/Workbench.Draft.cs` | 449 | Chem.Mcp: `partial Workbench`, `SpellArgs` | core, game, game-view, mcp, worldgen | JSON | — | tooling | remain | 2 (ChatKnowledgeTests, LiveStateTests) |
| `Mcp/Workbench.Dragon.cs` | 77 | Chem.Mcp: `partial Workbench` | — | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Fauna.cs` | 205 | Chem.Mcp: `partial Workbench` | core, game, mcp, worldgen | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Live.cs` | 269 | Chem.Mcp: `partial Workbench`, `WorldNow`, `Layer` | core, game, mcp, worldgen | JSON | — | tooling | remain | 2 (LiveStateTests, SiteRefusalTests) |
| `Mcp/Workbench.Look.cs` | 504 | Chem.Mcp: `partial Workbench` | core, game, mcp, worldgen | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Outside.cs` | 27 | Chem.Mcp: `partial Workbench` | — | — | — | tooling | remain | **none** |
| `Mcp/Workbench.Places.cs` | 135 | Chem.Mcp: `partial Workbench`, `HereSpot`, `IHere` | core, mcp | — | — | tooling | remain | 3 (PlayerToolsTests, TriggerToolsTests, …) |
| `Mcp/Workbench.Player.cs` | 104 | Chem.Mcp: `partial Workbench` | game, mcp, worldgen | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Read.cs` | 382 | Chem.Mcp: `partial Workbench` | core, game, mcp, worldgen | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Room.cs` | 206 | Chem.Mcp: `partial Workbench`, `IRoomDesk` | core, game, mcp, worldgen | JSON | disk | tooling | remain | 2 (LiveStateTests, McpRpcTests) |
| `Mcp/Workbench.Scatter.cs` | 98 | Chem.Mcp: `partial Workbench` | core, game, mcp, worldgen | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Simulate.cs` | 68 | Chem.Mcp: `partial Workbench` | core, game, mcp | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Sky.cs` | 109 | Chem.Mcp: `partial Workbench`, `ILiveDesk` | core, mcp, worldgen | — | — | tooling | remain | 2 (LiveStateTests, SiteRefusalTests) |
| `Mcp/Workbench.Things.cs` | 125 | Chem.Mcp: `partial Workbench` | core, game, mcp, worldgen | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.Triggers.cs` | 139 | Chem.Mcp: `partial Workbench` | core, game, mcp, worldgen | JSON | — | tooling | remain | **none** |
| `Mcp/Workbench.cs` | 201 | Chem.Mcp: `partial Workbench`, `RulesToolException`, `Draft` | core, game, host | JSON | disk, GameFolders | tooling | remain | 19 (ActToolsTests, BallInCupTests, …) |

#### → remain (Playtest tooling) (6 files)

| File | Lines | Namespace: key types | Uses (clusters) | Newtonsoft | Disk / threads / clock | Class | Move | Tests (files) |
|---|---|---|---|---|---|---|---|---|
| `Logic/PlaytestChains.cs` | 147 | Playtest: `Chain`, `Chains` | core, game, playtest, worldgen | — | — | tooling | remain | 2 (PlaytestChainTests, PlaytestLogicTests) |
| `Logic/PlaytestFindings.cs` | 120 | Playtest: `Category`, `Candidate`, `KnownIssue` | playtest, worldgen | JSON | — | tooling | remain | 145 (ActToolsTests, AirTests, …) |
| `Logic/PlaytestGoals.cs` | 167 | Playtest: `GoalKind`, `Goal`, `GoalOptions` | playtest | — | — | tooling | remain | 2 (PlaytestLogicTests, RunnerTests) |
| `Logic/PlaytestRandom.cs` | 33 | Playtest: `PlaytestRandom` | — | — | — | tooling | remain | 1 (PlaytestLogicTests) |
| `Logic/PlaytestRun.cs` | 121 | Playtest: `TraceStep`, `RunReport`, `ReplayCheck` | playtest, worldgen | JSON | — | tooling | remain | 2 (PlaytestLogicTests, RunnerTests) |
| `Logic/PlaytestWatches.cs` | 269 | Playtest: `WizardWatch`, `Sample`, `FrameWatch` | core, playtest, worldgen | — | — | tooling | remain | 2 (PlaytestLogicTests, WorldTwelveTests) |

## 3. Dependency clusters

```
                       ┌──────────────────────────────┐
  Unity view (Scripts) │  Chem.Mcp (in-game chat,     │   Playtest/Logic, tools
  ───────────────────▶ │  tools)   ─────────────────▶ │   (Rules.Mcp, runners)
           │           └──────────────┬───────────────┘
           │                          ▼
           │      ┌──────────── package ─────────────────────────────┐
           ├────▶ │ Chem.WorldGen ──────────▶ Chem.Core              │
           │      │ (WorldPlan, ChunkGen,     (cells, tick, rules,   │
           │      │  GeneratedMap, themes,     mods, rooms, saves,   │
           │      │  biomes, sea, landmarks)   map readers, view     │
           │      │                            math; game-feature    │
           │      │                            partials in pass 1)   │
           │      └──────────────────────────────────▲───────────────┘
           └──▶ host / game: GameFolders, PlayerSave, PlayerId,
                ModCatalog, ModMove, ProgressFile, the spell radial ───┘
```

- **Core → worldgen, after removing collisions:** only three uses.
  - `MapFile` calls `GeneratedMap.PinnedSquares(mapDir)` (MapFile.cs:97). Fix: a seam in core that worldgen fills, or
    this one call moves to worldgen.
  - `WorldBuildings` uses `RoadGrade`. `RoadGrade` is a small pure static class, also used by `Buildings` (core) and
    `LandmarkIntent`. Fix: it moves to core.
  - `LookFamilies` uses `CellPaint`, and `MapFile` uses `TerrainGrid.Layout()`. Both are engine-free view math. Fix:
    keep both in core.
- **`IGeneratedSource`** is declared in `GeneratedSource.cs`. `World`, `World.Chunks`, `World.Pack`, `ChunkStreaming`
  and `WorldBuildings` use it. Fix: the interface goes in its own core file; the class stays in worldgen.
- **Map readers that core uses, so they go in core:**
  - `LandFile`/`LandChunks` (World), `LandStamp` (defined in `World.Land`), `BakedSource` (World.Pack);
  - `WorldThings`/`ThingId` (Effects, Room, the World partials, Rules.Things, ModValidator);
  - `WorldBuildings` with `Village` and `RowPad`, `WorldPlaces`, `WorldHold` (in `Hold.cs`), `MapStamps`;
  - `WorldPuzzles`, `CourseLayout`, `GolfCommand.Load`;
  - `Wing`, used only by `Workbench.Buildings`, goes with the buildings.
- **Core ↔ game features:** the edges run both ways and through the same types.
  - There are 33 files of `partial World`/`partial Rules`: Acts, Boulders, Buildings, Course, Defense, Dragon, Golf,
    Hands, Hold, Land, Lay, Place, Progress, Raiders, Strike, Things, Triggers and Waves, and Rules.Buildings, .Lay,
    .Things and .Triggers.
  - The non-partial feature files go with them: Breath, Buildings, Defense, Downed, Fauna, FaunaGraft, Hands, Hold,
    Items, PlayerStats, StandKeeper.
  - These are one cluster with core in pass 1.
- **Host → core:** one edge back. `ModValidator` calls `new GameFolders(rulesDir)` for its budgets. Fix: an overload that
  takes the budgets' text, with the folder version in the host.
  - `HoldProgress` mixes the Hold's rule rows (`UpgradeDef`, `UnlockDef`, `RunWizard`: core) with `ProgressFile`. That
    is on disk and uses `PlayerSave`, so it goes to the host.
- **Core → tooling:** none. `Chem.Mcp` and `Playtest/Logic` depend on core, game, worldgen and host (`GameFolders`,
  `ModCatalog`). Nothing in the package depends on them.
  - The Unity view uses `Chem.Mcp` in the player (chat, player mods, the API key setup). So it's a game assembly, not
    an Editor tool: in Unity it needs its own asmdef referencing the package.
- **View-only files with no core user:** `RadialLayout`, `SpellRadial`, `SpellList`, `HandsPick`, `ElementHold` and
  `FallWatch` are the spell radial and HUD helpers. They stay in the game.
  - `CellContour` and `CellHeightfield` have no core user either, but they're general terrain math: they go to core
    with `CellPaint` and `TerrainGrid`.

## 4. Build/test baseline

Run on this Linux VM (2 cores, .NET 8 SDK), dev at 41e6d765, 2026-11-12:

| Command | Result | Time |
|---|---|---|
| `dotnet build core -c Release -warnaserror` | 0 warnings, 0 errors | 34 s |
| `dotnet test core`: Game.Core.Tests | 1671 passed, 1 skipped, 0 failed (1672) | 16 min 51 s |
| `dotnet test core`: Rules.Mcp.Tests | 449 passed | 1 min 12 s |
| `dotnet test core`: Playtest.Runner.Tests, Hold.Runner.Tests, Chat.Bench.Tests | 10, 2 and 2 passed | 2 s |
| `dotnet test core`, the whole run | all passed | 18 min 39 s |

- **Unity, from the PC on 2026-11-12:** EditMode `unity` 444/445 (1 skipped), `chem` 1627/1630 (3 skipped).
- **Golden and determinism tests:**
  - the `CellHash`/FNV-1a goldens in `TerraformTests.cs` and `ChemistryTests.cs`;
  - `RollingHashTests`, `SaveTests`, `LateJoinTests`, `GeneratedSourceTests` and the bake tests (`BakeTests`, which
    compare generated maps byte for byte).
  - They all run under `dotnet test` with no Editor.
- **What needs the Unity Editor:**
  - the `unity` EditMode assembly (Scripts' tests);
  - `Chemistry.Tests.asmdef`'s copy of the same Chemistry tests;
  - the probes (`tools/probe.sh`).
- **Explicit tests,** not run by default: `HeightfieldThreadingTests`, `ScaleIslandTests`, and the benches
  (`SparseWorldBench`, `TickStageBench`, `FogTickBench`, `HitchBench`, `MapSizeBench`, `WorldBuildBench`).

**Which tests are portable.**

- All 113 `Chemistry/Tests` files are engine-free and run under NUnit in both Unity and .NET. The one engine line is
  `ChemistryTests.Folders`: `UnityEngine.Application.dataPath` under `#if UNITY_5_3_OR_NEWER`, else the test binary's
  folder.
- Most read our content, though:
  - **35 files** load a map from `world/` (`WorldDir()`, `LandmarkIntentTests.GameWorldDir`, the valley and the nine).
  - **66 files** load only `rules/` (`ChemistryTests.R`, `RulesDir()`).
  - **12 files** need neither: ContourTests, HandsPickTests, HoldEastTests, PlayerIdTests, PlayerSaveTests, RowPadTests,
    StandKeeperTests, WorldGenTests, and the pictures and benches.
- **For a package:**
  - Tests that pin our rules' behaviour (the reactions, the goldens) are tests of the game's content as much as of the
    engine.
  - Engine tests that only need *some* rules would need a small sample ruleset in the package.
  - So pass 1 keeps every test where it is, run against this repo's `rules/` and `world/`. Making a portable subset
    (a sample ruleset, fixtures in the package) is later work.

## 5. Risks and unresolved questions

1. **Game features inside `World` and `Rules` (the biggest risk).**
   - The package boundary can be drawn around the files, but `Chem.Core` will carry the Hold, golf, the dragon, the
     defense, raiders and waves.
   - Another game using the package gets them too, and their rules tables (`hold.json`, `fauna.json`, …) are expected
     by `Rules.FromJson`.
   - Getting them out needs a feature/plug-in seam on `World` and `Rules`, a behaviour-preserving refactor, but a large
     one. Not in phase 1.
2. **Content by folder path.**
   - A host can't give the core its rules and map without our folder layout today.
   - Proposal, for a later ticket: a small content interface in core, e.g. `IContentSource { string Text(string name);
     IEnumerable<string> Names(string folder); }`, used by the existing `Parse`/`FromJson` entry points.
   - Today's `Load(dir)` methods become one folder implementation, kept for our game and tests.
   - `GameFolders` stays in the game as the thing that builds it. No behaviour changes, since every loader already
     parses text.
3. **Newtonsoft parity.**
   - .NET builds against Newtonsoft.Json 13.0.3; Unity uses the DLL from `com.unity.nuget.newtonsoft-json` 3.2.2.
   - Save text, the rules' fingerprints and the snapshot format go through it. A version difference could change text
     that's hashed.
   - Pin and check the versions together. The package should declare the Unity package as a dependency, not ship
     its own DLL.
4. **Threads in the package.** `GeneratedSource`, `KnownRing`, the parallel bakes and `CellHeightfield` use workers.
   - The World's thread guard and the bake tests cover determinism.
   - But a host that drives the World from another thread meets the guard. Document it as part of the package's
     contract.
5. **netstandard2.1 and C# 9.** These are Unity's limits. The package's .NET build must keep LangVersion 9, or code that
   compiles in CI would fail in Unity.
6. **Analyzer config by repo path.**
   - `Directory.Build.props` points at `unity/Assets/Default.globalconfig`, and Unity at `Prototype.Analyzers`.
   - If the sources move to `packages/…`, both need re-pointing, and the package's own build must keep the same
     rules. Otherwise warnings-as-errors diverge.
7. **The package path inside a Unity project.**
   - Sources under `packages/com.anweather.chemistry-core/` would be outside `unity/Assets/`.
   - Unity would take them through a `file:` reference in `unity/Packages/manifest.json`, and `core/` would link them
     from there.
   - `.meta` files are needed in the package folder for Unity. They need generating once, then kept stable.
8. **Tests' helpers are internal** and shared through `InternalsVisibleTo` (`Friends.cs`). If tests split between the
   package and the game, those helpers need a home both can see.
9. **Open questions for Andrew:**
   - Does the package carry the game features in phase 1 (as proposed), or should phase 1 wait for the plug-in
     seam?
   - Are the view-math files (`CellPaint`, `TerrainGrid`, `CellContour`, `CellHeightfield`, the look files) part of
     the core's contract, or a third, `Chem.View`, assembly later?
   - Should `Chem.Mcp` stay one assembly with the game, or be split into the game's chat and a tools assembly?
   - Punchy: the README puts its licence and the friend's permission first. Nothing in the core touches it, so this
     audit needs neither.

## 6. Recommended extraction order

Each step is behaviour-preserving, with the build and `dotnet test core` green and goldens unchanged after it:

1. **The three seams, in place:**
   - `IGeneratedSource` to its own file;
   - `RoadGrade` is classed as core (it's already its own pure file, so nothing changes);
   - `MapFile`'s pinned-squares call goes behind a core seam.
   - `ModValidator`'s budgets take text, with the folder overload kept.
   - `ProgressFile` is split out of `HoldProgress.cs`.
   - No moves yet; the build proves core no longer names worldgen.
2. **Split `Game.Core` into `Chem.Core` and `Chem.WorldGen` projects,** still linking the same files from `unity/`.
   - Mcp and Playtest/Logic go to their own projects (`Chem.Mcp`, `Playtest.Logic`).
   - The host files go to a `Chem.Game` project.
   - CI and the tool projects reference the new ones. This proves the boundary without Unity.
3. **The Unity side:**
   - asmdefs for `Chem.Core`, `Chem.WorldGen`, `Chem.Mcp` and the game's engine-free files.
   - `Chemistry.asmdef` is replaced. The Scripts' references follow (Needs: unity).
4. **Move the package's files** to `packages/com.anweather.chemistry-core/` (Runtime/Core, Runtime/WorldGen, a
   `package.json`, asmdefs, `.meta` files).
   - Unity references it with `file:`; `core/` links from there. One copy, as now.
5. **Later, not phase 1:** the content interface (risk 2), a sample ruleset and portable tests, then the game-feature
   plug-in seam (risk 1).

## 7. Files that should not move in the first pass

- **Our game and its host,** which stay in the game:
  - `GameFolders.cs`, `PlayerSave.cs`, `PlayerId.cs`, `ModCatalog.cs`, `ModMove.cs`;
  - `HoldProgress.cs`'s `ProgressFile` (split out; the rule rows go with core).
- **The game's engine-free view helpers:** `RadialLayout.cs`, `SpellRadial.cs`, `SpellList.cs`, `HandsPick.cs`,
  `ElementHold.cs`, `FallWatch.cs`.
- **Tooling and the in-game chat:** all of `Chemistry/Mcp/*.cs` (32 files) and `Scripts/Playtest/Logic/*.cs` (6).
- **All tests:** `Chemistry/Tests/*.cs`, `Scripts/Playtest/Editor/*Tests.cs` and the `core/*.Tests` projects stay
  where they are, against this repo's `rules/` and `world/` (section 4).
- **Content:** `rules/`, `rules/mods/` and `world/maps/` stay as data in the repo. The package holds code only.
- **The game-feature partials** do move with `Chem.Core` (they can't be separated without a refactor), but they're
  listed here so the next pass knows them:
  - `World.Acts`, `.Boulders`, `.Buildings`, `.Course`, `.Defense`, `.Dragon`, `.Golf`, `.Hands`, `.Hold`;
  - `World.Land`, `.Lay`, `.Place`, `.Progress`, `.Raiders`, `.Strike`, `.Things`, `.Triggers`, `.Waves`;
  - `Rules.Buildings`, `.Lay`, `.Things`, `.Triggers`;
  - `Breath`, `Buildings`, `Defense`, `Downed`, `Fauna`, `FaunaGraft`, `Hands`, `Hold`, `Items`, `PlayerStats`,
    `StandKeeper`.
