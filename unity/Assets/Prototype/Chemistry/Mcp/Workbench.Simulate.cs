// The simulate tool: the draft's run against the same run without it.
using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
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
                ? $"It ticks {slowdown.ToString("0.0", CultureInfo.InvariantCulture)} times slower than without it, past the budget of {Num(maxSlowdown)}. That's a guide, measured on a bare run, not a wall: apply doesn't time a mod (a timing differs from machine to machine). Fewer, rarer or smaller casts cost less, and fire spreading over grass costs far more than the cast that lit it, so a cast that doesn't light the ground may be nearly free. If it's what the player asked for, tell them it may slow the game and apply it: refuse only if they say so."
                : $"It ticks at {(1 / slowdown).ToString("0.00", CultureInfo.InvariantCulture)} times the speed without it (the budget allows down to 1/{Num(maxSlowdown)}).";
            return $"Scenario {scenario}: {Simulator.Scenarios[scenario]} {with.Ticks} ticks ({with.Ticks / InForce.Sim.TicksPerSecond} s of game time).\n\n" +
                $"With the mod:\n{Simulator.Describe(with, min)}\n\nWithout it:\n{Simulator.Describe(without, min)}\n\n{verdict} {cost}";
        }

        public string SimulateNote(string draftOrMod) =>
            ChangesBehaviour(ModPatch.Parse(PatchJson(draftOrMod)).Json)
                ? "\nThat only says it's legal. This changes how the world behaves, so run simulate on it once (fire-on-grass for what burns, flood for water, storm for lightning, walk for a trigger on steps) to see it goes the way the player meant, then apply. It shows the direction, not a multiple: \"twice as fast\" is the field doubled, so don't retune it to fit the numbers."
                : "";

        bool ChangesBehaviour(JObject patch) =>
            patch["reactions"] != null || patch["sim"] != null || patch[TriggersTable] != null
            || MaterialKinds.Any(kind => (patch[kind] as JObject)?.Properties().Any(p => !SameButLook(EntryIn(Raw, kind, p.Name), p.Value)) == true);

        static bool SameButLook(JToken was, JToken now)
        {
            if (was == null) return false;
            if (!(now is JObject fields)) return JToken.DeepEquals(was, now);
            return fields.Properties().All(p => p.Name == "look" || SameButLook(was[p.Name], p.Value));
        }

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
            string walker = !walking ? null : rules.TriggersOn(TriggerEvent.Step).Concat(rules.TriggersOn(TriggerEvent.Tick)).Select(t => t.Who).FirstOrDefault(p => p != TriggerDef.Each) ?? "Walker";
            var without = Simulator.Run(InForce, scenario, n, walker);
            return (Simulator.Run(rules, scenario, n, walker), without);
        }
    }
}
