using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Cell-chemistry port tests. Many mirror sandbox/test/sim.test.ts; the
    /// golden tests compare against numbers produced by running the TS sandbox.
    /// </summary>
    public class ChemistryTests
    {
        static Rules rules;

        internal static Rules R => rules ??= Rules.Load(RulesDir());

        /// <summary>
        /// The repo's rules/ folder. In Unity it sits next to the project; under
        /// `dotnet test` (core/Game.Core.Tests) we walk up from the test binary.
        /// </summary>
        static string RulesDir()
        {
#if UNITY_5_3_OR_NEWER
            return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../rules"));
#else
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "rules");
                if (File.Exists(Path.Combine(candidate, "materials.json"))) return candidate;
            }
            throw new DirectoryNotFoundException("No rules/ folder above " + TestContext.CurrentContext.TestDirectory);
#endif
        }

        /// <summary>A small dirt room with bedrock walls, like room() in sim.test.ts (seed 7).</summary>
        internal static World Room(int w = 12, int h = 8)
        {
            var world = new World(R, w, h, 7);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) world.At(x, y).T = "bedrock";
            return world;
        }

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }
        static void Direct(World w, int x, int y, string element) => w.Apply(x, y, element, 2, -1, true);

        static string Entry(LogEntry l) => $"{l.Tick}|{l.Kind.ToString().ToLowerInvariant()}|{l.Count}|{l.Text}";

        // ---------- rules + rng ----------

        [Test]
        public void RulesLoadAllFiles()
        {
            Assert.That(R.Terrain.ContainsKey("grass"));
            Assert.That(R.Terrain["pit"].Elevation, Is.EqualTo(-1));
            Assert.That(R.Terrain["grass"].Has("flammable"));
            Assert.That(R.Covers["oil"].BurnIntensity, Is.EqualTo(3));
            Assert.That(R.Gases.ContainsKey("steam"));
            Assert.That(R.CellRules.Count, Is.EqualTo(27));
            Assert.That(R.CellRulesFor("fire")[0].Id, Is.EqualTo("fire-oil"));
            Assert.That(R.Spell("fire-bolt").Power, Is.EqualTo(2));
            Assert.That(R.Spell("ray-of-frost").Length, Is.EqualTo(5));
            Assert.That(R.Spell("nope"), Is.Null);
            Assert.That(R.Sim.TicksPerSecond, Is.EqualTo(6));
            Assert.That(R.Sim.WaterUnitsPerStep, Is.EqualTo(4));
            Assert.That(R.Sim.WaterMaxLevel, Is.EqualTo(16));
        }

        [Test]
        public void FromJsonToleratesMissingOptionalFiles()
        {
            var files = new Dictionary<string, string>
            {
                ["materials.json"] = "{\"terrain\":{\"dirt\":{\"name\":\"Dirt\",\"tags\":[\"ground\"]}},\"covers\":{},\"gases\":{}}",
                ["reactions.json"] = "{\"cell\":[],\"entity\":[{\"id\":\"x\",\"element\":\"fire\",\"do\":[{\"status\":{\"burning\":3}}]}]}",
            };
            var r = Rules.FromJson(files);
            Assert.That(r.Spells.Count, Is.EqualTo(0));
            Assert.That(r.Sim.WaterUnitsPerStep, Is.EqualTo(4));
            var w = new World(r, 3, 3);
            w.Apply(1, 1, "fire");
            w.Step();
        }

        [Test]
        public void RngMatchesTsForSeed1()
        {
            // node: createWorld(2,2,1); rand(w) x3
            var w = new World(R, 2, 2, 1);
            Assert.That(w.Rand(), Is.EqualTo(0.6270739405881613));
            Assert.That(w.Rand(), Is.EqualTo(0.002735721180215478));
            Assert.That(w.Rand(), Is.EqualTo(0.5274470399599522));
            Assert.That(w.RngState, Is.EqualTo(1199730144u));
        }

        // ---------- golden parity with the TS sandbox ----------

        static void Set(World w, int x, int y, string t) => w.At(x, y).T = t;

        [Test]
        public void GoldenMeadowFireMatchesTs()
        {
            var w = new World(R, 20, 12, 42);
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 20; x++) Set(w, x, y, (x + y) % 7 == 0 ? "tree" : "grass");
            for (int x = 12; x <= 15; x++)
                for (int y = 3; y <= 5; y++) { Set(w, x, y, "pit"); w.At(x, y).Water = 4; }
            for (int x = 5; x <= 8; x++) w.At(x, 8).Cover = "oil";
            Set(w, 3, 2, "wood"); Set(w, 4, 2, "brush"); Set(w, 10, 10, "stone");
            w.Cast("fire-bolt", 6, 6, 0);
            Run(w, 150);

            Assert.That(w.CellHash(), Is.EqualTo("73a467cc"));
            Assert.That(w.RngState, Is.EqualTo(4226383236u));
            CollectionAssert.AreEqual(new[]
            {
                "0|info|1|You cast Fire Bolt",
                "0|info|1|Fire takes hold of the grass",
                "11|notable|3|The oil ignites",
                "140|notable|1|The brambles burn away, opening a path",
            }, w.Log.Select(Entry).ToArray());
        }

        [Test]
        public void GoldenMixedElementsMatchTs()
        {
            var w = new World(R, 16, 10, 7);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 10; y++)
                    if (x == 0 || y == 0 || x == 15 || y == 9) Set(w, x, y, "bedrock");
            for (int x = 8; x <= 13; x++) Set(w, x, 3, "pit");
            for (int x = 3; x <= 7; x++) Set(w, x, 6, "metal");
            for (int x = 2; x <= 6; x++) Set(w, x, 2, "grass");
            w.Cast("create-water", 6, 3, 1);
            Run(w, 30);
            w.Cast("grease", 9, 6, 1);
            w.Cast("fire-bolt", 9, 6, 1);
            w.Cast("fire-bolt", 3, 2, 1);
            Run(w, 10);
            w.Cast("gust-of-wind", 1, 2, 1);
            Run(w, 20);
            w.Cast("ray-of-frost", 8, 3, 1);
            w.Cast("shocking-grasp", 3, 6, 1);
            w.Cast("shocking-grasp", 10, 3, 1);
            w.Cast("thunderwave", 4, 4, 1);
            w.Apply(5, 7, "earth", 1, -1, true);
            w.Apply(12, 3, "earth", 1, -1, true);
            w.Apply(6, 3, "fire", 1, -1, true);
            w.Cast("create-water", 9, 6, 1);
            Run(w, 120);

            Assert.That(w.CellHash(), Is.EqualTo("8324eabc"));
            Assert.That(w.RngState, Is.EqualTo(939719859u));
            CollectionAssert.AreEqual(new[]
            {
                "0|info|1|You cast Create Water",
                "30|info|1|You cast Grease",
                "30|info|1|You cast Fire Bolt",
                "30|notable|1|The grease ignites",
                "30|info|1|You cast Fire Bolt",
                "30|info|1|Fire takes hold of the grass",
                "40|notable|2|The grease ignites",
                "40|info|1|You cast Gust of Wind",
                "43|notable|1|The grease ignites",
                "60|info|1|You cast Ray of Frost",
                "60|info|1|The water freezes solid",
                "60|info|1|You cast Shocking Grasp",
                "60|notable|1|Lightning arcs through everything conductive that touches",
                "60|info|1|You cast Shocking Grasp",
                "60|info|1|The ice insulates and the spark fizzles",
                "60|info|1|You cast Thunderwave",
                "60|info|1|A pillar of earth rises",
                "60|info|1|Earth fills the pit",
                "60|info|1|Water boils into steam",
                "60|info|1|You cast Create Water",
            }, w.Log.Select(Entry).ToArray());
        }

        // ---------- fire ----------

        [Test]
        public void FireSpreadsOnGrassAndBurnsItToDirtAndAsh()
        {
            var w = new World(R, 15, 15, 3, "grass");
            w.Apply(7, 7, "fire");
            Assert.That(w.At(7, 7).Fire, Is.EqualTo(2));
            Assert.That(w.Log[w.Log.Count - 1].Text, Is.EqualTo("Fire takes hold of the grass"));
            Run(w, 6);
            Assert.That(w.Cells.Count(c => c.Fire > 0), Is.GreaterThan(1), "fire spread to neighbours");
            Run(w, 200);
            var burnt = w.Cells.Where(c => c.T == "dirt").ToList();
            Assert.That(burnt.Count, Is.GreaterThan(10), "a patch of grass burned away");
            Assert.That(burnt.All(c => c.Cover == "ash" && c.Fire == 0));
            Assert.That(w.At(7, 7).T, Is.EqualTo("dirt"));
        }

        [Test]
        public void FadingFireSpreadsAWayThenDiesOut()
        {
            // How far right fire started at the left end of a long grass strip gets, in cells.
            int Reach(int fadeCells, uint seed)
            {
                var ownRules = Rules.Load(RulesDir());   // a private copy: R is shared by every test
                ownRules.Sim.Fire.FadeCells = fadeCells;
                var w = new World(ownRules, 120, 9, seed, "grass");
                w.Apply(0, 4, "fire");
                Run(w, 2000);
                int far = 0;
                for (int y = 0; y < w.H; y++)
                {
                    for (int x = 0; x < w.W; x++)
                    {
                        if (w.At(x, y).T == "dirt") far = System.Math.Max(far, x);
                    }
                }
                return far;
            }

            // Grass fire is chancy (some fires die at once), so judge over many seeds.
            var unfaded = new System.Collections.Generic.List<int>();
            var faded = new System.Collections.Generic.List<int>();
            for (uint seed = 1; seed <= 12; seed++)
            {
                unfaded.Add(Reach(0, seed));
                faded.Add(Reach(12, seed));
            }
            Assert.That(faded.Max(), Is.LessThanOrEqualTo(12), "a fire can't pass on beyond FadeCells spreads");
            Assert.That(faded.Average(), Is.GreaterThan(4), "but it still spreads a way first");
            Assert.That(unfaded.Average(), Is.GreaterThan(3 * faded.Average()), "without fading, fire runs much further");
        }

        [Test]
        public void FireOnOilBlazesAndWaterSplashesIt()
        {
            var w = Room();
            w.At(5, 4).Cover = "oil";
            Direct(w, 5, 4, "fire");
            Assert.That(w.At(5, 4).Fire, Is.EqualTo(3));
            Direct(w, 5, 4, "water");
            Assert.That(w.At(5, 4).Fire, Is.EqualTo(3), "still burning");
            int lit = new[] { (4, 4), (6, 4), (5, 3), (5, 5) }.Count(p => w.At(p.Item1, p.Item2).Fire > 0);
            Assert.That(lit, Is.EqualTo(2), "burning oil splashed onto two neighbours");
            Assert.That(w.Log.Any(l => l.Text.StartsWith("Water can't douse burning oil", StringComparison.Ordinal)));
        }

        [Test]
        public void WaterOnGrassFirePutsItOutInSteam()
        {
            var w = Room();
            w.Paint(5, 4, "grass");
            Direct(w, 5, 4, "fire");
            Assert.That(w.At(5, 4).Fire, Is.GreaterThan(0));
            Direct(w, 5, 4, "water");
            Assert.That(w.At(5, 4).Fire, Is.EqualTo(0));
            Assert.That(w.At(5, 4).Gas, Is.EqualTo("steam"));
        }

        [Test]
        public void ColdFreezesWaterAndExtinguishesFire()
        {
            var w = Room();
            w.Paint(3, 3, "grass");
            Direct(w, 3, 3, "fire");
            Direct(w, 3, 3, "cold");
            Assert.That(w.At(3, 3).Fire, Is.EqualTo(0));
            Assert.That(w.At(3, 3).Fuel, Is.EqualTo(0));

            w.Paint(6, 3, "pit");
            w.AddWater(6, 3, 4);
            Direct(w, 6, 3, "cold");
            Assert.That(w.At(6, 3).Frozen);
            Assert.That(w.IsSlippery(w.At(6, 3)));
        }

        [Test]
        public void RayOfFrostFreezesAPondIntoInsulatingIce()
        {
            var w = Room();
            for (int x = 3; x <= 7; x++) { w.At(x, 4).T = "pit"; w.At(x, 4).Water = 4; }
            w.Cast("ray-of-frost", 3, 4, 1);
            for (int x = 3; x <= 7; x++) Assert.That(w.At(x, 4).Frozen, $"x={x} frozen");
            Direct(w, 5, 4, "lightning");
            Assert.That(w.At(6, 4).Charge, Is.EqualTo(0), "ice doesn't conduct");
        }

        [Test]
        public void FireMeltsIceThenBoilsTheWater()
        {
            var w = Room();
            var c = w.At(5, 4);
            c.T = "pit"; c.Water = 4; c.Frozen = true;
            Direct(w, 5, 4, "fire");
            Assert.That(c.Frozen, Is.False);
            Assert.That(c.Water, Is.EqualTo(4));
            Direct(w, 5, 4, "fire");
            Assert.That(c.Water, Is.EqualTo(3));
            Assert.That(c.Gas, Is.EqualTo("steam"));
        }

        [Test]
        public void ColdCondensesSteam()
        {
            var w = Room();
            w.AddGas(w.At(5, 4), "steam", 4);
            Direct(w, 5, 4, "cold");
            Assert.That(w.At(5, 4).GasAmt, Is.EqualTo(2));
            Assert.That(w.At(5, 4).Water, Is.EqualTo(2));
        }

        [Test]
        public void ABurningBridgeCollapsesIntoAPit()
        {
            var w = Room(10, 6);
            w.At(4, 2).T = "wood";
            Direct(w, 4, 2, "fire");
            Run(w, 40);
            Assert.That(w.At(4, 2).T, Is.EqualTo("pit"));
            Assert.That(w.At(4, 2).Cover, Is.Null, "no ash floating in a pit");
            Assert.That(w.Log.Any(l => l.Text == "The bridge burns through and collapses" && l.Kind == LogKind.Notable));
        }

        [Test]
        public void GreaseIntoFireWhooshes()
        {
            var w = Room();
            w.Paint(5, 4, "grass");
            Direct(w, 5, 4, "fire");
            Direct(w, 5, 4, "grease");
            Assert.That(w.At(5, 4).Cover, Is.EqualTo("grease"));
            Assert.That(w.At(5, 4).Fire, Is.EqualTo(2));
            Assert.That(w.Log[w.Log.Count - 1].Text, Is.EqualTo("Grease hits the flames. Whoosh!"));
        }

        // ---------- air / thunder ----------

        [Test]
        public void AirBlowsOutSmallFlamesAndFansBigOnes()
        {
            var w = Room();
            w.Paint(3, 3, "grass");
            w.Apply(3, 3, "lightning");
            Assert.That(w.At(3, 3).Fire, Is.EqualTo(1));
            w.Apply(3, 3, "air", 1, 1);
            Assert.That(w.At(3, 3).Fire, Is.EqualTo(0));

            w.Paint(6, 3, "grass");
            w.Paint(7, 3, "grass");
            Direct(w, 6, 3, "fire");
            w.Apply(6, 3, "air", 1, 1);
            Assert.That(w.At(6, 3).Fire, Is.EqualTo(3));
            Assert.That(w.At(7, 3).Fire, Is.GreaterThan(0), "fanned downwind");
        }

        [Test]
        public void AirPushesGasAndThunderClearsIt()
        {
            var w = Room();
            w.AddGas(w.At(4, 4), "smoke", 3);
            w.Apply(4, 4, "air", 1, 1);
            Assert.That(w.At(4, 4).Gas, Is.Null);
            Assert.That(w.At(5, 4).Gas, Is.EqualTo("smoke"));
            Assert.That(w.At(5, 4).GasAmt, Is.EqualTo(3));
            w.Apply(5, 4, "thunder");
            Assert.That(w.At(5, 4).Gas, Is.Null);
        }

        [Test]
        public void GustOfWindSetsWindThatExpires()
        {
            var w = Room(16, 6);
            w.Cast("gust-of-wind", 2, 2, 1);
            Assert.That(w.Wind, Is.EqualTo(((int, int)?)(1, 18)));
            Run(w, 18);
            Assert.That(w.Wind, Is.Null);
        }

        // ---------- water ----------

        [Test]
        public void WaterFlowsDownhillIntoAPit()
        {
            var w = Room();
            w.Paint(5, 4, "pit");
            w.AddWater(4, 4, 3);
            Run(w, 10);
            Assert.That(w.At(5, 4).Water, Is.EqualTo(3), "all of it ran into the pit");
            Assert.That(w.At(4, 4).Water, Is.EqualTo(0));
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(3), "water is conserved");
        }

        [Test]
        public void CreateWaterRunsDownAChannel()
        {
            var w = Room(16, 5);
            for (int x = 2; x <= 13; x++) w.At(x, 2).T = "pit";
            w.Cast("create-water", 3, 2, 0);
            int total = w.Cells.Sum(c => c.Water);
            Run(w, 80);
            Assert.That(w.At(9, 2).Water, Is.GreaterThan(0), "water reached the wheel's square (x=9 in sim.test.ts)");
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(total));
            // The same setup in the TS sandbox ends with exactly this row.
            CollectionAssert.AreEqual(new[] { 0, 2, 7, 6, 6, 5, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0 },
                Enumerable.Range(0, 16).Select(x => w.At(x, 2).Water).ToArray());
        }

        // ---------- lightning ----------

        [Test]
        public void LightningConductsThroughWaterIntoMetal()
        {
            var w = Room(14, 6);
            w.At(2, 2).T = "pit"; w.At(2, 2).Water = 4;
            for (int x = 3; x <= 8; x++) w.At(x, 2).T = "metal";
            w.At(9, 2).T = "coil";
            w.Cast("shocking-grasp", 2, 2, 1);
            Assert.That(w.At(9, 2).Charge, Is.EqualTo(3));
            Assert.That(w.At(5, 2).Charge, Is.EqualTo(3));
            Assert.That(w.At(5, 3).Charge, Is.EqualTo(0), "dirt doesn't conduct");
            w.Step();
            Assert.That(w.At(9, 2).Charge, Is.EqualTo(2), "charge decays");
        }

        [Test]
        public void LightningLightsOilOnWater()
        {
            var w = Room();
            for (int x = 3; x <= 6; x++) { w.At(x, 4).T = "pit"; w.At(x, 4).Water = 4; }
            w.At(6, 4).Cover = "oil";
            Direct(w, 3, 4, "lightning");
            Assert.That(w.At(6, 4).Fire, Is.EqualTo(3), "the charge reached the oil and lit it");
        }

        // ---------- earth ----------

        [Test]
        public void EarthBreaksStoneFillsPitsAndRaisesPillars()
        {
            var w = Room();
            w.At(3, 3).T = "stone";
            w.At(5, 3).T = "pit";
            w.Apply(3, 3, "earth");
            w.Apply(5, 3, "earth");
            w.Apply(7, 3, "earth");
            Assert.That(w.At(3, 3).T, Is.EqualTo("rubble"));
            Assert.That(w.At(5, 3).T, Is.EqualTo("dirt"));
            Assert.That(w.At(7, 3).T, Is.EqualTo("pillar"));
            Assert.That(w.IsBlocking(w.At(7, 3)));
        }

        [Test]
        public void FillingAPondPushesItsWaterOut()
        {
            var w = Room();
            w.Paint(5, 3, "pit");
            w.AddWater(5, 3, 4);
            w.Apply(5, 3, "earth");
            Assert.That(w.At(5, 3).T, Is.EqualTo("dirt"));
            Assert.That(w.At(5, 3).Water, Is.EqualTo(0));
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(4));
        }

        // ---------- shapes / log / determinism ----------

        [Test]
        public void SpellShapes()
        {
            var w = Room();
            var sq = w.SpellCells(R.Spell("create-water"), 5, 4, 0);
            Assert.That(sq.Count, Is.EqualTo(9));
            Assert.That(sq[0], Is.EqualTo((4, 3)));
            var grease = w.SpellCells(R.Spell("grease"), 5, 4, 0);
            CollectionAssert.AreEqual(new[] { (5, 4), (6, 4), (5, 5), (6, 5) }, grease);
            Assert.That(w.SpellCells(R.Spell("thunderwave"), 5, 4, 0).Count, Is.EqualTo(9));
            Assert.That(w.SpellCells(R.Spell("fire-bolt"), 5, 4, 0).Single(), Is.EqualTo((5, 4)));
            w.At(8, 4).T = "stone";
            var line = w.SpellCells(R.Spell("ray-of-frost"), 6, 4, 1);
            CollectionAssert.AreEqual(new[] { (6, 4), (7, 4), (8, 4) }, line, "a line stops at the first wall");
            var west = w.SpellCells(R.Spell("gust-of-wind"), 3, 4, 3);
            CollectionAssert.AreEqual(new[] { (3, 4), (2, 4), (1, 4), (0, 4) }, west);
        }

        [Test]
        public void LogMergesRepeatsAndDedupsPerTick()
        {
            var w = Room();
            w.Paint(3, 3, "pit"); w.AddWater(3, 3, 4);
            w.Paint(4, 3, "pit"); w.AddWater(4, 3, 4);
            Direct(w, 3, 3, "cold");
            Direct(w, 4, 3, "cold");
            Assert.That(w.Log.Count(l => l.Text == "The water freezes solid"), Is.EqualTo(1));
            Assert.That(w.Log[w.Log.Count - 1].Count, Is.EqualTo(1), "second one was deduped within the tick");
            w.Step();
            w.Paint(5, 3, "pit"); w.AddWater(5, 3, 4);
            Direct(w, 5, 3, "cold");
            Assert.That(w.Log[w.Log.Count - 1].Count, Is.EqualTo(2), "a later repeat merges into the last line");
            Assert.That(w.Log[w.Log.Count - 1].Tick, Is.EqualTo(1));
        }

        [Test]
        public void SameSeedSameResult()
        {
            string RunOnce()
            {
                var w = new World(R, 20, 20, 42, "grass");
                w.Cast("fire-bolt", 5, 5, 0);
                w.Cast("fire-bolt", 15, 12, 0);
                Run(w, 150);
                return w.CellHash();
            }
            Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
        }
    }
}
