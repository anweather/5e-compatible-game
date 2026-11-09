using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The dragon in the tools (#77): tune and list reach sim.json's dragon, and describe says what it is.</summary>
    [Category("Simulation")]
    public class DragonToolsTests
    {
        static Workbench bench;
        static Workbench Bench => bench ??= new Workbench(ChemistryTests.Folders);

        static Rules With(Draft d) => Bench.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });

        // Every cell of a one-chunk world changed: past a budget of 100 the dragon comes; at never it doesn't.
        static World Changed(Draft d)
        {
            var w = new World(With(d), 64, 64, 7, "dirt");
            w.KeepStart();
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++) w.At(x, y).T = "stone";
            for (int t = 0; t < 20; t++) w.Step();
            return w;
        }

        [Test]
        public void NeverMeansItNeverComesByItself()
        {
            Assert.That(Bench.LiveWorld.Rules.Sim.Dragon.Budget, Is.Zero, "by default it comes only when summoned (J-073)");
            var d = Bench.Tune(null, "dragon.budget", "never");
            Assert.That(Bench.Validate(d.Id), Is.Empty, Workbench.Report(Bench.Validate(d.Id)));
            Assert.That(With(d).Sim.Dragon.Budget, Is.Zero);
            var never = Changed(d);
            Assert.That((never.ChangedCount, never.Dragon), Is.EqualTo((64 * 64, (DragonState)null)), "every cell changed, and no dragon");
            Assert.That(Changed(Bench.Tune(null, "dragon.budget", "100")).Dragon, Is.Not.Null, "at 100 it comes");
        }

        [Test]
        public void EveryFieldIsTunedWithinItsBoundsAndListedWithWhatItDoes()
        {
            var fields = ((JObject)JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(ChemistryTests.RulesDir(), "sim.json")))["dragon"]).Properties().Select(p => p.Name).ToList();
            string list = Bench.List("dragon");
            StringAssert.Contains("comes by itself", list);
            foreach (var f in fields)
            {
                StringAssert.Contains($"dragon.{f} = ", list);
                Assert.That(list.Split('\n').Single(l => l.StartsWith($"dragon.{f} = ", System.StringComparison.Ordinal)), Does.Contain(": "), $"{f} says what it does");
                if (f != "wallTerrains") Assert.That(list, Does.Match($"dragon\\.{f} = [^\\n]*a mod may set"), $"{f} has bounds");
            }
            Assert.That(Bench.List("sim"), Does.Contain("dragon.speed = 6 (a mod may set 1 to 12): cells its breath goes a tick"));
            var fast = Bench.Tune(null, "dragon.speed", "12");
            Assert.That(With(fast).Sim.Dragon.Speed, Is.EqualTo(12));
            Assert.That(Assert.Throws<RulesToolException>(() => Bench.Tune(null, "dragon.speed", "40")).Message, Does.Contain("from 1 to 12"));
        }

        [Test]
        public void DescribeFindsTheDragon()
        {
            StringAssert.Contains("resets the world", Bench.Describe("dragon", ""));
            StringAssert.Contains("- budget: ", Bench.Describe("sim", "dragon"));
            StringAssert.Contains("dragon.budget: the cells", Bench.Describe("dragon", "budget"));
            StringAssert.Contains("\"warningTicks\": 180", Bench.Describe("dragon", "dragon"));
        }

        [Test]
        public void TheDefaultsStayAsTheyWere()
        {
            var d = new DragonParams();
            var read = Bench.Base.Sim.Dragon;
            Assert.That((read.WarningTicks, read.TurnTicks, read.LeavingTicks, read.Speed, read.Depth, read.BreathChunks, read.OverlapChunks, read.ChunksPerTick, read.Budget, read.ShelterWater, read.WallReach, read.Scale),
                Is.EqualTo((d.WarningTicks, d.TurnTicks, d.LeavingTicks, d.Speed, d.Depth, d.BreathChunks, d.OverlapChunks, d.ChunksPerTick, d.Budget, d.ShelterWater, d.WallReach, d.Scale)));
            foreach (var (path, min, max) in Bench.Budgets.SimBounds.Where(b => b.path.StartsWith("dragon.", System.StringComparison.Ordinal)))
                Assert.That((double)JObject.Parse(Bench.Base.ToJson())["sim"].SelectToken(path), Is.InRange(min, max), $"{path}: the base value is within its bounds");
        }
    }
}
