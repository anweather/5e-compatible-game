# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A 3D RPG in Unity on the open 5E rules (SRD 5.2.1), built around deep,
physics-based interactions between spells ("spell chemistry"). Say
"5E-compatible", never "Dungeons & Dragons". More specific guidance loads
from the `CLAUDE.md` in each area as you work there: `unity/`,
`unity/Assets/Prototype/Chemistry/`, `unity/Assets/Prototype/Scripts/`,
`sandbox/`, `rules/`.

## Before starting any work

Get up to date with main first. More than one machine works on this repo
(the Windows PC is the main one; a Mac too):

```
git fetch origin
git switch main
git pull --ff-only
```

If the pull won't fast-forward or the tree has local changes, stop and say
so rather than discarding anything. In particular, never revert a scene or
prefab while the Unity Editor has it open (the "modified externally" alert
blocks the Editor); rebuild it with its builder menu instead.

## Committing

Nothing is live yet, so commit and push straight to `main`; no branch or
PR is needed. Pull again just before pushing. Don't commit the churn the
Editor leaves behind after a rebuild (`ChemistryYard.unity`, URP assets
upgraded by a newer Editor, `unity/.vsconfig`) unless that change is the
point.

## The map

| Folder | What it is |
|---|---|
| `rules/` | The game's interaction rules as JSON. The single source of truth for what spells, materials and reactions do |
| `unity/Assets/Prototype/Chemistry/` | The engine-free C# core (namespace `Chem`): loads the rules, runs the cell world one tick at a time, deterministically |
| `unity/Assets/Prototype/Scripts/` | The Unity view: draws the world, input, the wizard, props, items, HUD; `Net/` is the multiplayer layer; `Editor/` builds scenes and players |
| `core/` | .NET projects that *link* the Chemistry sources and tests from `unity/` so they build and test without Unity (CI runs these) |
| `sandbox/` | A TypeScript web paper prototype running the same `rules/`; its sim mirrors the C# core |
| `tools/probe.sh` | Runs a scripted scenario in the open Editor and prints pass/fail JSON (`docs/FAST_LOOP.md`) |
| `docs/` | Design and engineering docs; start with `ARCHITECTURE.md` (layers, patterns, "where new things go") |

## Commands

```sh
# C# core (what CI's "quality" workflow runs); the Editor isn't needed
dotnet build core -c Release -warnaserror
dotnet test core
dotnet test core --filter "FullyQualifiedName~EveryExampleModPassesTheChecks"   # one test
dotnet format core --verify-no-changes        # drop --verify-no-changes to fix
npx --yes jscpd@4.0.5 unity/Assets/Prototype sandbox/src   # duplicate-code gate (2%)

# Sandbox (CI's "sandbox" workflow)
cd sandbox && npm ci && npm run typecheck && npm test && npm run build

# In the open Unity Editor (see unity/CLAUDE.md)
unity command test run --mode EditMode --result-only
tools/probe.sh smoke
```

## Invariants that span folders

- **One rule set, two engines.** `rules/*.json` drive both the C# core and
  the TS sandbox, and the core mirrors the sandbox line by line. A change
  to chemistry behaviour changes both, with a shared golden test (the
  `CellHash` / FNV-1a goldens in `TerraformTests.cs`, `ChemistryTests.cs`
  and `sandbox/test/terraform.test.ts`).
- **Lockstep multiplayer.** Every machine runs the same world from the
  same inputs (cast intents, `EffectCommand`s, rule switches), so the core
  must stay deterministic: integer state, the world's seeded RNG, ordered
  iteration, no Unity. Physics never decides shared state (D4).
- **ECS-ready core (D9).** Unity Entities is out for now; new core code
  follows D9's seams (data apart from systems, integer ids in hot state,
  cells through accessors, tags in the rules, commands as the only way in).
- **Scenes are generated.** Change the builder and run its menu item
  (Prototype > Build Chemistry Yard); never hand-edit `.unity` or
  `.prefab` YAML.
- **Decisions are ADRs.** `docs/DECISIONS.md` is the index, one file per
  decision in `docs/adr/` (template in `docs/adr/README.md`). Features to
  build next are in `docs/BACKLOG.md`.
- **Line endings are LF** (`.gitattributes`); C# style is in
  `.editorconfig` (Allman braces, 4 spaces), checked by `dotnet format`.
