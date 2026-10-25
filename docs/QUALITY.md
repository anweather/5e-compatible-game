# Code quality checks

Automatic checks on our C#, so problems get caught without anyone needing to
read the code. There are two layers:

| Layer | Where you see it | What runs | Blocks anything? |
|---|---|---|---|
| 1. Analyzers | Unity's **Console**, as yellow warnings, every time scripts compile | Sonar, Unity and .NET analyzers on our code | No. Warnings never stop the game compiling |
| 2. Quality gate | GitHub, the **quality** check on every push and PR | Build of `Game.Core` (warnings fail), its tests, formatting, duplicate code | Yes. A red check means fix before merging |

Only our code is checked: `unity/Assets/Prototype/` and `sandbox/src/`. The
Asset Store packs (`Starter Assets`, `UnityTechnologies`) are never analyzed,
with one exception noted below.

## Layer 1: analyzers in Unity

The analyzers are DLLs in `unity/Assets/Prototype/Analyzers/`, labelled
`RoslynAnalyzer` so Unity hands them to its compiler:

| Analyzer | Version | Finds |
|---|---|---|
| SonarAnalyzer.CSharp | 10.35.0.4138 | Bugs, code smells, unused code, methods that are too complex (cognitive complexity over 15), too many parameters (over 7) |
| Microsoft.Unity.Analyzers | 1.28.0 | Unity mistakes: `?.`/`??` on Unity objects, wrong message signatures, slow APIs in `Update` |
| Microsoft.CodeAnalysis.NetAnalyzers | 10.0.402 | .NET mistakes, including culture-dependent string calls that could break the deterministic core |

Unity 6.3's compiler is Roslyn 4.3, so an analyzer must be built against
Microsoft.CodeAnalysis 4.3 or older. All three are built against 3.11 or older.
Sonar's licence (source-available, free to use) is next to its DLL.

**Scope.** Unity runs an analyzer that sits next to an `.asmdef` only on that
assembly and on the assemblies that reference it. `Prototype.Analyzers.asmdef`
(made real by the comment-only `AnalyzerScope.cs`) is referenced by
`Chemistry`, `Chemistry.Tests` and `Prototype.AssetPacks.Editor`, and
automatically by `Assembly-CSharp` and `Assembly-CSharp-Editor` (which hold
`Scripts/` and `Scripts/Editor/`). Packs with their own `.asmdef` and Unity
packages don't reference it, so they aren't analyzed. The exception is
two Starter Assets tutorial files that have no `.asmdef`
(`TutorialInfo/Scripts/Readme.cs` and `Editor/ReadmeEditor.cs`). They compile
into `Assembly-CSharp`, so they get 2 warnings. Ignore those, or see
"Recommendations".

**Severities** are in `unity/Assets/Default.globalconfig`. Unity 6.3 reads
that file for every assembly. It does not read `.editorconfig`. Each rule
that is switched off or turned up has its reason written next to it. The
core/ build reads the same file, so Unity and CI agree.

**Reading a warning:**

```
Assets/Prototype/Scripts/EventLog.cs(68,28): warning UNT0008: Unity objects should not use null propagation.
          file                         line,column      rule        what is wrong
```

Look the rule up by its ID: `S…` at `rules.sonarsource.com/csharp/RSPEC-<number>`,
`UNT…` at `github.com/microsoft/Microsoft.Unity.Analyzers/blob/main/doc/<ID>.md`,
and `CA…`/`IDE…` at `learn.microsoft.com/dotnet/fundamentals/code-analysis/`.

## Layer 2: the CI quality gate

`.github/workflows/quality.yml` runs when C#, rules or this tooling changes:

1. **Build and test.** `core/Game.Core` (netstandard2.1, C# 9: D3's
   engine-free core) compiles `unity/Assets/Prototype/Chemistry/*.cs` from
   where Unity keeps them. `core/Game.Core.Tests` runs the same NUnit tests
   Unity runs in EditMode. Every warning is an error, except cognitive
   complexity (`S3776`, see the baseline). The build also runs .NET's
   `latest-recommended` rules and the naming and code-style rules in the root
   `.editorconfig`.
