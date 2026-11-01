Brief from the PM: the rules MCP's fireball scenario, its warnings, and the core's tick cost in storm and fog (Pro account; Andrew wants Pro used before its weekly reset).

You're a worker in a VM, running headless, with no Unity: core only (the sandbox is frozen; don't touch it). Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../core-tick-cost origin/dev -b core-tick-cost` and work there. Read the root CLAUDE.md and `unity/Assets/Prototype/Chemistry/CLAUDE.md`. The rules MCP tools now live in `unity/Assets/Prototype/Chemistry/Mcp/` (the game serves them; `core/Rules.Mcp` hosts the same tools over stdio).

1. **A fireball scenario for `simulate`** (`Simulator.cs`, `Workbench.Simulate.cs`). Today the scenarios start with a Fire Bolt, so a draft that makes Fireball bigger shows nothing. Add a scenario that casts Fireball on grass (and whatever the existing scenarios do to report), so "radius 2 to 4" shows as burnt cells, peak fire and the hash. Mention it in the tool's description. Test it in `RulesMcpTests.cs`: the Bigger fireballs mod changes the result.
2. **The three S3776 warnings** (cognitive complexity) in `Simulator` and `Workbench.Read` (two): split the methods so they go, no behaviour change. The Mac's parallel build turns S3776 into an error, so build once with `-m:1` too.
3. **The tick's cost in storm and fog** (GitHub #27): a tick costs about 39 ms in storm and 78 ms in fog (18 ms in the meadow); storm's cost is flow (about 15 ms a tick), fog's is likely gas over about 220 000 cells. Measure each stage of `World` tick in a core benchmark for the storm and fog weather (add one under `core/` if none fits; Release build, a fixed seed, a warm-up), find the biggest costs, and make them cheaper. **Determinism is the rule:** every golden hash (`CellHash`, the FNV-1a goldens in `TerraformTests.cs`, `ChemistryTests.cs` and the rest) must stay byte for byte the same; integer state, ordered iteration, no new allocation per tick. Report before and after ms per tick per stage. Stop at the first change that doesn't keep the goldens, and say so. The view's tick-debt cap (`ChemGrid`) is not yours: the PC does it.

Checks: `dotnet build core -c Release -warnaserror` (and once with `-m:1`), `dotnet test core`, `dotnet format core --verify-no-changes`, jscpd as in CLAUDE.md.

Deliver: commit and push `core-tick-cost`, not merged (one commit per item is fine). Final message, 12 lines or fewer: the commit; one line per item; storm and fog ms per tick before and after, by stage; goldens unchanged (yes or no); the time.
