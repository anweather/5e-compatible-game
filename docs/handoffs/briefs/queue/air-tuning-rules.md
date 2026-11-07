Needs: core
Area: rules/ (Gust of Wind and Whirlwind entries, and any new field), unity/Assets/Prototype/Chemistry/World.Spells.cs, unity/Assets/Prototype/Chemistry/World.Raiders.cs, unity/Assets/Prototype/Chemistry/Tests/AirTests.cs
Branch: air-tuning-rules

# Gust and Whirlwind numbers move into the rules (follow-up to aimed-spells, a83b608b)

`aimed-spells` tuned Gust of Wind and Whirlwind with constants in the core, because the rules MCP was down
on the PC. `rules/` is the single source of truth for what spells do, so move them there:

- **Gust** (`World.Raiders.cs`): push 4 cells tapped, 8 held; half the second shove, none after
  (`MostShoves = 2`); a stagger (`ShovedStatus`); a fall over a cliff hurts (`CliffSteps = 4`,
  `FallStepsPerHurt = 2`).
- **Whirlwind** (`World.Spells.cs`): `LiftTicks = 12`, `WhirlThrow = 6`, `WhirlFallHurt = 2`, `WhirlEye = 1`.

Rules:
- Change rules through the rules MCP (`docs/RULES_MCP.md`, D13), not by hand. Prefer the material verbs and
  existing fields over a field for one spell (STATE's standing rules); if a new field is needed, make it general
  (e.g. a push or lift any spell can carry) and add its word to `docs/GLOSSARY.md`.
- **Behaviour must not change.** The `AirTests`, golf and whirlwind goldens stay exactly as they are on dev
  (`CellHash` / FNV-1a unchanged). A changed golden means a mistake, not a new baseline.
- Don't port to the sandbox (frozen). Don't touch the view (`Scripts/`): the PC compiles Unity.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet format core --verify-no-changes`,
`dotnet test core`. Land with `land.sh here` (no `--compiled`: the core files are linked into Unity, so say
in the landing message that the PC owes an EditMode run).