2. **Format.** `dotnet format core --verify-no-changes` fails if spacing,
   braces or other style in Chemistry doesn't match `.editorconfig`.
3. **Duplicates.** `jscpd` (4.0.5) looks for copy-pasted blocks (50 tokens
   and 5 lines or more) in `unity/Assets/Prototype` and `sandbox/src`. It
   fails if more than 2% of the code is duplicated. The settings are in
   `.jscpd.json`.

The sandbox's own tests stay in `sandbox.yml`.

`Scripts/` (the MonoBehaviours) can't build outside Unity, so CI doesn't
cover it. Its only checks are the Unity console warnings.

**Reading a failure:** open the red **quality** check on the PR, then the
failed job:

- *build-and-test*: an `error XX1234:` line gives file, line and rule, read it
  as above. A `Failed <TestName>` line is a broken test.
- *format*: each line names a file and line that isn't formatted. Running
  `dotnet format core` fixes them all.
- *duplicates*: jscpd lists each clone as two file:line ranges. Move the
  shared code into one method.

## Running it locally

```sh
export PATH="$HOME/.dotnet:$PATH"          # .NET 8 SDK
dotnet build core -c Release -warnaserror  # analyzers, warnings fail
dotnet test core                           # the chemistry and live-mod tests
dotnet format core --verify-no-changes     # drop --verify-no-changes to fix
npx --yes jscpd@4.0.5 unity/Assets/Prototype sandbox/src
```

In Unity: change any script (or **Assets → Refresh**). The warnings appear in
the Console once compiling finishes.

## Baseline (2026-10-25, after the clean-up)

What the analyzers report on our code now. The architecture the code follows
is in `docs/ARCHITECTURE.md`.

| File | Warnings | Rules |
|---|---|---|
| Chemistry/World.React.cs | 3 | S3776 ×3 (complexity 18, 28, 69) |
| Chemistry/World.Sim.cs | 3 | S3776 ×3 (28, 35, 55) |
| Chemistry/World.Spells.cs | 1 | S3776 (22) |
| *Starter Assets ReadmeEditor.cs (pack)* | 2 | S1066, S1199 |

Totals: **Scripts/ 0**, **Chemistry/ 7** (all cognitive complexity), pack 2,
which makes 9 in the Unity console.

Before the clean-up (2026-10-25, the code had grown since the first baseline
of 39) it was **Scripts/ 65**, **Chemistry/ 8**, pack 2: S3776 21, UNT0039 10,
S3358 9, S3973 8, CA1310 6, UNT0024 5, S2325 4, UNT0022 3, UNT0026 2, and
UNT0038, UNT0017, UNT0008, S1905 and S1121 once each. All of them in
`Scripts/` were fixed in the code, by splitting methods and files
(`ChemGrid.Visuals.cs` became `ChemGrid.Visuals.cs`, `ChemGrid.Effects.cs`,
`GridTileBuilder.cs`, `MaterialCache.cs` and `FireLightPool.cs`), ordinal string
comparisons, `RequireComponent`/`TryGetComponent`, and braces. One rule was
swapped rather than fixed: Sonar's S2325 ("make it static") flags Unity
messages such as `OnDestroy`, which Unity calls on an instance, so it is off and
.NET's identical CA1822 is on instead, which the Unity analyzers know to skip
for Unity messages (see `Default.globalconfig`). Naming rules (`IDE1006`) find
nothing in our code. jscpd finds one 5-line clone (the rules-folder lookup in
`ChemGrid` and the tests), 0.08% of the C#.

The Chemistry methods are ported line by line from the TS sandbox. Splitting
them (together with the TS) is the next refactor; then remove `S3776` from
`WarningsNotAsErrors` in `core/Directory.Build.props`.

## Recommendations

- Give `Scripts/` its own `.asmdef` (e.g. `Prototype`, referencing
  `Chemistry`, `Prototype.Analyzers`, Unity.InputSystem and Cinemachine).
  `Prototype.Analyzers` could then stop being auto-referenced, so
  `Assembly-CSharp` (and the two pack tutorial files) would no longer be
  analyzed, and the Scripts would compile separately from the packs.
