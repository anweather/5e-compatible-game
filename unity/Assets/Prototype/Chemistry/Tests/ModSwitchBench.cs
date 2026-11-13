using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Where a mod switched on in play spends its frame (#155; docs/design/mod-switch-stall.md): the patch read, its checks
    /// (ModValidator), the compose (RoomRules.WithMods: the rules copied and read again), and the whole World.ApplyRoom,
    /// switch-on and revert, on the valley and the nine as ChemGrid builds them, in a room. Each part warm, the median of
    /// five. And a prototype of composing ahead: the same RuleSet composed on a worker is the same rules, to the byte.
    /// Not a check and not in CI (Explicit): run it by name in Release and read the lines it prints.
    /// <code>dotnet test core -c Release --filter "FullyQualifiedName~ModSwitchBench" --logger "console;verbosity=detailed"</code>
    /// </summary>
    [Explicit("a benchmark: run it by name")]
    [Category("Simulation")]
    public class ModSwitchBench
    {
        static Rules R => ChemistryTests.R;

        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        static string ModJson(string name) =>
            name == "sim-only" ? "{ \"sim\": { \"conductLimit\": 10 } }" : File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", name + ".json"));

        static double Median(Func<double> run)
        {
            run(); // warm
            var times = Enumerable.Range(0, 5).Select(_ => run()).OrderBy(t => t).ToArray();
            return times[2];
        }

        static double Ms(Action act)
        {
            var watch = Stopwatch.StartNew();
            act();
            return watch.Elapsed.TotalMilliseconds;
        }

        [TestCase("valley", "sim-only")]
        [TestCase("valley", "moonstone")]
        [TestCase("valley", "wildfire")]
        [TestCase("nine", "sim-only")]
        [TestCase("nine", "moonstone")]
        public void WhereASwitchsFrameGoes(string map, string mod)
        {
            string dir = map == "nine" ? SparseWorldBench.Baked() : Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var w = SparseWorldBench.Build(dir, R, out _);
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            for (int t = 0; t < 5; t++) w.Step();
            string json = ModJson(mod);

            double parse = Median(() => Ms(() => ModPatch.Parse(json)));
            double checks = Median(() => Ms(() => ModValidator.Validate(w.Rules, Budgets, json, TempMaterials.Owners(w.TemporaryMaterials))));
            var on = w.Room.Mods.Select(m => new ActiveMod(m.Id, m.Json)).Append(new ActiveMod(mod, json)).ToList();
            double compose = Median(() => Ms(() => w.RoomRules.WithMods(on)));
            double switchOn = 0, revert = 0;
            var ons = new List<double>();
            var reverts = new List<double>();
            for (int k = 0; k < 6; k++)
            {
                RoomVerdict v = default;
                ons.Add(Ms(() => v = w.ApplyRoom(RoomCommand.Apply("Ana", mod, json))));
                Assert.That(v.Done, Is.True, v.Reason);
                w.Step();
                reverts.Add(Ms(() => Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", mod)).Done, Is.True)));
                w.Step();
            }
            switchOn = ons.Skip(1).OrderBy(t => t).ElementAt(2);
            revert = reverts.Skip(1).OrderBy(t => t).ElementAt(2);
            double rest = Math.Max(0, switchOn - checks - compose);
            // What each part allocates (Mono's collector is what makes garbage a stall in the game): MB on this thread.
            double Mb(Action act)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                act();
                return (GC.GetAllocatedBytesForCurrentThread() - before) / 1e6;
            }
            double checksMb = Mb(() => ModValidator.Validate(w.Rules, Budgets, json, TempMaterials.Owners(w.TemporaryMaterials)));
            double composeMb = Mb(() => w.RoomRules.WithMods(on));
            double switchMb = Mb(() => w.ApplyRoom(RoomCommand.Apply("Ana", mod, json)));
            TestContext.Out.WriteLine(
                $"SWITCH {map} {mod}: read {parse:0.0} ms, checks {checks:0.0} ms, compose {compose:0.0} ms; the switch-on {switchOn:0.0} ms (so {rest:0.0} ms besides checks and compose), the revert {revert:0.0} ms; allocated: checks {checksMb:0.0} MB, compose {composeMb:0.0} MB, the switch-on {switchMb:0.0} MB");
        }

        /// <summary>
        /// The compose-ahead prototype: the RuleSet a switch puts in force, composed on a worker while the main thread ticks,
        /// is the RuleSet composed on the main thread, to the byte (its rules' JSON and fingerprint), and the time a tick
        /// would then spend is only the swap.
        /// </summary>
        [Test]
        public void AModComposedOnAWorkerIsTheSameRules()
        {
            string dir = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var w = SparseWorldBench.Build(dir, R, out _);
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            string json = ModJson("moonstone");
            var on = new[] { new ActiveMod("moonstone", json) };
            var here = w.RoomRules.WithMods(on);
            var ahead = Task.Run(() => w.RoomRules.WithMods(on));
            double ticking = Ms(() => { for (int t = 0; t < 10; t++) w.Step(); });
            var there = ahead.Result;
            Assert.That(there.Effective.Fingerprint, Is.EqualTo(here.Effective.Fingerprint));
            Assert.That(there.Effective.ToJson(), Is.EqualTo(here.Effective.ToJson()));
            TestContext.Out.WriteLine($"SWITCH compose-ahead: composed on a worker during 10 ticks ({ticking:0} ms), the same rules to the byte");
        }
    }
}
