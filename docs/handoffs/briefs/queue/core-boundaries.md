Needs: core
Area: .github/workflows/quality.yml; core/*.Tests (new test projects split from Game.Core.Tests); NUnit categories
on Chemistry/Tests; unity/Assets/Prototype/Chemistry/World.React.cs and a new Chemistry/Reactions/ folder;
tools/workers/gates.sh; docs/ARCHITECTURE.md (dependency directions)
Branch: core-boundaries (one commit per step; land each step that passes)

# Core boundaries from the outside review (u2; J-081)

Andrew passed on an outside review (2026-11-08). Kept: the engine-free core, rules as JSON, lockstep, goldens. Taken
from it, in this order, each measured before and after (wall time of `dotnet build core`, `dotnet test core`, a CI
run from `gh run list --workflow quality.yml`, and the slowest 15 tests):

1. **CI:** the dotnet jobs in `quality.yml` stop triggering on `sandbox/src/**` (the jscpd job keeps it: it scans the
   sandbox too). NuGet caching on `setup-dotnet` (`cache: true`, keyed on `core/**/*.csproj`; add lock files if
   needed); pin jscpd in a lockfile-managed `package.json` instead of `npx --yes` each run. Today a run is about
   4.5-5 min. Don't merge the parallel jobs.
2. **Split the test host:** `Game.Core.Tests` references Playtest.Runner, Rules.Mcp, Chat.Bench and Hold.Runner and
   links their tests. Give each its own test project (`Rules.Mcp.Tests`, `Playtest.Runner.Tests`, `Chat.Bench.Tests`,
   `Hold.Runner.Tests`); `Game.Core.Tests` keeps only the Chemistry tests. `dotnet test core` still runs them all;
   the gates stay one command. Every test still runs somewhere; the count before and after must match. Keep
   `Parallel.cs`'s cap of 6 workers (fixtures build whole worlds): don't raise it without measuring.
3. **Categories:** `Unit` (a rule, a condition, an effect: no whole world), `Simulation` (whole-world ticks), and the
   existing `Sweep` (long runs, nightly). `dotnet test core --filter Category=Unit` runs alone in seconds. Say what the
   landing gates should run once you've seen the times; don't change the gates' coverage without saying.
4. **World.React.cs:** `Apply()` (cognitive complexity 69) becomes a short orchestration over a matcher, an executor
   and the reaction log, in `Chemistry/Reactions/` (ReactionMatcher, ReactionExecutor, CellConditions, CellEffects;
   no class per effect). The iteration order, every RNG draw and every golden byte-identical: if a golden moves, stop.
5. **Don't split World.Sim's tick** (the second review, 2026-11-08: its stages, slices and order are worth keeping).
   `SlicedTickTests` and `TickStageBench` already cover sliced-versus-whole equivalence and time per stage; add a
   case only for a stage they miss.
6. **Dependency directions** in `docs/ARCHITECTURE.md`: a short table of which project or folder may reference which
   (Chemistry references nothing of Unity; Scripts may reference Chemistry; tests and tools reference what they test),
   so the next agent sees them.
Layer 1; steps 1-3 need no Unity. Step 4 changes Chemistry/, so the PC compiles it and runs EditMode under Mono.
