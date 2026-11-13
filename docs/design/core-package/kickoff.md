# Coding-agent kickoff prompt

You are working on the Chemistry Core extraction and Punchy integration. Read `CHEMISTRY_CORE_PUNCHY_IMPLEMENTATION_PLAN.md` before making changes.

## Your immediate assignment

Work on **Ticket 1 only: Audit core source dependencies**.

Repositories:
- Original game: https://github.com/anweather/5e-compatible-game (branch `dev`)
- Punchy: https://github.com/Skeleman/Punchy (branch `main`)
- Proposed new core repository: `anweather/chemistry-core` (not yet created)

Do not create repositories, move files, modify either game, or push commits as part of this first assignment.

## Deliverable

Produce a file-by-file audit table for every source file compiled by `core/Game.Core/Game.Core.csproj`, plus relevant Unity assembly definitions and test projects.

For each file report:
- Current path.
- Namespace and key public types.
- Dependencies on other source files.
- Unity or other engine dependencies, if any.
- Filesystem/content-layout assumptions.
- Whether it is core domain, reusable math, original-game feature, tooling, or host adapter.
- Recommended destination.
- Whether it can move unchanged, needs adaptation, or should remain in the original game.
- Tests that cover it and missing tests.

Also identify:
- Current .NET target framework and language settings.
- External NuGet dependencies.
- Unity package dependencies relevant to the Chemistry assembly.
- How `Game.Core.csproj` links sources from the Unity project.
- How rules/content files are located and loaded.
- Existing deterministic/golden-hash tests and baseline commands.
- Any build/test commands that require Unity Editor.

## Architecture constraints

- Exactly one canonical source copy after extraction.
- Core must not depend on UnityEngine, Unity.Entities, Unity.NetCode, or Punchy.
- Both Unity projects should eventually consume the same immutable versioned package.
- Original game must remain buildable and playable.
- Punchy-specific integration stays outside core.
- Do not refactor simulation behavior during extraction.
- Do not begin Punchy world integration until package extraction and original-game package consumption are proven.

## Output format

Create a concise audit report with:
1. Executive summary.
2. File-by-file classification table.
3. Dependency graph or dependency clusters.
4. Build/test baseline.
5. Risks and unresolved questions.
6. Recommended extraction order.
7. Explicit list of files that should not move in the first pass.

Stop after delivering the audit. Wait for review before implementing Ticket 2.
