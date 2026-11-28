# The Chem core (`packages/com.anweather.chemistry-core/`)

The Unity package `com.anweather.chemistry-core`; its sources are in `Runtime/`
(the Chemistry assembly), and our game uses it by local path from
`unity/Packages/manifest.json`. The rules MCP and chat tools
(`unity/Assets/Prototype/Chemistry/Mcp/`, the Chemistry.Mcp assembly) and the
tests (`unity/Assets/Prototype/Chemistry/Tests/`) stay in the game.

The engine-free rules engine: cells in 64×64 chunks, one deterministic tick
at a time, with behaviour read from `rules/*.json`. `docs/ARCHITECTURE.md`
section 2 walks through the files; `docs/WORLD_STREAMING.md` covers chunks.

## Four assemblies

- **`Runtime/Land/` is `Chem.Land`** (its own asmdef; `core/Chem.Land` in dotnet): hills, ChunkGen, world
  plans, themes, biomes, noise, the map files, `GeneratedMap`, `Places` (reading). It never uses `World`,
  `Cell`, `Rules` or anything else in `Runtime/` (a use there is a compile error).
- **The rest of `Runtime/` is `Chemistry`** (`core/Game.Core`), which references `Chem.Land` and sees its
  internals. The land's adapters onto a world stay here: `World.Land`, `GeneratedSource`, `BakedSource`,
  `MapBake` (a generated map's bake: it writes Play's rows), `GeneratedPlaces`, `MapCheck`,
  `MapStampsLaying`, `CellHeightfield`, `OpenWorld*`. `docs/design/chem-land-split.md` says why.
- **`Runtime/Play/` is `Chem.Play`** (its own asmdef; `core/Chem.Play` in dotnet), above `Chemistry`, which never
  references it: the defense field's, the Hold's and the course's systems (`DefenseSystem`, `HoldSystem`,
  `CourseSystem`, `WayField`), `PlayModes` (what installs them on a world) and `WorldPlay` (`world.Play()` and the
  mode-only reads). Their state stays the world's, with its text, hash and save (`World.Play.cs`), so a save or a
  checksum doesn't depend on them; the world calls them through `IWorldModes`. A world that plays a mode is made
  with `PlayModes.Install(world)` (a joiner's takes the modes of the world it replaces, `InstallModesOf`); one
  without them throws where a mode would have run. Raiders and the dragon stay in `Chemistry` (spells, acts and the
  change audit reach them). A test that plays one of the three goes in `Chem.Play.Tests` and installs them.
- **`Runtime/Golf/` is `Chem.Play.Golf`** (its asmdef; `core/Chem.Play.Golf`), a mini-game on `Chemistry` alone (D22,
  `docs/design/minigames.md`): `GolfMode`, a `WorldMode` installed with `GolfMode.Install(world)`, its state in its
  own slot, its ball a `Body`; `world.Golf()` and `ApplyGolf` are its extensions. Its tests are the Chemistry tests'
  `Golf*.cs` (`core/Chem.Play.Golf.Tests`). A new mini-game is another folder like it, by the recipe in
  `docs/design/minigames.md`, and changes nothing in `Runtime/*.cs`.

## World modes (D22)

`WorldMode.cs` is the contract every mode derives from (`PlayModes` and `GolfMode` do): the world calls installed
modes in install order for their slots (save, casts checksum text, the dragon's start, each at the one place the
world calls them), the events (a map laid, a chunk the dragon reset, a spell striking or landing on a body, a body's
steps) and acts of a kind the world doesn't know. `Body.cs` and `World.Bodies.cs` are the thrown or rolled bodies the
world steps after the boulders. A save loads before its modes are installed: the world keeps it until the first
tick and hands each mode its part as it's installed, so install modes before a world ticks. A mode uses only the
core's public members (no `InternalsVisibleTo` for it).

## Hard constraints

- **No Unity.** `Runtime/Chemistry.asmdef`, `Runtime/Land/Chem.Land.asmdef`, `Runtime/Play/Chem.Play.asmdef` and
  `Runtime/Golf/Chem.Play.Golf.asmdef` set `noEngineReferences`; only .NET and Newtonsoft.Json. `core/Game.Core`,
  `core/Chem.Land`, `core/Chem.Play` and `core/Chem.Play.Golf` compile these same files as
  netstandard2.1 with **C# 9** (Unity's limit), so no newer syntax.
- **Deterministic everywhere** (lockstep, D3/D4): integer state, all
  randomness from the world's seeded generator, no mutable statics, no
  culture-dependent string calls, ordered iteration (`Step` visits active
  chunks as row segments in world row-major order, never the store's
  order).
- **Ported from the TS sandbox** (`sandbox/src/*.ts`), which is now frozen
  (J-042). File headers say which TS file each one came from. A behaviour
  change goes here only, with a C# golden (`World.CellHash`, FNV-1a, in
  `TerraformTests.cs` / `ChemistryTests.cs`); don't port it to the sandbox.
- **ECS-ready (D9, `docs/adr/0009-*`).** New code keeps data apart from
  systems, reaches cells through accessors (`World.At`), prefers integer
  ids over strings in cell state, and changes shared state only through
  commands (casts, `EffectCommand` via `World.ApplyEffect`, rule switches).

## Changing it

- Rule checks live in `Rules.Check.cs` / `Rules.CheckItems.cs`. Their
  messages started as the frozen sandbox's `validateRules`; no need to match it now.
- A new effect kind from the view goes in `Chem.EffectKind` and
  `World.ApplyEffect`.
- `S3776` (cognitive complexity) is the only warning allowed to stay; the
  four methods carrying it were ported from the TS and can be split freely.
  Everything else fails `dotnet build core -c Release -warnaserror`.

## Tests

`unity/Assets/Prototype/Chemistry/Tests/` holds NUnit 3 tests that run both in Unity's EditMode runner and
under `dotnet test core` (via the `core/Chem.<Module>.Tests` projects, `core/Chem.Tests.props`). A land test that
needs a World, the rules or `ChemistryTests` goes in its fixture's `*WorldTests.cs` half (`Chem.LandWorld.Tests`):
`Chem.Land.Tests` builds on `Chem.Land` alone, with `LandFolders` for the repo's folders. Run one with
`dotnet test core --filter "FullyQualifiedName~<Name>"`. Tests read the real
`rules/` folder through `ChemistryTests.RulesDir()`, and some read docs
(`docs/MODDING.md` examples must pass the checks). Every fixture is
`[Category("Unit")]` (a rule, a condition, an effect: at most a small made-up
world, tens of milliseconds a test) or `[Category("Simulation")]` (a world's
ticks, chunks, peers, generation); a test checks it. `dotnet test core --filter
TestCategory=Unit` runs the Unit ones in seconds (docs/TESTING_LAYERS.md).
A new fixture needs its category before it lands, or that test fails on dev.

Unity's NUnit is older than the one `dotnet test` uses, so a test can pass
under dotnet and still fail to compile in the Editor. One known case:
`Does.Contain(x)` takes only a string there. For a member of a list, use
`Has.Member(x)` / `Has.No.Member(x)`.
