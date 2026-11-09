Needs: core
Area: world/maps/drowned-chain/, unity/Assets/Prototype/Chemistry/Tests/DrownedChainTests.cs (MapCheck.cs only if the schema needs it)
Branch: drowned-chain-core

# The Drowned Chain as a map folder (maps phase 8, core half)

Maps phase 8 is "the test" (`docs/design/maps-as-data.md`, the Phase 8 section; issue #140): the host picks a map,
the Drowned Chain is the second one, and two peers play it. This brief is the core half: make the Chain a real,
checked map folder on dev, so the PC can then do the runtime switch (tear down and rebuild terrain in play).

## What to do

- The Chain exists only on branch `second-map` (afff4c9): a theme, things, one village and a baked `land.json`.
  Bring its files over with `git show second-map:world/maps/drowned-chain/...` (don't merge that branch).
- Fit it to today's schema, the way `world/maps/valley` and `world/maps/nine` are: `map.json` with `size`,
  `seaLevel`, `spawn`, `expects`, `sea`, `yard`, `fullDetail`; its theme, `things.json` and one village; no
  `ground/`. Optional files (golf, gauntlet, puzzles) are simply absent. If committing a baked land is heavy or
  stale, make it a generated map like `nine` (a seed, no committed land) instead, and say which you picked.
- Add `DrownedChainTests.cs` (it needs a Unit or Simulation category: `EveryFixtureIsUnitOrSimulation`) showing:
  - MapCheck passes on the folder, and so do its `expects` (land share about 31%, highest about 154 m, sea share about 69%);
  - the world builds from the folder with no `ground/`, and optional files that are missing raise no error;
  - a CellHash golden after N ticks (deterministic, the same on two builds);
  - `MapIdentity.Hash` is stable, and `Refusal` fires for a valley peer joining a host on the Chain.
- The valley's and the nine's goldens stay byte-identical.

## Done

`dotnet build core -c Release -warnaserror`, `dotnet test core` and `dotnet format core --verify-no-changes` all pass
on the branch. Push it. If it touches only data and core, land it with `land.sh`; if any Unity-side C# changes, stop
and report so the PC compiles it. Mind `Chemistry/CLAUDE.md`'s notes on Unity's NUnit (no `Does.Contain(object)`).

After: the PC does the view half (a runtime map switch rebuilds terrain; the client follows `hostMap`).
