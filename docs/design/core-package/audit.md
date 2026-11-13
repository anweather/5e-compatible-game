# Core-package audit (Ticket 1)

Ticket 1 of `plan.md`: what's in the engine-free core, what can move into a package, what should stay in the game, two
ways to move it with what each costs, and whether now is the time. Read-only: nothing was moved. Taken at dev
41e6d765 (2026-11-12). Punchy is at b66b4c2a on GitHub (the local clone is 31 commits behind).

## 1. Summary

- **The core is clean of Unity but not of our game.** None of its ~200 files touches UnityEngine. But the `World` class
  (one partial class over 43 files) has our game modes built into it: golf, the gauntlet course, the dragon, the Hold
  (tower defence), raiders, and between-run progress. Both the tick (`World.Sim.cs:118-134`) and save/load
  (`World.Save.cs:84-99, 280-289`) call into them. A partial class can't be split across assemblies, so those modes
  can't leave the core without hooks being added first.
- **The rules engine assumes our content in places.** Examples: the 22 base elements are hard-coded
  (`RuleTables.cs:685`); `dirt`, `water`, `ice`, `steam` and `fog` are fixed ids; and the fauna, buildings, hold,
  golf, dragon and looks tables are parsed by the core. Loading itself is already host-friendly:
  `Rules.Load(dir)` and `Rules.FromJson(files)` take a path or the texts, so another game can supply its own rules today.
- **World generation is mostly a separate layer.** WorldPlan, ChunkGen, Theme* and the biome code don't need `World`.
  But a handful of data types run both ways: `Buildings`, `Fauna`, `WorldThings`, `LandChunks`, `IGeneratedSource`, and
  the parts of `LookScopes` that `Rules.Check` needs. Those have to stay with the sim.
- **Tools separate cleanly.** Nothing in the core references `Chem.Mcp` or `Playtest`. `Mcp/` has no asmdef of its
  own today, so it's compiled into the Chemistry assembly. The chat half of it is used by the game's Scripts.
- **Two ways to make the package** (section 6):
  - **A, lift and shift:** move the folder as it is. 0 C# edits, ~10 edited files, about half a day.
  - **B, carve a true sim core:** pull out the game modes, content ids and loaders. Roughly 50-70 C# edits, and it
    risks the golden hashes and lockstep. Weeks of core work, with other core work paused.
- **Timing: not this week** (section 7). The core is the busiest code in the repo: 205 commits in the last 7 days,
  and three branches from today still open on it. Do A at the next quiet point (right after a release, with those
  branches landed or parked). Do B only when Punchy has a concrete need it would serve.

## 2. Build and test baseline

