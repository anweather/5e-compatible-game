// The simulate tool: the draft's run against the same run without it.
using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        /// <summary>Runs a scenario for some ticks on the rules in force with the patch on, then without it, and compares.</summary>
        public string Simulate(string draftOrMod, string scenario, int? ticks = null, bool? walk = null)
        {
            var (with, without) = SimulateRuns(draftOrMod, scenario, ticks, walk);
            scenario = string.IsNullOrEmpty(scenario) ? Simulator.WalkScenario : scenario.ToLowerInvariant();
            double min = Budgets.Number("simulate.minTicksPerSecond", 300);
            double maxSlowdown = Budgets.Number("simulate.maxSlowdown", 2);
            double slowdown = without.TicksPerSecond / Math.Max(with.TicksPerSecond, 1e-6);
            string verdict = with.Hash == without.Hash
                ? "The mod changed nothing in this scenario (same cell hash)."
                : $"The mod changed the outcome (a different cell hash): {with.CellsChanged} cells changed with it, {without.CellsChanged} without.";
            string cost = slowdown > maxSlowdown
                ? $"It ticks {slowdown.ToString("0.0", CultureInfo.InvariantCulture)} times slower than without it, past the budget of {Num(maxSlowdown)}. apply doesn't time a mod (a timing differs from machine to machine, so it can't decide a room's rules), so cut it down before applying it: fewer, rarer or smaller casts."
                : $"It ticks at {(1 / slowdown).ToString("0.00", CultureInfo.InvariantCulture)} times the speed without it (the budget allows down to 1/{Num(maxSlowdown)}).";
            return $"Scenario {scenario}: {Simulator.Scenarios[scenario]} {with.Ticks} ticks ({with.Ticks / InForce.Sim.TicksPerSecond} s of game time).\n\n" +
                $"With the mod:\n{Simulator.Describe(with, min)}\n\nWithout it:\n{Simulator.Describe(without, min)}\n\n{verdict} {cost}";
        }

        /// <summary>validate's last line for a patch that changes how the world behaves, and not only how it looks: a model that passes it reads the line.</summary>
        public string SimulateNote(string draftOrMod) =>
            ChangesBehaviour(ModPatch.Parse(PatchJson(draftOrMod)).Json)
                ? "\nThat only says it's legal. This changes how the world behaves, so run simulate on it once (fire-on-grass for what burns, flood for water, storm for lightning, walk for a trigger on steps) to see it goes the way the player meant, then apply. It shows the direction, not a multiple: \"twice as fast\" is the field doubled, so don't retune it to fit the numbers."
                : "";

        bool ChangesBehaviour(JObject patch) =>
            patch["reactions"] != null || patch["sim"] != null || patch[TriggersTable] != null
            || MaterialKinds.Any(kind => (patch[kind] as JObject)?.Properties().Any(p => !SameButLook(EntryIn(Raw, kind, p.Name), p.Value)) == true);

        // A patch names only the fields it changes, so what it leaves out stays. A look changes nothing the world does.
        static bool SameButLook(JToken was, JToken now)
        {
            if (was == null) return false;
            if (!(now is JObject fields)) return JToken.DeepEquals(was, now);
            return fields.Properties().All(p => p.Name == "look" || SameButLook(was[p.Name], p.Value));
        }

        /// <summary>
        /// The two runs (with the patch, without it), for simulate and the tests. A draft with a trigger on steps walks a
        /// loop unless <paramref name="walk"/> says not; the walker is the trigger's player, and the scenario then defaults to the walk alone.
        /// </summary>
        public (SimRun with, SimRun without) SimulateRuns(string draftOrMod, string scenario, int? ticks = null, bool? walk = null)
        {
            bool walking = walk ?? HasStepTrigger(ModPatch.Parse(PatchJson(draftOrMod)).Json);
            if (walking && string.IsNullOrEmpty(scenario)) scenario = Simulator.WalkScenario;
            scenario = Required(scenario, "scenario").ToLowerInvariant();
            if (!Simulator.Scenarios.ContainsKey(scenario))
                throw new RulesToolException($"There is no scenario {scenario}; the scenarios are {string.Join(", ", Simulator.Scenarios.Keys)}.");
            int max = (int)Budgets.Number("simulate.maxTicks", 1200);
            int n = ticks ?? (int)Budgets.Number("simulate.defaultTicks", 300);
            if (n < 1 || n > max) throw new RulesToolException($"Simulate runs 1 to {max} ticks, not {n}.");
            var rules = RulesWith(PatchJson(draftOrMod));
            // Without it first, so warming up the runtime doesn't count against the mod.
            string walker = !walking ? null : rules.TriggersOn(TriggerEvent.Step).Select(t => t.Who).FirstOrDefault(p => p != TriggerDef.Each) ?? "Walker";
            var without = Simulator.Run(InForce, scenario, n, walker);
            return (Simulator.Run(rules, scenario, n, walker), without);
        }
    }
}
