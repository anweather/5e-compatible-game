using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What the two kinds of frame-long stall in Andrew's 2026-11-08 play log cost in the core: a world rebuilt from
    /// scratch (start-up, hosting, an online reset: its ground's heightfield worked out for all 81 chunks), and a mod
    /// switched on from chat (checked, then composed over the rules). Not a check and not in CI (Explicit): run it by
    /// name in Release and read the lines it prints.
    /// <code>dotnet test core/Game.Core.sln -c Release --filter "FullyQualifiedName~HitchBench" --logger "console;verbosity=detailed"</code>
    /// </summary>
    [Explicit("a benchmark: run it by name")]
    [Category("Simulation")]
    public class HitchBench
    {
        const int Runs = 5;

        // A chat record's mod as he switched it on (74f9b7a34e6c, "Make the fire pink").
        internal const string PinkFire = "{ 'title': 'Pink fire', 'patch': { 'elements': { 'fire': { 'look': { 'color': [1.0, 0.4, 0.75] } } }, "
            + "'spells': [ { 'id': 'fireball', 'look': { 'color': [1.0, 0.4, 0.75] } }, { 'id': 'fire-bolt', 'look': { 'color': [1.0, 0.4, 0.75] } } ] } }";

        static double Best(Action run)
        {
            run();
            double best = double.MaxValue;
            for (int i = 0; i < Runs; i++)
            {
                var sw = Stopwatch.StartNew();
                run();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        [Test]
        public void HeightfieldReset()
        {
            var world = FogTickBench.Valley(ChemistryTests.R);
            var field = new CellHeightfield(TerrainGrid.Valley());
            double ms = Best(() => field.Reset(world));
            TestContext.Out.WriteLine($"HITCH heightfield reset, 81 chunks: {ms:0.0} ms");
            Assert.That(ms, Is.GreaterThan(0));
        }

        [Test]
        public void ModSwitchOn()
        {
            var rules = ChemistryTests.R;
            var budgets = ModBudgets.Load(ChemistryTests.RulesDir());
            string json = PinkFire.Replace('\'', '"');
            double validate = Best(() => ModValidator.Validate(rules, budgets, json));
            double compose = Best(() => RuleSet.Plain(rules).WithMods(new[] { new ActiveMod("pink", json) }));
            double room = Best(() =>
            {
                var w = new World(rules, 64, 64, 1, "grass");
                w.OpenRoom(ChangeMode.Open, "host", rules, budgets);
                var v = w.ApplyRoom(RoomCommand.Apply("host", "pink", json, "Pink fire"));
                if (!v.Done) throw new InvalidOperationException(v.Reason);
            });
            TestContext.Out.WriteLine($"HITCH mod switch-on: validate {validate:0.0} ms, compose {compose:0.0} ms, whole room command {room:0.0} ms");
            Assert.That(room, Is.GreaterThan(0));
        }
    }
}
