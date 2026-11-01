// The simulate tool: the draft's run against the same run without it.
using System;
using System.Globalization;
using System.IO;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        /// <summary>Runs a scenario for some ticks with the patch on, then without it, and compares.</summary>
        public string Simulate(string draftOrMod, string scenario, int? ticks = null)
        {
            var (with, without) = SimulateRuns(draftOrMod, scenario, ticks);
            double min = Budgets.Number("simulate.minTicksPerSecond", 300);
            double maxSlowdown = Budgets.Number("simulate.maxSlowdown", 2);
            double slowdown = without.TicksPerSecond / Math.Max(with.TicksPerSecond, 1e-6);
            string verdict = with.Hash == without.Hash
                ? "The mod changed nothing in this scenario (same cell hash)."
                : $"The mod changed the outcome (a different cell hash): {with.CellsChanged} cells changed with it, {without.CellsChanged} without.";
            string cost = slowdown > maxSlowdown
                ? $"It ticks {slowdown.ToString("0.0", CultureInfo.InvariantCulture)} times slower than without it, past the budget of {Num(maxSlowdown)}."
                : $"It ticks at {(1 / slowdown).ToString("0.00", CultureInfo.InvariantCulture)} times the speed without it (the budget allows down to 1/{Num(maxSlowdown)}).";
            return $"Scenario {scenario}: {Simulator.Scenarios[scenario]} {with.Ticks} ticks ({with.Ticks / Base.Sim.TicksPerSecond} s of game time).\n\n" +
                $"With the mod:\n{Simulator.Describe(with, min)}\n\nWithout it:\n{Simulator.Describe(without, min)}\n\n{verdict} {cost}";
        }

        /// <summary>The two runs (with the patch, without it), for simulate and the tests.</summary>
        public (SimRun with, SimRun without) SimulateRuns(string draftOrMod, string scenario, int? ticks = null)
        {
            scenario = Required(scenario, "scenario").ToLowerInvariant();
            if (!Simulator.Scenarios.ContainsKey(scenario))
                throw new RulesToolException($"There is no scenario {scenario}; the scenarios are {string.Join(", ", Simulator.Scenarios.Keys)}.");
            int max = (int)Budgets.Number("simulate.maxTicks", 1200);
            int n = ticks ?? (int)Budgets.Number("simulate.defaultTicks", 300);
            if (n < 1 || n > max) throw new RulesToolException($"Simulate runs 1 to {max} ticks, not {n}.");
            var rules = RulesWith(PatchJson(draftOrMod));
            // Without it first, so warming up the runtime doesn't count against the mod.
            string repoRoot = Path.GetDirectoryName(Path.GetFullPath(RulesDir));
            var without = Simulator.Run(Base, scenario, n, repoRoot);
            return (Simulator.Run(rules, scenario, n, repoRoot), without);
        }
    }
}
