# The Chem core (`packages/com.anweather.chemistry-core/`)

The Unity package `com.anweather.chemistry-core`; its sources are in `Runtime/`
(the Chemistry assembly), and our game uses it by local path from
`unity/Packages/manifest.json`. The rules MCP and chat tools
(`unity/Assets/Prototype/Chemistry/Mcp/`, the Chemistry.Mcp assembly) and the
tests (`unity/Assets/Prototype/Chemistry/Tests/`) stay in the game.

The engine-free rules engine: cells in 64×64 chunks, one deterministic tick
at a time, with behaviour read from `rules/*.json`. `docs/ARCHITECTURE.md`
section 2 walks through the files; `docs/WORLD_STREAMING.md` covers chunks.

## Hard constraints

- **No Unity.** `Runtime/Chemistry.asmdef` sets `noEngineReferences`; only .NET and
  Newtonsoft.Json. `core/Game.Core` compiles these same files as
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
under `dotnet test core` (via `core/Game.Core.Tests`). Run one with
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