| Item | Value |
|---|---|
| Core target | `netstandard2.1`, C# 9 (`LangVersion 9.0`, Unity's limit, D3), Nullable off, ImplicitUsings off |
| Tools and tests | `net8.0` (Rules.Mcp, Playtest.Runner, Playtest.Text, Hold.Runner, Chat.Bench, and the five test projects) |
| NuGet (core) | Newtonsoft.Json 13.0.3 (used by ~100 files; rules are held as `JObject`, so it can't be dropped) |
| Analyzers | SonarAnalyzer.CSharp 10.35.0.4138, NetAnalyzers 10.0.402, `unity/Assets/Default.globalconfig`; warnings are errors (S3776 excepted) |
| How sources are linked | `Game.Core.csproj` globs `unity/Assets/Prototype/Chemistry/*.cs`, `Chemistry/Mcp/*.cs`, `Scripts/Playtest/Logic/*.cs`; `Game.Core.Tests` globs `Chemistry/Tests/*.cs` and `Scripts/Playtest/Editor/*Tests.cs` |
| Unity assembly | `Chemistry.asmdef`: `noEngineReferences`, `autoReferenced` (Assembly-CSharp sees it), precompiled `Newtonsoft.Json.dll`, references `Prototype.Analyzers`. Covers `Mcp/` too. `Chemistry.Tests.asmdef` is Editor-only (NUnit) |
| Unity packages | ours: Unity 6000.3.27f1, `com.unity.nuget.newtonsoft-json` 3.2.2, test-framework 1.6.0. Punchy: Unity **6000.5.8f1**, Entities 6.5, URP 17.5, Newtonsoft 3.2.2 (only as a dependency of another package) |
| Build | `dotnet build core -c Release -warnaserror`: 0 errors, 155 S3776 warnings, 43 s |
| Tests | `dotnet test core -c Release --no-build`: **2,134 passed, 1 skipped, 0 failed**. Game.Core.Tests 1671 (+1 skipped), 3 m 02 s; Rules.Mcp.Tests 449; Playtest.Runner.Tests 10; Hold.Runner.Tests 2; Chat.Bench.Tests 2 |
| Goldens | Cell and world hashes in ChemistryTests (52 hash assertions), BuildingTests 21, PackTests 16, GeneratedSourceTests 15, TerraformTests 14, ChunkTests 10, RuleSwitchByChunkTests 10, WeatherTests 9, HeightfieldThreadingTests 8, LiquidTests 8, LayTests 7, ComboTests 7, WorldGenTests 7, and a few others. The hash code is in `WorldHash.cs`, `Cell.cs` (Fnv1a), `RollingHash.cs`, `WorldPlan.cs:668` and `LookScopes.cs:313` |
| Needs the Editor | only `unity command test run` (EditMode) and `tools/probe.sh`. CI (`quality.yml`, ubuntu, .NET 8) runs build, test and format; `duplicates.yml` runs jscpd |

**Content tie:** nearly every test loads the real `rules/` and `world/maps/valley` through
`ChemistryTests.Folders` → `GameFolders.InRepo`. So in pass A the tests can't go into the package; they stay with the game.

## 3. What stays in the core, what moves out

Classes, as `plan.md` defines them: **Core** (portable rules or simulation), **Math** (engine-free world maths), **Game**
(our game's feature), **Tool**, **Adapter** (IO and paths, belongs to the host). "Sim" means the core package.
"Gen" means world generation: in the package's second assembly, or kept in the game. "Game" means stays in our game.

### 3a. The sim World (43 partial files)

| File(s) | Class | Dest | Move as is? | What blocks a clean core |
|---|---|---|---|---|
| World.cs, Chunks, CatchUp, Pack, Sim, Spread, Liquids, Mix, StillWater, Fit, Lapse, Lay, Entities, Flash, Shatter, Terraform, Machines, Boulders, Waves, Movers, Weather, Time, Night, RoomSky, Room, Combos, Land | Core | Sim | yes | `Sim.cs:120-133` ticks Raiders, Defense, Hold, Golf, and the `TickStage.Dragon` stage |
| World.Save.cs | Core, with game parts | Sim | yes | saves and loads Golf, Dragon, Course, Hold, Defense (`:84-99, 280-289`) |
| World.React.cs, Spells, Hands, Strike, Triggers, Acts | Core, with game hooks | Sim | yes | Raider (`React:36,42`, `Strike:222`), Course and Golf (`Spells:278,298`), HoldSite/HoldTower (`Hands:32-54`), GolfState (`Triggers:106`), ActGolf (`Acts:142,167,272`) |
| World.Things.cs, Place, Buildings | Core | Sim | yes | WorldThings, BuildingRaster and WorldBuildings, which load map data |
| World.Golf.cs, Course | Game | Game | **no** | partial of World; `File.ReadAllText(worldDir/golf.json, gauntlet.json)` |
| World.Dragon.cs | Game | Game | **no** | partial of World; a tick stage of its own |
| World.Hold.cs, Progress, Defense, Raiders | Game | Game | **no** | partial of World; Theme, BuildingRaster |

Tests: every row is covered by name (ChemistryTests and a test file per feature). No direct tests for Flash, Machines,
Terraform's craters, or Acts.

### 3b. Rules, cells, commands, players

| File(s) | Class | Dest | Move as is? | Notes |
|---|---|---|---|---|
| Cell, Chunk, ChunkCells, ChunkStreaming, RollingHash, WorldHash, JoinSnapshot, StandKeeper, Room, Sky, PropState, PlayerId, TempMaterials, Components, ModAdds, ModPatch, RuleSet, Rules.Lapse, Rules.Liquids, Rules.Mix, Rules.Mods | Core | Sim | yes | `Cell.cs:25,176` default terrain is `"dirt"`; `Rules.Liquids.cs:56` is `"water"` |
| RuleTables.cs | Core, with our content | Sim | yes | `BaseElements` (`:685`), `FogGas`, Golf and Dragon params in SimParams (`:593-599`); parses fauna, buildings, looks (`:731, 809-813`). `Load(dir)` at `:756`, `FromJson` at `:765` |
| RuleSchema, Rules.Check, CheckComponents, CheckItems, CheckProducts, CheckSchema | Core, with our checks | Sim | yes | fixed product slots (`CheckProducts:16`), fire looks (`Check:289`), `sim.dragon.*` (`Check:120`), `LookScopes.ShapeProblems` (`Check:315`) |
| MaterialGroups | Core | Sim | yes | `FreezesWater="ice"`, `BoilsWater="steam"` (`:91`) |
| TickBatch, Effects | Core | Sim | yes | the Dragon command list (`TickBatch:84`); WorldThings |
| Rules.CheckFauna, Rules.Buildings, Rules.Things, Rules.Triggers, Rules.Lay | Game rules tables | Sim (for now) | yes | partials of `Rules`, so they stay with it until rule extensions exist |
| Combos, Items, Hands, HandsPick, ElementHold, Placement, RowPad, Wing | Game | Sim (for now) | yes | used by World partials |
| Defense, Downed, Breath, Hold, HoldProgress, PlayerStats | Game | Game later, Sim for now | yes | `Hold.cs:258,277` reads `<world>/hold.json` |
| SpellGroups, SpellList, SpellRadial, RadialLayout, Colors, FallWatch | Game (UI and view maths) | Game | yes, but better left out | nothing in the sim uses them; SpellGroups is a `Rules` partial |
| ModCatalog | Adapter | Game, or behind a mod-source interface | yes | reads `*.json` in two directories |
| ModMove, PlayerSave | Adapter | Game | no | moves and copies files; `progress.json` |
| ModValidator | Game, partly Core | Game | no | `GameFolders`, `budgets.json`, `looks.json`, and the map's places, buildings and things |
| GameFolders | Adapter | Game | no | finds `rules/materials.json` by walking up, `world/maps/<id>`, `claude-terminal/`, API key files |

Gaps: no direct tests for SpellGroups, ComboDef, LandCodec, or any single Check* rule. MaterialGroups and ModPatch are
tested only indirectly.

### 3c. World generation and map data

| File(s) | Class | Dest | Move as is? | Notes |
|---|---|---|---|---|
| Buildings, BuildingsShown, Fauna, FaunaGraft, LandFile (LandChunks), WorldThings, WorldBuildings | Game rules data | **Sim** | yes | the sim compiles against them (World.Buildings, Place, Things, Hold; RuleTables; ModPatch). WorldThings and WorldBuildings also have `Load(dir)` |
| GeneratedSource | Adapter | Sim (the `IGeneratedSource` interface), Gen (the class) | yes | World.cs, World.Chunks, World.Pack and ChunkStreaming use the interface |
| BakedSource | Adapter | Sim or Game | yes | `<world>/cells.txt`; a `World` partial |
| ChunkGen, ChunkGen.Known, Land, Rivers, Spawns; WorldPlan and its seven parts; WorldBiomes, WorldSketch, WorldNoise, KnownRing, LandmarkIntent, Village, Places, FarLandmarks, BiomeField | Game | Gen | yes | they don't need `World`, except that ChunkGen uses `LandStamp` (World.Land.cs) through MapStamps. Parallel.For in ChunkGen.Land (`:259`). Floats in WorldNoise, BiomeField and ChunkGen.Land |
| Theme* (Blueprint, Field, Geometry, Registry, Schema) | Game, with Math | Gen | yes | ThemeRegistry reads `<world>/looks.json` and `themes/*.json` |
| SeaMask, ValueNoise, BiomeMath, RoadGrade, ThemeGeometry | Math (generic) | Gen | yes | integer-only or free of dependencies; the best candidates for a Punchy terrain bridge |
| TerrainGrid | Math (view) | Gen or Game | yes | needs only the chunk constants and `ChunkStreaming.Within` |
| CellHeightfield, CellPaint, CellContour, GroundPack, LookFamilies, LookScopes | Game (view maths and looks) | Game, but LookScopes stays with the sim | yes | CellHeightfield and CellPaint read `World` (Parallel.For). LookScopes is needed by `Rules.Check` |
| GeneratedMap, GeneratedMap.Bake, MapCheck, MapFile, MapIdentity, MapInputs, MapStamps, WorldPuzzles | Tool, Game, Adapter | Game | yes | they hard-code the `world/maps/<id>/` layout: map.json, cells.txt, land.json, things.json, places.json, stamps.json, villages.json, buildings.json, golf.json, puzzles.json, hold.json, biomes.json, bands.json, `squares/*.json`, `themes/`. GeneratedMap.Bake uses Parallel and Task |

### 3d. Tools linked into Game.Core

| File(s) | Class | Dest | Notes |
|---|---|---|---|
| Mcp/McpRpc, RuleFields, RulesTools, Simulator, Workbench (18 partials) | Tool | Game, as a `Chemistry.Mcp` assembly | Workbench uses GameFolders and the game modes (Dragon, Fauna, Buildings, Places) |
| Mcp/ApiBudget, ApiLoop, ApiModels, ChatHarness, ChatProfile, ChatRecord, ClaudeRun | Tool (chat) | Game | used by Scripts (ApiChat, ApiWire, ChatBar, ChatSetup...) |
| Mcp/StandInValley, BodyShape | Game | Game | |
| Scripts/Playtest/Logic/* (6 files) | Game (bot logic) | Game | in Assembly-CSharp in Unity; only Game.Core links it |

## 4. Dependency clusters

```
  Tools (Mcp, Playtest/Logic)  ──►  everything below            [nothing points back]
  Game view maths (CellHeightfield, CellPaint, Spell radial UI) ──►  Sim
  Map pipeline (GeneratedMap, MapFile, MapCheck, MapStamps, WorldPuzzles) ──►  Gen, Sim
  Gen (WorldPlan, ChunkGen, Theme*, Biome*, SeaMask, noise) ──► LandStamp (Sim) only
  Sim ◄──► game modes (Golf, Course, Dragon, Hold, Defense, Raiders, Progress): one partial class
  Sim ──► game rules data (Buildings, Fauna, WorldThings, LandChunks, LookScopes.ShapeProblems)
  Adapters (GameFolders, ModCatalog, ModMove, PlayerSave, *.Load(dir)) used by Sim, Gen and the game
```

## 5. Risks and open questions

1. **Churn and rename conflicts** (section 7): moving the folder renames every file under an open branch.
2. **Our game in the package.** Pass A ships Golf, Dragon, Hold and our base elements to Punchy as dead code.
   That's harmless, but the package would be "our engine", not a neutral one. Is that acceptable to Punchy's owner?
3. **The analyzer reference.** `Chemistry.asmdef` references `Prototype.Analyzers`, which lives in our `Assets/`. In
   Punchy that reference won't resolve: drop it from the package asmdef, or put the analyzers in the package.
   `Directory.Build.props` also points at `unity/Assets/Default.globalconfig`.
4. **Unity versions differ:** ours 6000.3, Punchy 6000.5. The package should declare a minimum of `"unity": "6000.3"`
   and depend on `com.unity.nuget.newtonsoft-json` 3.2.2 explicitly; Punchy has it only as another package's dependency.
5. **Determinism across hosts.** There are doubles in sim decisions (`World.Sim.cs:627-642` SpreadChance, weather
   chances, dryness) and floats in world generation. These are fine while every peer runs the same code. A Punchy
   peer on IL2CPP and one of ours on Mono have never been compared.
6. **Tests can't come along yet.** They need our rules and maps. A small sample ruleset (Ticket 4) only works once
   `BaseElements` and the fixed ids don't force our content. Before that, the package's own CI would test nothing.
7. **Punchy has no licence file.** The October permission (fauna, then "the rest" for vendoring) was for borrowing
   from Punchy into our game. Forking Punchy and adding our package to it is a new ask.
8. **Open question for the plan:** what does Punchy actually need? Chemistry on its terrain needs the sim and rules
   (pass A covers it). A terrain bridge would add SeaMask, noise and maybe TerrainGrid. Neither needs our world
   generation.

## 6. How to move it: two ways, with what each costs

### A. Lift and shift (recommended first)

Move `unity/Assets/Prototype/Chemistry/*.cs` (not `Mcp/`, not `Tests/`) with their `.meta` files, so GUIDs are kept, into
`packages/com.anweather.chemistry-core/Runtime/`. Add it to `unity/Packages/manifest.json` as `file:../../packages/...`.

| Change | Files |
|---|---|
| Renamed with no edits (`.cs` and `.meta`) | ~165 `.cs` + ~165 `.meta` + the asmdef and `CLAUDE.md` |
| New | `package.json`; a `Chemistry.Mcp.asmdef` beside `Mcp/` (autoReferenced, Newtonsoft, references Chemistry) |
| Edited | `Chemistry.asmdef` (the analyzer reference), `manifest.json` and `packages-lock.json`, `Game.Core.csproj` (one glob), `quality.yml` and `duplicates.yml` (path filters, the jscpd path), `tools/workers/queue.test.mjs`, root `CLAUDE.md` (the map); 19 docs name the old path (most are dated briefs and logs, left as they are) |
| C# edited | **0** |
| Unchanged | Scripts (they still see `Chem` through autoReferenced), Chemistry.Tests (references by name), the five other core projects, the golden hashes |

Verify with the three `dotnet` gates (same 2,134 tests), a full EditMode run, `probe.sh smoke`, a player build, and a
fresh Unity project that imports the package alone. About half a day, including the Editor's full reimport. It's
fully reversible: revert one commit.

What it buys: a real, versioned boundary that another project can pin by git URL with `?path=` and a tag, and an
assembly with no Unity or Punchy dependency. What it doesn't buy: a neutral core.

### B. Carve a true sim core

All of A, plus these changes, each its own golden-checked commit:

| Step | C# files edited (rough) | Risk |
|---|---|---|
| Turn the game modes into extensions: hooks for tick stages, save/load, acts, spell and hand targets; move Golf, Course, Dragon, Hold, Progress, Defense and Raiders out | 15-20 World partials + 8 moved and rewritten + TickBatch | **Tick order and the save format.** Goldens, LateJoin, and lockstep between versions |
| Rules extensions: fauna, buildings, hold, golf, dragon and look tables registered by the host; split their checks | 8-12 (RuleTables, the Rules.Check* files, Rules.Mods, ModAdds, ModPatch, ModValidator) | rule fingerprints, and with them mod and room compatibility |
| Content ids into rules data (base elements, dirt, water, ice, steam, fog, product slots) | 6-8 C#, plus `rules/*.json` through the rules MCP | every golden that hashes rules or cells |
| Content behind a host interface: `GameFolders` and the `*.Load(dir)` loaders | 15-20 | small, but wide |
| Split Gen into its own assembly | asmdefs, plus `LandStamp` moved out of World.Land | small |

That's roughly 50-70 C# files and some rules files, so weeks of u2's time, with other core work stopped or rebased
around it. It's the version Punchy would really want, but it's only worth doing once there's a concrete Punchy use
that our game modes get in the way of.

**Recommendation:** do A. Do B in the steps above inside the package later, only on a real need. The package lives in
this repo, so carving later costs no more than carving now.

## 7. Is now the time?

**No, not this week.**

- **Churn on the core:**
  - last 3 days: 63 commits, 134 files
  - last 7 days: 205 commits, 309 files
  - last 30 days: 589 commits, 618 file paths, which means essentially every file, tests included
- **Open branches that touch the core:**
  - three from today: `view-mem`, `sea-land-speed` and `desert-canyons` (which is WIP and blocked)
  - about 16 older unmerged `wip/u2-*` and other branches, from 11-01 to 11-11, some of them probably dead
- **What the game is focused on:** memory, 60 fps, the island and the desert map. All of that is core-side work.

A pass-A move is a rename, and git follows renames. Still, every open branch would rebase across a 330-file move,
and the parallel worktree setup has been bitten by smaller moves (the soft-reset incident). Pass B now would collide
with every core job.

**A good moment for pass A:**
1. `view-mem` and `sea-land-speed` have landed, and `desert-canyons` has landed or been parked.
2. The stale `wip/*` branches have been sorted: kept with an owner, or deleted.
3. The next alpha has been cut. That gives a released, tagged baseline whose goldens and player build are known
   good, which is the rollback checkpoint `plan.md` asks for.
4. No u1 or u2 core job is in flight for the half day the move takes.

## 8. Extraction order (pass A)

1. Phase 0: record dev's and Punchy's commits; re-run this section 2 baseline on the day.
2. Create `packages/com.anweather.chemistry-core/` with `package.json` and the Runtime asmdef. No sources yet.
3. Give `Mcp/` its own `Chemistry.Mcp.asmdef` in place, and confirm the game still compiles.
4. Move `Chemistry/*.cs` and their `.meta` files in one commit. Repoint `Game.Core.csproj`, CI and the docs.
5. Verify (section 6.A), then import the package alone in a clean Unity 6000.3 project.
6. Tag `chemistry-core-0.1.0`. Only then talk about Punchy (Ticket 7), after its owner agrees.

## 9. Not moving in the first pass

- `Chemistry/Tests/` (all of them; they need our rules and maps), `core/Game.Core.Tests` and its helpers
- `Chemistry/Mcp/` (gets its own assembly, stays in the game), `Scripts/Playtest/Logic/`
- `core/Rules.Mcp`, `Playtest.Runner`, `Playtest.Text`, `Hold.Runner`, `Chat.Bench` (they stay consumers)
- `rules/`, `world/` and every content file
- Everything under `Scripts/` (view, input, net, editor builders)
- No C# changes of any kind, including the hard-coded ids, the game-mode partials, `GameFolders` and the loaders.
  They move unchanged with the rest; carving them out is pass B.
