// Cell-chemistry port tests. Many mirror sandbox/test/sim.test.ts; the
// goldens were first produced by the TS sandbox (frozen 2026-10-31).
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class ChemistryTests
    {
        static Rules rules;

        internal static Rules R => rules ??= Rules.Load(RulesDir());
        static Rules noSoak;
        internal static Rules NoSoak => noSoak ??= R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"water\": { \"soakPass\": 0 } } }") });

        static GameFolders folders;

        internal static GameFolders Folders => folders ??= GameFolders.InRepo(
#if UNITY_5_3_OR_NEWER
            UnityEngine.Application.dataPath);
#else
            TestContext.CurrentContext.TestDirectory);
#endif

        internal static string RulesDir() => Folders.Rules;

        internal static string WorldDir() => Folders.World;

        internal static World Room(int w = 12, int h = 8, uint seed = 7, Rules rules = null)
        {
            var world = new World(rules ?? R, w, h, seed);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) world.At(x, y).T = "bedrock";
            return world;
        }

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }
        static void Direct(World w, int x, int y, string element) => w.Apply(x, y, element, 2, -1, true);

        static string Entry(LogEntry l) => $"{l.Tick}|{l.Kind.ToString().ToLowerInvariant()}|{l.Count}|{l.Text}";

        [Test]
        public void WallsBlockSightButGateBarsAndTheEndsDont()
        {
            var w = Room(12, 8);
            Assert.That(w.LineOfSight(2, 3, 9, 3), Is.True, "open floor");
            w.At(5, 3).T = "tree";
            Assert.That(w.LineOfSight(2, 3, 9, 3), Is.False, "a tree in the way");
            Assert.That(w.LineOfSight(9, 3, 2, 3), Is.False, "either way round");
            w.At(5, 3).T = "gate";
            Assert.That(w.LineOfSight(2, 3, 9, 3), Is.True, "gate bars are see-through");
            w.At(5, 3).T = "dirt";
            w.At(9, 3).T = "tree";
            Assert.That(w.LineOfSight(2, 3, 9, 3), Is.True, "the target square itself doesn't block the view of it");
            Assert.That(w.LineOfSight(4, 4, 4, 4), Is.True, "your own square");
        }

        [Test]
        public void RulesLoadAllFiles()
        {
            Assert.That(R.Terrain.ContainsKey("grass"));
            Assert.That(R.Terrain["pit"].Elevation, Is.EqualTo(-1));
            Assert.That(R.Terrain["grass"].Has("flammable"));
            Assert.That(R.Covers["oil"].BurnIntensity, Is.EqualTo(3));
            Assert.That(R.Gases.ContainsKey("steam"));
            Assert.That(R.CellRules.Count, Is.EqualTo(77));
            Assert.That(R.CellRulesFor("fire")[0].Id, Is.EqualTo("fire-fog"), "fire burns fog off before any rule stops");
            Assert.That(R.Spell("fire-bolt").Power, Is.EqualTo(2));
            Assert.That(R.Spell("ray-of-frost").Length, Is.EqualTo(5));
            Assert.That(R.Spell("nope"), Is.Null);
            Assert.That(R.Sim.TicksPerSecond, Is.EqualTo(6));
            Assert.That(R.Sim.WaterUnitsPerStep, Is.EqualTo(4));
            Assert.That(R.Sim.WaterMaxLevel, Is.EqualTo(32));
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
            var w = new World(R, 2, 2, 1);
            Assert.That(w.Rand(), Is.EqualTo(0.6270739405881613));
            Assert.That(w.Rand(), Is.EqualTo(0.002735721180215478));
            Assert.That(w.Rand(), Is.EqualTo(0.5274470399599522));
            Assert.That(w.RngState, Is.EqualTo(1199730144u));
        }

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

            Assert.That(w.CellHash(), Is.EqualTo("8cef26c5"));
            Assert.That(w.RngState, Is.EqualTo(2387241123u));
            CollectionAssert.AreEqual(new[]
            {
                "0|info|1|You cast Fire Bolt",
                "0|info|1|Fire takes hold of the grass",
                "11|notable|3|The oil ignites",
                "109|notable|1|The bridge burns through and collapses",
                "110|notable|1|The brambles burn away, opening a path",
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

            Assert.That(w.CellHash(), Is.EqualTo("bd0d8a05"));
            Assert.That(w.RngState, Is.EqualTo(819413765u));
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
                "60|notable|1|The ice cracks",
                "60|info|1|The ice insulates and the spark fizzles",
                "60|info|1|You cast Thunderwave",
                "60|info|1|A pillar of earth rises",
                "60|info|1|Earth fills the pit",
                "60|info|1|You cast Create Water",
                "60|info|1|Watered, green shoots push up through the ash",
            }, w.Log.Select(Entry).ToArray());
        }

        [TestCase(1)]
        [TestCase(2)]
        public void AFireballBurstsOnTheCellItIsAimedAt(int charge)
        {
            var fireball = R.Spell("fireball");
            var w = new World(R, 21, 21, 5, "grass");
            w.Cast("fireball", 10, 10, 0, charge);
            int radius = fireball.Radius + World.ChargedGrowth(fireball, charge);
            Assert.That(w.At(10, 10).Height, Is.LessThan(0), "the crater is on the aimed cell");
            Assert.That(w.At(10, 10).T, Is.EqualTo("dirt"));
            int crater = fireball.Crater.Radius;
            for (int y = 0; y < 21; y++)
                for (int x = 0; x < 21; x++)
                {
                    var c = w.At(x, y);
                    int d = System.Math.Max(System.Math.Abs(x - 10), System.Math.Abs(y - 10));
                    if (d > crater && d <= radius) Assert.That(c.Fire, Is.GreaterThan(0), $"({x}, {y}) in the burst round the crater burns");
                    if (d > radius && c.Fire > 0) Assert.That(c.Cover, Is.EqualTo("embers"), $"({x}, {y}) past the burst burns only as debris");
                }
            Assert.That(w.Log.Select(l => l.Text), Has.Member("Fire takes hold of the grass"));
        }

        [Test]
        public void FireSpreadsOnGrassAndBurnsItToDirtAndAsh()
        {
            var w = new World(R, 15, 15, 3, "grass");
            w.Apply(7, 7, "fire");
            Assert.That(w.At(7, 7).Fire, Is.EqualTo(2));
            Assert.That(w.Log[w.Log.Count - 1].Text, Is.EqualTo("Fire takes hold of the grass"));
            Run(w, 6);
            Assert.That(w.AllCells().Count(c => c.Fire > 0), Is.GreaterThan(1), "fire spread to neighbours");
            Run(w, 200);
            var burnt = w.AllCells().Where(c => c.T == "dirt").ToList();
            Assert.That(burnt.Count, Is.GreaterThan(10), "a patch of grass burned away");
            Assert.That(burnt.All(c => c.Cover == "ash" && c.Fire == 0));
            Assert.That(w.At(7, 7).T, Is.EqualTo("dirt"));
        }

        [Test]
        public void FadingFireSpreadsAWayThenDiesOut()
        {
            int Reach(int fadeCells, uint seed)
            {
                var ownRules = Rules.Load(RulesDir());
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

        static int Spread(int[] land, out int east)
        {
            var w = new World(NoSoak, 16, 9, 1, "grass");
            w.SetLand(land);
            w.At(8, 4).Water = 12;
            Run(w, 300);
            int west = w.AllCells().Where((c, k) => k % 16 < 8).Sum(c => c.Water);
            east = w.AllCells().Where((c, k) => k % 16 > 8).Sum(c => c.Water);
            return west;
        }

        [Test]
        public void WaterRunsDownTheLandsSlope()
        {
            var slope = new int[16 * 9];
            for (int k = 0; k < slope.Length; k++) slope[k] = (k % 16) * 3;
            int westOnSlope = Spread(slope, out int eastOnSlope);
            Assert.That(eastOnSlope, Is.EqualTo(0), "none runs uphill");
            Assert.That(westOnSlope, Is.GreaterThanOrEqualTo(10), "it runs down the slope");
            int westOnFlat = Spread(null, out int eastOnFlat);
            Assert.That(eastOnFlat, Is.GreaterThan(0), "on the flat it spreads both ways");
            Assert.That(westOnFlat, Is.LessThan(westOnSlope));
            var lake = (int[])slope.Clone();
            for (int k = 0; k < lake.Length; k++) if (k % 16 >= 8) lake[k] = World.NoLand;
            Spread(lake, out int eastOfLake);
            Assert.That(eastOfLake, Is.GreaterThan(0), "inside a water body it spreads as on the flat");
        }

        [Test]
        public void LandFileRoundTripsAndSetLandKeepsWaterBodiesLevel()
        {
            var rng = new Random(3);
            var land = new int[7 * 5];
            for (int k = 0; k < land.Length; k++) land[k] = rng.Next(-900, 900);
            string json = LandFile.Write(land, 7, 5, 0.125);
            Assert.That(LandFile.Read(json, 7, 5), Is.EqualTo(land));
            Assert.That(LandFile.Read(json, 5, 7), Is.Empty, "another world's size");
            var w = new World(R, 7, 5, 1, "grass");
            w.At(3, 2).Water = 6;
            var high = new int[7 * 5];
            for (int k = 0; k < high.Length; k++) high[k] = 400;
            high[2 * 7 + 3] = 0;
            w.SetLand(high);
            high[0] = 1;
            Run(w, 20);
            Assert.That(w.At(2, 2).Water + w.At(4, 2).Water + w.At(3, 1).Water + w.At(3, 3).Water, Is.GreaterThan(0), "the pond spills as on the flat");
        }

        [Test]
        public void TheValleysLandIsPinned()
        {
            string path = System.IO.Path.Combine(WorldDir(), "land.json");
            var land = LandFile.Read(System.IO.File.ReadAllText(path), 576, 576);
            Assert.That(land.Length, Is.EqualTo(576 * 576));
            Assert.That(land[0], Is.EqualTo(665), "the north-west corner, up in the hills");
            Assert.That(land[418 * 576 + 202], Is.EqualTo(103), "the slope probe's hillside");
            Assert.That(land[418 * 576 + 208], Is.EqualTo(64), "six cells down it");
            Assert.That(land[288 * 576 + 288], Is.EqualTo(0), "the middle");
            Assert.That(land[575 * 576 + 575], Is.EqualTo(-174), "the south-east corner, by the sea");
        }

        static World Puddles(Rules rules = null)
        {
            var w = new World(rules ?? R, 14, 9, 1, "dirt");
            w.At(3, 3).Water = 1;
            w.At(3, 6).Water = 1;
            for (int y = 3; y <= 5; y++)
                for (int x = 8; x <= 10; x++)
                {
                    w.At(x, y).T = "pit";
                    w.At(x, y).Water = 4;
                }
            return w;
        }

        static int PondWater(World w)
        {
            int sum = 0;
            for (int y = 3; y <= 5; y++) for (int x = 8; x <= 10; x++) sum += w.At(x, y).Water;
            return sum;
        }

        [Test]
        public void ThinWaterSoaksInAndDeepWaterStays()
        {
            var w = Puddles();
            w.At(3, 6).DampUntil = 100000;
            Run(w, 80);
            Assert.That(w.At(3, 3).Water, Is.EqualTo(0), "dry dirt: a unit in 64 ticks");
            Assert.That(w.At(3, 3).DampUntil, Is.GreaterThan(w.Tick), "and it's damp now");
            Assert.That(w.At(3, 6).Water, Is.EqualTo(1), "damp dirt soaks five times slower");
            Run(w, 320);
            Assert.That(w.At(3, 3).Water + w.At(3, 6).Water, Is.EqualTo(0), "both puddles gone");
            Assert.That(PondWater(w), Is.EqualTo(36), "the pond is deep: it stays");
            var off = Puddles(NoSoak);
            Run(off, 800);
            Assert.That(off.At(3, 3).Water, Is.EqualTo(1), "sim.water.soakPass 0: no soaking");
        }

        [Test]
        public void SoakingMatchesTs()
        {
            var w = Puddles();
            w.At(5, 1).T = "arid";
            w.At(5, 1).Water = 2;
            w.At(12, 7).T = "rock";
            w.At(12, 7).Water = 1;
            Run(w, 20);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("20661f72 1"));
        }

        [Test]
        public void AridGroundSoaksWaterUpFast()
        {
            var w = new World(R, 6, 6, 1, "arid");
            w.At(2, 2).Water = 2;
            Run(w, 40);
            Assert.That(w.AllCells().Sum(c => c.Water), Is.EqualTo(0), "gone in a few passes (dirt takes over a hundred ticks)");
        }

        [Test]
        public void WaterOnArcticGroundFreezes()
        {
            var w = new World(R, 6, 6, 1, "arctic");
            w.At(2, 2).Water = 6;
            Run(w, 16);
            var wet = w.AllCells().Where(c => c.Water > 0).ToList();
            Assert.That(wet, Is.Not.Empty);
            Assert.That(wet.All(c => c.Frozen), "every wet cell is ice, deep or thin");
        }

        [Test]
        public void WaterOnLavaBoilsToSteam()
        {
            var w = new World(R, 6, 6, 1, "lava");
            w.At(2, 2).Water = 6;
            bool steamed = false;
            for (int t = 0; t < 160; t++)
            {
                w.Step();
                steamed |= w.AllCells().Any(c => c.Gas == "steam");
            }
            Assert.That(steamed, "it boils off as steam");
            Assert.That(w.AllCells().Sum(c => c.Water), Is.EqualTo(0), "all of it");
        }

        static World ThatchByLogs(uint seed, Rules rules = null)
        {
            var w = new World(rules ?? R, 8, 3, seed, "dirt");
            w.At(2, 1).T = "thatch";
            w.At(3, 1).T = "thatch";
            w.At(4, 1).T = "logwall";
            w.Apply(2, 1, "fire", 3, -1, false);
            return w;
        }

        static (int thatchOut, int logsLit, int logsOut) BurnTimes(uint seed)
        {
            var w = ThatchByLogs(seed);
            int thatchOut = -1, logsLit = -1, logsOut = -1;
            for (int t = 1; t <= 400 && logsOut < 0; t++)
            {
                w.Step();
                if (thatchOut < 0 && w.At(2, 1).T != "thatch") thatchOut = t;
                if (logsLit < 0 && w.At(4, 1).Fire > 0) logsLit = t;
                if (logsLit >= 0 && w.At(4, 1).T != "logwall") logsOut = t;
            }
            return (thatchOut, logsLit, logsOut);
        }

        [Test]
        public void ThatchFlaresAndLightsTheLogWallWhichBurnsLong()
        {
            int Lit(Rules rules)
            {
                int lit = 0;
                for (uint seed = 1; seed <= 40; seed++)
                {
                    var w = ThatchByLogs(seed, rules);
                    Run(w, 30);
                    if (w.At(4, 1).Fire > 0 || w.At(4, 1).T != "logwall") lit++;
                }
                return lit;
            }
            var cool = R.WithMods(new[] { ModPatch.Parse("{ \"terrain\": { \"thatch\": { \"name\": \"Thatch roof\", \"tags\": [\"roof\", \"flammable\"], \"flammability\": 0.6, \"fuel\": 18, \"heat\": 1, \"burnsTo\": \"rubble\" } } }") });
            int hot = Lit(R), plain = Lit(cool);
            Assert.That(hot, Is.GreaterThan(plain), $"hot thatch lit the logs {hot} times in 40, at heat 1 {plain}");
            Assert.That($"{hot} {plain}", Is.EqualTo("32 23"), "the golden");

            var (thatchOut, logsLit, logsOut) = (-1, -1, -1);
            for (uint seed = 1; seed <= 40 && logsLit < 0; seed++) (thatchOut, logsLit, logsOut) = BurnTimes(seed);
            Assert.That(thatchOut, Is.InRange(12, 24), "thatch burns a few seconds, then is out");
            Assert.That(logsLit, Is.GreaterThan(0), "the logs catch from it");
            Assert.That(logsOut - logsLit, Is.GreaterThanOrEqualTo(100), $"and burn long: lit at {logsLit}, out at {logsOut}");
        }

        static (int next, int cell, int wall) WallBurn(string terrain, uint seed)
        {
            var w = new World(R, 10, 3, seed, "dirt");
            for (int x = 1; x <= 8; x++) w.At(x, 1).T = terrain;
            w.Apply(1, 1, "fire", 3, -1, false);
            var (next, cell, wall) = (-1, -1, -1);
            for (int t = 1; t <= 6000 && wall < 0; t++)
            {
                w.Step();
                if (next < 0 && w.At(2, 1).Fire > 0) next = t;
                if (cell < 0 && w.At(1, 1).T != terrain) cell = t;
                if (Enumerable.Range(1, 8).All(x => w.At(x, 1).T != terrain)) wall = t;
                if (Enumerable.Range(1, 8).All(x => w.At(x, 1).Fire == 0)) break;
            }
            return (next, cell, wall);
        }

        [Test]
        public void StuccoBurnsSlowerThanLogs()
        {
            var seeds = Enumerable.Range(1, 40).Select(s => (uint)s).ToList();
            var logs = seeds.Select(s => WallBurn("logwall", s)).ToList();
            var stucco = seeds.Select(s => WallBurn("stucco-wall", s)).ToList();
            double Mean(IEnumerable<int> ticks) => ticks.Where(t => t >= 0).DefaultIfEmpty(0).Average();
            Assert.That(stucco.Average(b => b.cell), Is.EqualTo(1.5 * logs.Average(b => b.cell)), "each cell lasts half as long again");
            Assert.That(Mean(stucco.Select(b => b.next)), Is.GreaterThan(2 * Mean(logs.Select(b => b.next))), "the next cell is more than twice as slow to catch");
            Assert.That(Mean(stucco.Select(b => b.wall)), Is.GreaterThan(1.75 * Mean(logs.Select(b => b.wall))), "the wall takes far longer to go");
            Assert.That(logs.Count(b => b.wall >= 0), Is.EqualTo(40), "logs always burn through");
            Assert.That(stucco.Count(b => b.wall >= 0), Is.InRange(25, 39), "stucco mostly does, and the fire sometimes dies in it");
        }

        static int ThatchCaught(int dampUntil, int lightAt)
        {
            int caught = 0;
            for (uint seed = 1; seed <= 40; seed++)
            {
                var w = new World(R, 8, 3, seed, "dirt");
                w.At(2, 1).T = "thatch";
                w.At(3, 1).T = "thatch";
                w.At(3, 1).DampUntil = dampUntil;
                Run(w, lightAt);
                w.Apply(2, 1, "fire", 3, -1, false);
                w.Step();
                if (w.At(3, 1).Fire > 0) caught++;
            }
            return caught;
        }

        [Test]
        public void SoakedThatchCatchesPoorlyUntilItDries()
        {
            Assert.That(R.Terrain["thatch"].Dampens, Is.EqualTo(0.5));
            int dry = ThatchCaught(0, 0), damp = ThatchCaught(int.MaxValue, 0);
            Assert.That(damp, Is.LessThan(dry), $"damp thatch caught {damp} times in 40, dry {dry}");
            Assert.That($"{dry} {damp}", Is.EqualTo("21 10"), "the golden");
            Assert.That(ThatchCaught(5, 10), Is.EqualTo(ThatchCaught(0, 10)), "once it has dried, it catches as dry thatch does");
        }

        static int ThatchBurnTicks(int dampUntil)
        {
            var w = new World(R, 8, 3, 1, "dirt");
            w.At(3, 1).T = "thatch";
            w.At(3, 1).DampUntil = dampUntil;
            w.Apply(3, 1, "fire", 3, -1, false);
            int ticks = 0;
            while (w.At(3, 1).Fire > 0 && ticks < 100)
            {
                w.Step();
                ticks++;
            }
            return ticks;
        }

        [Test]
        public void SoakedThatchBurnsOutSooner()
        {
            int dry = ThatchBurnTicks(0), damp = ThatchBurnTicks(int.MaxValue);
            Assert.That(damp, Is.LessThan(dry), $"damp thatch burnt {damp} ticks, dry {dry}");
            Assert.That($"{dry} {damp}", Is.EqualTo("18 9"), "the golden");
        }

        static (World w, int rusted, int lines) WetMetal(int ticks)
        {
            var w = new World(R, 8, 4, 1, "metal");
            for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) w.At(x, y).Water = 2;
            Run(w, ticks);
            return (w, w.AllCells().Count(c => c.T == "rusted-metal"), w.Log.Count(l => l.Text == "The metal rusts through"));
        }

        [Test]
        public void WetMetalRustsThroughAndDryMetalDoesNot()
        {
            Assert.That(R.Terrain["metal"].RustEvery, Is.EqualTo(2000));
            var (early, none, _) = WetMetal(1000);
            Assert.That(none, Is.EqualTo(0), "not yet");
            Assert.That(early.AllCells().Count(c => c.Rust > 0), Is.GreaterThan(0), "but counting");
            var (w, rusted, lines) = WetMetal(2400);
            Assert.That(rusted, Is.GreaterThan(0));
            Assert.That(lines, Is.InRange(1, rusted - 1), "a line a pass, not a line a cell");
            Assert.That(R.Terrain["metal"].Hardness == 0 && R.Terrain["rusted-metal"].Hardness > 0, "weaker: rusted metal digs, metal never does");
            Assert.That(R.Terrain["rusted-metal"].Has("conductive"), Is.False, "and it doesn't conduct");
            Assert.That($"{rusted} {lines} {w.CellHash()}", Is.EqualTo("20 1 1dc2aae5"), "the golden");

            var dry = new World(R, 8, 4, 1, "metal");
            Run(dry, 2400);
            Assert.That(dry.AllCells().All(c => c.T == "metal" && c.Rust == 0), "dry metal keeps");
        }

        [Test]
        public void BrokenRustIsRefusedWithTheTsWording()
        {
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(
                "{ 'terrain': { 'tin': { 'name': 'Tin', 'tags': ['ground'], 'rust': { 'every': 0.5, 'leaves': 'nope' } } } }".Replace('\'', '"')) })).Errors;
            Assert.That(errors, Is.EqualTo(new[] { "terrain tin: rustsTo nope unknown", "terrain tin: rustEvery must be a whole number from 1" }));
        }

        static World OilyPond(int spreadTicks)
        {
            var w = new World(R, 10, 5, 1, "pit");
            foreach (var c in w.AllCells()) c.Water = 3;
            w.At(1, 2).Cover = "oil";
            Run(w, spreadTicks);
            return w;
        }

        [Test]
        public void OilFloatsOutOverWaterAndBurnsAcrossItUntilItIsUsedUp()
        {
            var w = OilyPond(80);
            int slick = w.AllCells().Count(c => c.Cover == "oil");
            Assert.That(slick, Is.GreaterThan(5), "it spread across the surface");
            Assert.That(w.AllCells().Sum(c => c.Water), Is.EqualTo(150), "and the water stays as it was");

            w.Apply(1, 2, "fire", 1, -1, false);
            int most = 0;
            for (int t = 0; t < 120; t++)
            {
                w.Step();
                most = Math.Max(most, w.AllCells().Count(c => c.Fire > 0));
            }
            Assert.That(most, Is.GreaterThan(3), "the fire spread over the water");
            Assert.That(w.AllCells().Count(c => c.Fire > 0), Is.EqualTo(0), "and went out");
            Assert.That(w.AllCells().Count(c => c.Cover != null), Is.EqualTo(0), "with the oil used up, and no ash on the water");
            int water = w.AllCells().Sum(c => c.Water);
            Assert.That(water, Is.InRange(1, 149), "the heat boiled some off (sim.fire.boilChance), but the water itself never burned");
            Assert.That($"{slick} {most} {water} {w.CellHash()} {w.RngState}", Is.EqualTo("19 19 147 e3e69588 2221874590"), "the golden");
        }

        [Test]
        public void ABrokenFloatIsRefusedWithTheTsWording()
        {
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(
                "{ 'covers': { 'tar': { 'name': 'Tar', 'tags': [], 'float': { 'every': 0 } } } }".Replace('\'', '"')) })).Errors;
            Assert.That(errors, Is.EqualTo(new[] { "cover tar: floatEvery must be a whole number from 1" }));
        }

        [Test]
        public void LightningFusesSandIntoGlass()
        {
            var w = new World(R, 9, 5, 1, "sand");
            w.At(6, 2).Water = 2;
            w.Cast("shocking-grasp", 2, 2, 2);
            int glass = w.AllCells().Count(c => c.T == "glass");
            Assert.That(glass, Is.GreaterThan(0), "the strike fused some of the sand");
            Assert.That(w.Log.Any(l => l.Text == "The strike fuses the sand into glass"));
            var g = R.Terrain["glass"];
            Assert.That(g.Has("brittle") && !g.Burns && !g.Has("conductive"), "brittle; doesn't burn or conduct");
            Assert.That(new[] { "glow-ball-amber", "glow-ball-azure", "glow-ball-rose" }.All(id => R.Items[id].Material == "glass"));
            Run(w, 40);
            Assert.That($"{glass} {w.CellHash()} {w.RngState}", Is.EqualTo("1 e8b4e694 1"), "the golden");
        }

        [Test]
        public void MetalStaysHotAfterFireBurnsWhatStandsOnItAndSteamsWater()
        {
            var w = new World(R, 9, 5, 1, "metal");
            var r = w.SpawnRaider("giant-spider", 4, 2, 99);
            w.Cast("fire-bolt", 4, 2, 1);
            Assert.That(w.At(4, 2).Heat, Is.EqualTo(120), "the fire left it hot");
            int hp = r.Hp;
            Run(w, 12);
            Assert.That(w.At(4, 2).Heat, Is.EqualTo(108), "and it stays hot, cooling a tick at a time");
            Assert.That(r.Hp, Is.LessThan(hp), "it burns what stands on it");
            w.Cast("create-water", 4, 2, 1);
            int steam = 0;
            for (int t = 0; t < 10; t++)
            {
                w.Step();
                steam = Math.Max(steam, w.AllCells().Sum(c => c.Gas == "steam" ? c.GasAmt : 0));
            }
            Assert.That(steam, Is.GreaterThan(0), "the water hisses into steam");
            Assert.That(w.Log.Any(l => l.Text == "Water hisses into steam on the hot metal"));
            Assert.That(w.At(4, 2).Heat, Is.LessThan(98), "and cools it faster than time alone");
            Assert.That($"{r.Hp} {steam} {w.At(4, 2).Heat} {w.CellHash()} {w.RngState}", Is.EqualTo("84 3 0 4f80a218 1639742223"), "the golden");
        }

        [Test]
        public void SteamScaldsFleshNotBoneAndCondensesBackIntoWater()
        {
            var w = new World(R, 9, 5, 1, "metal");
            foreach (var c in w.AllCells())
            {
                c.Gas = "steam";
                c.GasAmt = 6;
            }
            var goblin = w.SpawnRaider("goblin", 3, 2, 99);
            var hound = w.SpawnRaider("bone-hound", 5, 2, 99);
            Run(w, 30);
            Assert.That(goblin.Hp, Is.LessThan(99), "flesh scalds");
            Assert.That(hound.Hp, Is.EqualTo(99), "bone doesn't");
            Run(w, 150);
            int steam = w.AllCells().Sum(c => c.Gas == "steam" ? c.GasAmt : 0), water = w.AllCells().Sum(c => c.Water);
            Assert.That(water, Is.GreaterThan(0), "it cooled back into water");
            Assert.That($"{goblin.Hp} {hound.Hp} {steam} {water} {w.CellHash()} {w.RngState}", Is.EqualTo("89 99 0 112 dc213318 2670964354"), "the golden");
        }

        static (World w, int near, int far) SteamByHeat(bool hot)
        {
            var w = new World(R, 9, 5, 1, "metal");
            foreach (var c in w.AllCells())
            {
                c.Gas = "steam";
                c.GasAmt = 6;
            }
            for (int y = 1; y <= 3; y++)
                for (int x = 3; x <= 5; x++) w.At(x, y).Height = 1;
            if (hot) w.At(4, 2).Heat = 120;
            Run(w, 60);
            int near = 0, far = 0;
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 9; x++)
                {
                    if (Math.Abs(x - 4) <= 1 && Math.Abs(y - 2) <= 1) near += w.At(x, y).Water;
                    else far += w.At(x, y).Water;
                }
            return (w, near, far);
        }

        [Test]
        public void SteamCondensesOnlyAwayFromHeat()
        {
            var (hot, near, far) = SteamByHeat(true);
            Assert.That(near, Is.EqualTo(0), "beside the heat it doesn't condense");
            Assert.That(far, Is.GreaterThan(0), "drifted away from it, it does");
            var (_, coldNear, _) = SteamByHeat(false);
            Assert.That(coldNear, Is.GreaterThan(0), "and with no heat, there too");
            Assert.That($"{near} {far} {coldNear} {hot.CellHash()} {hot.RngState}", Is.EqualTo("0 77 2 62127840 3544326918"), "the golden");
        }

        [Test]
        public void TheFireCombosCastScaldingSteamALavaPoolAndAFlameWave()
        {
            var w = new World(R, 24, 9, 1, "grass");
            var goblin = w.SpawnRaider("goblin", 18, 4, 99);
            var hound = w.SpawnRaider("bone-hound", 10, 4, 99);
            w.Cast("flame-wave", 2, 4, 1);
            Assert.That(w.Wind, Is.EqualTo(((int, int)?)(1, 18)), "the flame wave raises a wind along it");
            Assert.That(w.AllCells().Count(c => c.Fire > 0), Is.GreaterThan(3), "and sets its cone alight");
            w.Cast("lava-pool", 10, 4, 0);
            int pool = w.AllCells().Count(c => c.T == "lava-pool");
            Assert.That(pool, Is.GreaterThan(0));
            Assert.That(w.At(10, 4).Heat, Is.EqualTo(300), "the lava is hot");
            w.Cast("scalding-steam", 18, 4, 0);
            Assert.That(w.At(18, 4).Gas == "steam" && w.At(18, 4).GasAmt == 6, "a burst of thick steam");
            Run(w, 30);
            Assert.That(goblin.Hp, Is.LessThan(99), "the steam scalds the goblin");
            Assert.That(hound.Hp, Is.LessThan(99), "the lava burns the hound");
            Run(w, 300);
            Assert.That(w.AllCells().Count(c => c.T == "lava-pool"), Is.EqualTo(0), "the lava pool cools");
            Assert.That(w.Log.Any(l => l.Text == "The lava pool cools into rock"));
            Assert.That($"{pool} {goblin.Hp} {hound.Hp} {w.CellHash()} {w.RngState}", Is.EqualTo("9 90 -1 d6bc3323 2450008921"), "the golden");
        }

        [Test]
        public void TheAirCombosCastLightningBoltRainCloudSandBlastAndWhirlwind()
        {
            var w = new World(R, 24, 11, 1, "grass");
            w.Cast("sand-blast", 1, 2, 1);
            int sand = w.AllCells().Count(c => c.T == "sand");
            Assert.That(sand, Is.GreaterThan(0), "the blast leaves sand along its line");
            w.Cast("lightning-bolt", 1, 2, 1);
            int glass = w.AllCells().Count(c => c.T == "glass");
            Assert.That(glass, Is.GreaterThan(0), "the bolt fuses the sand it crosses into glass");
            Assert.That(w.AllCells().Count(c => c.Fire > 0), Is.GreaterThan(0), "and sets the grass beyond smouldering");
            w.Cast("rain-cloud", 16, 2, 0);
            Assert.That(w.At(16, 2).Gas, Is.EqualTo("cloud"));
            w.At(10, 8).Gas = "smoke";
            w.At(10, 8).GasAmt = 4;
            w.Cast("whirlwind", 12, 8, 0);
            Assert.That(w.At(10, 8).Gas, Is.Null, "the whirlwind draws the smoke in");
            int drawn = 0;
            for (int y = 7; y <= 9; y++)
                for (int x = 11; x <= 13; x++) drawn += w.At(x, y).Gas == "smoke" ? w.At(x, y).GasAmt : 0;
            Assert.That(drawn, Is.EqualTo(4), "toward its middle, all of it");
            Run(w, 60);
            int rained = 0;
            for (int x = 14; x <= 18; x++) rained += w.At(x, 2).Water;
            Assert.That(rained, Is.GreaterThan(0), "the cloud rains on the cells under it");
            Assert.That($"{sand} {glass} {rained} {w.CellHash()} {w.RngState}", Is.EqualTo("8 8 9 ef434c7d 269346629"), "the golden");
        }

        [Test]
        public void LightLastsItsDurationThenFades()
        {
            var s = R.Spell("light");
            Assert.That((s.LightBright, s.LightDim, s.Duration), Is.EqualTo((20, 20, 21600)));
            var w = new World(R, 7, 5, 1, "dirt");
            w.Cast("light", 3, 2, 0);
            Assert.That(w.At(3, 2).Lit, Is.EqualTo(21600), "lit for the hour");
            Assert.That(w.AllCells().Count(c => c.Lit > 0), Is.EqualTo(1), "on the cell touched only");
            Run(w, 10800);
            string half = w.CellHash();
            Assert.That(w.At(3, 2).Lit, Is.EqualTo(10800), "half way");
            Run(w, 10800);
            Assert.That(w.At(3, 2).Lit, Is.EqualTo(0), "and it fades");
            Assert.That($"{half} {w.CellHash()} {w.RngState}", Is.EqualTo("c022a8f5 b33ecaf0 364438357"), "the golden");
        }

        [Test]
        public void AGustPushesSmokeAndSteamFansTheFireAndKeepsMetalHot()
        {
            var w = new World(R, 16, 5, 1, "dirt");
            for (int x = 5; x <= 7; x++) w.At(x, 2).T = "grass";
            w.At(10, 2).T = "metal";
            w.At(10, 2).Heat = 40;
            w.At(3, 2).Gas = "smoke";
            w.At(3, 2).GasAmt = 4;
            w.At(4, 2).Gas = "steam";
            w.At(4, 2).GasAmt = 4;
            w.Apply(5, 2, "fire", 3, -1, false);
            Assert.That(w.At(5, 2).Fire, Is.EqualTo(2));
            w.Cast("gust-of-wind", 1, 2, 1);
            Assert.That(w.At(3, 2).Gas == null && w.At(4, 2).Gas == null, "the gust swept the smoke and steam on");
            Assert.That(w.AllCells().Where((c, i) => i % 16 > 4 && c.Gas != null).Sum(c => c.GasAmt), Is.EqualTo(8), "east, all of it");
            Assert.That(w.At(5, 2).Fire, Is.EqualTo(3), "it fanned the fire hotter");
            Assert.That(w.At(6, 2).Fire, Is.GreaterThan(0), "and on downwind");
            Assert.That(w.At(10, 2).Heat, Is.EqualTo(120), "and kept the metal hot");
            Run(w, 20);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("94877252 97577195"), "the golden");
        }

        [Test]
        public void AWetThingOfADampenedMaterialCatchesPoorly()
        {
            var straw = R.WithMods(new[] { ModPatch.Parse("{ \"entities\": { \"strawman\": { \"name\": \"Straw man\", \"material\": \"thatch\", \"hp\": 999, \"raider\": { \"ticksPerSquare\": 5 } } } }") });
            int Caught(bool wet)
            {
                int caught = 0;
                for (uint seed = 1; seed <= 40; seed++)
                {
                    var w = new World(straw, 20, 9, seed, "stone");
                    var r = w.SpawnRaider("strawman", 8, 4, 99);
                    if (wet) r.Status["wet"] = 60;
                    w.Cast("shocking-grasp", 8, 4, 0);
                    if (r.StatusOf("burning") > 0) caught++;
                }
                return caught;
            }
            int dry = Caught(false), soaked = Caught(true);
            Assert.That(soaked, Is.LessThan(dry), $"wet it caught {soaked} times in 40, dry {dry}");
            Assert.That($"{dry} {soaked}", Is.EqualTo("25 10"), "the golden");
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
        public void AWhirlwindDrawsGasAndCreaturesInAndRunsFireRoundItsRing()
        {
            var w = new World(R, 15, 9, 4, "dirt");
            for (int x = 5; x <= 9; x++) w.At(x, 2).T = "grass";
            var gob = w.SpawnRaider("goblin", 5, 4, 10);
            var hound = w.SpawnRaider("bone-hound", 9, 6, 6);
            w.Cast("whirlwind", 7, 4, 0);
            Assert.That(w.Whirls, Has.Count.EqualTo(1));
            w.At(9, 4).Gas = "smoke";
            w.At(9, 4).GasAmt = 3;
            w.Apply(5, 2, "fire", 3, -1, true);
            Run(w, 3);
            Assert.That(w.At(6, 2).Fire, Is.GreaterThan(0), "the fire runs east along the top ring");
            Assert.That((gob.X, gob.Y), Is.EqualTo((6, 4)), "the goblin is drawn a cell in");
            Assert.That(w.At(8, 4).Gas, Is.EqualTo("smoke"), "and the smoke");
            Run(w, 30);
            string mid = $"{w.CellHash()} {w.RngState} {gob.X},{gob.Y} {hound.X},{hound.Y}";
            Run(w, 30);
            Assert.That(w.Whirls, Is.Empty, "gone after its duration");
            Assert.That(hound.Alive, Is.False, "the hound, lifted in the eye and flung out, dies of the fall (World.LiftInTheEye)");
            Assert.That($"{mid} | {w.CellHash()} {w.RngState}", Is.EqualTo("62e2a56e 3068262754 7,0 7,0 | ab58829b 901838356"), "the golden");
        }

        [Test]
        public void ForgeHeatsAndTheMaterialDecides()
        {
            var w = new World(R, 24, 5, 4, "dirt");
            w.At(2, 2).T = "sand";
            w.At(6, 2).T = "mud";
            w.At(6, 2).Water = 1;
            w.At(10, 2).T = "metal";
            w.At(14, 2).Cover = "snow";
            w.At(18, 2).Water = 2;
            w.At(18, 2).Frozen = true;
            w.At(22, 2).T = "grass";
            var gob = w.SpawnRaider("goblin", 3, 2, 10);
            w.Apply(3, 2, "water");
            Assert.That(gob.StatusOf("wet"), Is.GreaterThan(0));
            Assert.That(R.Combos.Combo("earth+fire").Spell, Is.EqualTo("forge"));
            foreach (int x in new[] { 2, 6, 10, 14, 18, 22 }) w.Cast("forge", x, 2, 0);
            Assert.That(w.At(2, 2).T, Is.EqualTo("glass"));
            Assert.That(w.At(6, 2).T, Is.EqualTo("brick"));
            Assert.That(w.At(6, 2).Water, Is.EqualTo(0), "the mud dried");
            Assert.That(w.At(10, 2).Heat, Is.EqualTo(120));
            Assert.That(w.At(14, 2).Cover, Is.Null);
            Assert.That(w.At(18, 2).Frozen, Is.False);
            Assert.That(w.At(22, 2).Fire, Is.EqualTo(0), "it lights nothing");
            Assert.That(gob.StatusOf("wet"), Is.EqualTo(0));
            Assert.That(R.Terrain["brick"].Hardness, Is.EqualTo(3));
            Run(w, 12);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("1124f40e 3023719660"), "the golden");
        }

        [Test]
        public void AGeyserBoilsThePondIntoAJetAndFizzlesOnDryGround()
        {
            var w = new World(R, 12, 7, 5, "dirt");
            for (int y = 2; y <= 4; y++)
                for (int x = 6; x <= 8; x++) w.At(x, y).Water = 3;
            var gob = w.SpawnRaider("goblin", 7, 3, 10);
            Assert.That(R.Combos.Combo("water+fire").Spell, Is.EqualTo("geyser"));
            string dry = w.CellHash();
            w.Cast("geyser", 2, 3, 0);
            Assert.That(w.Log[w.Log.Count - 1].Text, Is.EqualTo("The geyser fizzles: there's no water there to boil"));
            Assert.That(w.CellHash(), Is.EqualTo(dry), "nothing changes on dry ground");
            w.Cast("geyser", 6, 3, 0);
            Assert.That(w.At(7, 3).Gas, Is.EqualTo("steam"));
            Assert.That(w.At(7, 3).Water, Is.EqualTo(2));
            Assert.That(w.At(5, 3).Gas, Is.Null, "only the water jets");
            Assert.That(gob.StatusOf(World.ThrownStatus), Is.GreaterThan(0));
            Run(w, 12);
            Assert.That(gob.Hp, Is.LessThan(10), "the steam scalds it");
            Assert.That($"{w.CellHash()} {w.RngState} {gob.Hp}", Is.EqualTo("4209bc06 3136449455 5"), "the golden");
        }

        [Test]
        public void AWaveRollsOutDousesKnocksBackAndLeavesMud()
        {
            var w = new World(R, 16, 7, 6, "dirt");
            w.At(6, 3).T = "grass";
            w.Ignite(w.At(6, 3), 2);
            var gob = w.SpawnRaider("goblin", 4, 2, 10);
            Assert.That(R.Combos.Combo("water+water").Spell, Is.EqualTo("wave"));
            w.Cast("wave", 1, 3, 1);
            Assert.That(w.Waves, Has.Count.EqualTo(1));
            Assert.That(w.At(1, 3).Water, Is.EqualTo(1), "it leaves shallow water as it rolls on");
            Run(w, 6);
            Assert.That(w.Waves[0].X, Is.EqualTo(4));
            Assert.That((gob.X, gob.Y), Is.EqualTo((5, 2)), "knocked back ahead of the wave");
            Assert.That(gob.StatusOf("wet"), Is.GreaterThan(0));
            Run(w, 10);
            Assert.That(w.Waves, Is.Empty, "it breaks at its length's end");
            Assert.That(w.At(6, 3).Fire, Is.EqualTo(0), "the fire is out");
            Assert.That((gob.X, gob.Y), Is.EqualTo((9, 2)));
            foreach (int y in new[] { 2, 3, 4 }) Assert.That(w.At(8, y).T, Is.EqualTo("mud"), "mud where it stopped");
            Assert.That(w.At(7, 3).T, Is.EqualTo("dirt"));
            Run(w, 4);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("33cc6d1f 752141770"), "the golden");
        }

        [Test]
        public void AWaveRollsAlongASlantedAimAndLeavesWaterBehind()
        {
            var w = new World(R, 20, 16, 12, "dirt");
            w.At(6, 5).T = "grass";
            w.Ignite(w.At(6, 5), 2);
            w.Cast("wave", 2, 2, World.DirOf(3, 2), 1, 3, 2);
            Assert.That(w.Waves[0].Dir, Is.EqualTo(1), "it pushes along the nearest grid way");
            Assert.That(w.At(2, 2).Water, Is.EqualTo(1), "shallow water where it passed");
            Run(w, 6);
            Assert.That((w.Waves[0].X, w.Waves[0].Y), Is.EqualTo((5, 4)));
            Run(w, 10);
            Assert.That(w.Waves, Is.Empty);
            Assert.That(w.At(6, 5).Fire, Is.EqualTo(0), "the fire on its slant is out");
            foreach (int y in new[] { 6, 7, 8 }) Assert.That(w.At(9, y).T, Is.EqualTo("mud"), "mud where it broke, off the straight line");
            Assert.That(w.At(9, 2).T, Is.EqualTo("dirt"), "it didn't go straight east");
            Run(w, 4);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("5331a509 2455825195"), "the golden");
        }

        [Test]
        public void HeatOnStandingWaterBoilsItAndBakesNothingUnderIt()
        {
            var w = new World(R, 14, 5, 8, "dirt");
            for (int x = 1; x <= 5; x++)
                for (int y = 1; y <= 3; y++)
                {
                    w.At(x, y).T = "mud";
                    w.At(x, y).Water = 3;
                }
            w.At(10, 2).T = "sand";
            w.At(10, 2).Water = 2;
            w.Cast("geyser", 3, 2, 0);
            w.Cast("forge", 10, 2, 0);
            Assert.That(w.At(3, 2).T, Is.EqualTo("mud"), "no brick under the pool");
            Assert.That(w.At(3, 2).Water, Is.EqualTo(2), "the water boiled");
            Assert.That(w.At(3, 2).Gas, Is.EqualTo("steam"));
            Assert.That(w.At(10, 2).T, Is.EqualTo("sand"), "no glass under the water");
            Assert.That(w.At(10, 2).Heat, Is.EqualTo(0), "the water took the heat");
            Run(w, 12);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("cdece807 2305213897"), "the golden");
        }

        static World SleetField()
        {
            var w = new World(R, 20, 20, 7);
            for (int y = 3; y <= 6; y++) w.At(5, y).T = "grass";
            w.At(7, 8).Water = 8;
            w.At(9, 9).Fire = 3;
            w.At(9, 9).Fuel = 4;
            w.Cast("sleet-storm", 8, 8, 1);
            return w;
        }

        [Test]
        public void SleetStormGlazesAWideSlipperyFieldAndPutsOutFires()
        {
            var w = SleetField();
            var field = w.SpellCells(R.Spell("sleet-storm"), 8, 8, 1);
            Assert.That(field.Count(p => w.At(p.x, p.y).Cover == "frost"), Is.GreaterThan(field.Count - 3), "the same frost Frost leaves");
            Assert.That(w.CoverHasTag(w.At(8, 8), "slippery"));
            Assert.That(w.At(7, 8).Frozen, "water still freezes");
            Assert.That(w.At(9, 9).Fire, Is.EqualTo(0));
        }

        [Test]
        public void GoldenSleetStorm()
        {
            var w = SleetField();
            for (int i = 0; i < 4; i++) w.Step();
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("bcb137be 7"));
        }

        [Test]
        public void FrostBridgesWaterFreezesTheWetAndGlazesTheGround()
        {
            var w = new World(R, 14, 7, 7, "dirt");
            for (int y = 0; y <= 6; y++)
                for (int x = 6; x <= 8; x++) w.At(x, y).Water = 3;
            var gob = w.SpawnRaider("goblin", 3, 3, 10);
            w.Apply(3, 3, "water");
            Assert.That(R.Combos.Combo("water+air").Spell, Is.EqualTo("frost"));
            w.Cast("frost", 1, 3, 1);
            foreach (int x in new[] { 6, 7, 8 }) Assert.That(w.At(x, 3).Frozen, Is.True, "an ice bridge");
            Assert.That(w.At(7, 2).Frozen, Is.False);
            Assert.That(w.At(2, 3).Cover, Is.EqualTo("frost"));
            Assert.That(w.IsSlippery(w.At(2, 3)) && w.IsSlippery(w.At(7, 3)), "slippery underfoot");
            Assert.That(gob.StatusOf("frozen"), Is.GreaterThan(0));
            w.Cast("ray-of-frost", 1, 5, 1);
            Assert.That(w.At(2, 5).Cover, Is.Null, "a cantrip doesn't frost the ground");
            w.Cast("acid-splash", 3, 3, 0);
            Assert.That(gob.Alive && gob.StatusOf("cracked") > 0, "the first hit cracks it");
            w.Cast("acid-splash", 3, 3, 0);
            Assert.That(gob.Alive, Is.False, "the next shatters it");
            w.Cast("fire-bolt", 2, 3, 0);
            Assert.That(w.At(2, 3).Cover, Is.Null, "fire melts the frost");
            Run(w, 12);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("4f8ffa48 1199730150"), "the golden");
        }

        [Test]
        public void ARollingBoulderRollsDownhillSmashesKnocksOverAndDamsTheStream()
        {
            var w = new World(R, 16, 10, 8, "dirt");
            w.At(5, 4).T = "glass";
            w.At(6, 4).Water = 2;
            w.At(6, 4).Frozen = true;
            w.At(8, 5).Height = -1;
            w.At(8, 6).Height = -1;
            w.At(8, 6).T = "pit";
            w.At(8, 6).Water = 3;
            var gob = w.SpawnRaider("goblin", 7, 4, 10);
            Assert.That(R.Combos.Combo("earth+air").Spell, Is.EqualTo("rolling-boulder"));
            w.Cast("rolling-boulder", 1, 4, 1, 2);
            Assert.That(w.Boulders, Has.Count.EqualTo(1));
            Run(w, 4);
            Assert.That((w.Boulders[0].X, w.Boulders[0].Y), Is.EqualTo((3, 4)));
            Assert.That(w.At(2, 4).Gas, Is.EqualTo("dust"), "a trail of dust");
            w.Cast("fire-bolt", 2, 4, 0);
            Assert.That(w.Log.Any(l => l.Text == "The dust goes up in a flash"));
            Run(w, 8);
            Assert.That(gob.StatusOf(World.ThrownStatus), Is.GreaterThan(0), "knocked over");
            Assert.That(gob.Hp, Is.LessThan(10));
            Run(w, 12);
            Assert.That(w.Boulders, Is.Empty);
            Assert.That(w.At(5, 4).Cracked, Is.GreaterThan(0), "the glass cracks");
            Assert.That(w.At(6, 4).Cracked, Is.GreaterThan(0), "and the ice");
            Assert.That(w.At(8, 6).T, Is.EqualTo("dirt"), "it rolled down the slope and dammed the stream");
            Assert.That($"{w.CellHash()} {w.RngState} {gob.X},{gob.Y} {gob.Hp}", Is.EqualTo("9179f2b8 729412872 7,4 5"), "the golden");
        }

        [Test]
        public void ARollingBoulderRollsAlongASlantedAimAndThenDownhill()
        {
            var w = new World(R, 18, 14, 8, "dirt");
            w.At(5, 4).T = "glass";
            foreach (int y in new[] { 6, 7, 8, 9, 10 }) w.At(6, y).Height = -1;
            w.Cast("rolling-boulder", 2, 2, World.DirOf(3, 2), 1, 3, 2);
            Assert.That(w.Boulders[0].Dir, Is.EqualTo(1), "it pushes along the nearest grid way");
            Run(w, 6);
            Assert.That((w.Boulders[0].X, w.Boulders[0].Y), Is.EqualTo((5, 4)), "three cells along the aim, not along a grid way");
            Assert.That(w.At(5, 4).Cracked, Is.GreaterThan(0), "and it strikes what's on its slant");
            Run(w, 2);
            var back = World.Load(w.Save(new Blanks()), R, new Blanks());
            Run(back, 2);
            Run(w, 2);
            Assert.That((w.Boulders[0].X, w.Boulders[0].Y), Is.EqualTo((6, 6)), "the slope to its side turns it");
            Assert.That((back.Boulders[0].X, back.Boulders[0].Y), Is.EqualTo((6, 6)));
            Assert.That(w.Boulders[0].Dir, Is.EqualTo(2));
            Run(w, 4);
            Assert.That((w.Boulders[0].X, w.Boulders[0].Y), Is.EqualTo((6, 8)), "on down its new way");
            Run(w, 20);
            Assert.That(w.Boulders, Is.Empty);
            Assert.That(w.At(3, 2).Gas, Is.Not.EqualTo("dust"), "no trail along the grid way it wasn't aimed");
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("c5a8709f 3377061646"), "the golden");
        }

        [Test]
        public void PlantGrowthVinesClimbWallsBridgeGapsBurnAndRootsCrackStone()
        {
            var w = new World(R, 12, 7, 9, "dirt");
            w.At(3, 3).T = "stone";
            w.At(5, 3).T = "pit";
            w.At(7, 3).T = "river";
            w.At(9, 3).T = "rock";
            w.At(9, 3).Height = 3;
            w.At(6, 5).T = "pit";
            w.Cast("plant-growth", 6, 3, 0);
            foreach (int x in new[] { 3, 5, 7, 9 }) Assert.That(w.IsClimbable(w.At(x, 3)), Is.True, $"vines at {x}");
            Assert.That(w.At(4, 3).Cover, Is.Null, "nothing to climb on open ground");
            w.Apply(6, 5, "fire");
            Assert.That(w.At(6, 5).Fire, Is.GreaterThan(0), "vines burn");
            Run(w, 920);
            Assert.That(w.At(3, 3).T, Is.EqualTo("rubble"), "roots crack the stone wall");
            Assert.That(w.At(9, 3).T, Is.EqualTo("rock"));
            Assert.That(w.At(5, 3).Cover, Is.Null, "the vines rot away");
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("f70d280c 1696107128"), "the golden");
        }

        [Test]
        public void WallOfStoneMakesStepsOverWaterRampsWhenHeldAndOnlyHardBlowsBreakIt()
        {
            var w = new World(R, 14, 8, 10, "dirt");
            for (int y = 0; y <= 7; y++)
                for (int x = 6; x <= 7; x++) w.At(x, y).Water = 3;
            w.Cast("wall-of-stone", 2, 3, 1);
            Assert.That(w.At(4, 3).Height, Is.EqualTo(3));
            Assert.That(w.IsBlocking(w.At(4, 3)), Is.True);
            Assert.That(w.At(6, 3).Height, Is.EqualTo(1), "a stepping stone");
            Assert.That(w.At(6, 3).T, Is.EqualTo("rock"));
            Assert.That(w.IsBlocking(w.At(6, 3)), Is.False);
            w.Cast("stone-ramp", 2, 6, 1);
            Assert.That(new[] { 2, 3, 4, 5 }.Select(x => w.At(x, 6).Height), Is.EqualTo(new[] { 1, 2, 2, 2 }), "a ramp up");
            w.Cast("fireball", 3, 3, 0);
            Assert.That(w.At(3, 3).Height, Is.EqualTo(3), "too hard for a fireball");
            w.Cast("falling-star", 3, 3, 0);
            Run(w, 6);
            Assert.That(w.At(3, 3).Height, Is.LessThan(3), "a falling star breaks it");
            Run(w, 6);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("4432219c 1218213550"), "the golden");
        }

        [Test]
        public void ADustFlashShovesARaiderTheSameAsTheSandbox()
        {
            var w = new World(R, 10, 5, 11, "dirt");
            for (int x = 3; x <= 5; x++) w.AddGas(w.At(x, 2), "dust", 3);
            var hound = w.SpawnRaider("bone-hound", 5, 2, 6);
            w.Apply(3, 2, "fire");
            Assert.That((hound.X, hound.Y), Is.EqualTo((7, 2)), "shoved two cells away from the spark");
            Run(w, 6);
            Assert.That($"{w.CellHash()} {w.RngState} {hound.X},{hound.Y} {hound.Hp}", Is.EqualTo("31a36a85 3279484455 7,2 6"), "the golden");
        }

        [Test]
        public void LightningJumpsToAPuddleAndItsChargeOpensTheGate()
        {
            var w = new World(R, 16, 7, 12, "dirt");
            for (int x = 4; x <= 6; x++) w.At(x, 5).Water = 2;
            w.At(7, 5).T = "coil";
            w.At(12, 3).T = "gate";
            w.Cast("lightning-bolt", 2, 3, 1);
            Assert.That(w.At(4, 5).Charge, Is.GreaterThan(0), "it jumped to the puddle");
            Assert.That(w.At(7, 5).Charge, Is.GreaterThan(0), "and ran through it to the coil");
            Assert.That(w.GatesOpen, Is.False);
            w.Step();
            Assert.That(w.GatesOpen, Is.True);
            Assert.That(w.At(12, 3).T, Is.EqualTo("gate_open"));
            Assert.That(w.Log.Any(l => l.Text == "The gate grinds open, powered by the lightning coil"));
            Run(w, 30);
            Assert.That(w.At(12, 3).T, Is.EqualTo("gate"), "it shuts once the coil's hold runs out");
            var dry = new World(R, 16, 7, 12, "dirt");
            dry.At(10, 3).T = "sand";
            dry.Cast("lightning-bolt", 2, 3, 1);
            Assert.That(dry.At(10, 3).T, Is.EqualTo("glass"), "with nothing to jump to it flies straight");
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("c4741e7f 12"), "the golden");
        }

        [Test]
        public void RainCloudsGreenAshLightningMakesAStormAndColdMakesSnow()
        {
            var w = new World(R, 16, 7, 13, "dirt");
            for (int y = 1; y <= 5; y++)
                for (int x = 1; x <= 5; x++) w.At(x, y).Cover = "ash";
            w.Cast("rain-cloud", 3, 3, 0);
            w.Cast("rain-cloud", 11, 3, 0);
            w.Cast("shocking-grasp", 3, 3, 0);
            Assert.That(w.At(3, 3).Gas, Is.EqualTo("storm"));
            w.Apply(11, 3, "cold");
            Assert.That(w.At(11, 3).Gas, Is.EqualTo("snowcloud"));
            Run(w, 120);
            Assert.That(w.AllCells().Count(c => c.T == "sprouts" || c.T == "grass"), Is.GreaterThan(0), "the rain greens the ash");
            Assert.That(w.AllCells().Count(c => c.Cover == "snow"), Is.GreaterThan(0), "the snow cloud snows");
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("788f5f20 1615770602"), "the golden");
        }

        [Test]
        public void SandBlastScoursWearsAndLeavesSandAndGustDrivesOilAcrossAPond()
        {
            var w = new World(R, 14, 7, 14, "dirt");
            w.At(2, 2).Cover = "snow";
            w.At(3, 2).T = "rusted-metal";
            w.At(4, 2).Height = 2;
            w.At(5, 2).T = "logwall";
            w.Cast("sand-blast", 1, 2, 1);
            Assert.That(w.At(2, 2).Cover, Is.Null);
            Assert.That(w.At(2, 2).T, Is.EqualTo("sand"));
            Assert.That(w.At(3, 2).T, Is.EqualTo("metal"), "the rust scoured off");
            Assert.That(w.At(4, 2).Height, Is.EqualTo(1), "raised earth worn down a step");
            Assert.That(w.At(5, 2).T, Is.EqualTo("worn-logwall"));
            w.Cast("sand-blast", 1, 2, 1);
            Assert.That(w.At(5, 2).T, Is.EqualTo("rubble"), "and then through");
            w.Cast("forge", 2, 2, 0);
            Assert.That(w.At(2, 2).T, Is.EqualTo("glass"));
            for (int x = 3; x <= 9; x++) w.At(x, 5).Water = 3;
            w.At(3, 5).Cover = "oil";
            w.Cast("gust-of-wind", 1, 5, 1);
            Assert.That(w.At(3, 5).Cover, Is.Null);
            Assert.That(w.At(9, 5).Cover, Is.EqualTo("oil"), "driven across the pond");
            Run(w, 6);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("7716f25c 14"), "the golden");
        }

        [Test]
        public void LavaRunsDownhillLeavingACrustAndSetsAtOnceInWater()
        {
            var w = new World(R, 12, 7, 15, "dirt");
            w.At(5, 3).Height = -1;
            w.At(6, 3).Height = -2;
            w.At(7, 3).T = "pit";
            w.At(7, 3).Height = -2;
            w.At(7, 3).Water = 3;
            w.Cast("lava-pool", 3, 3, 0);
            Assert.That(w.At(4, 3).T, Is.EqualTo("lava-pool"));
            Run(w, 6);
            Assert.That(w.At(5, 3).T, Is.EqualTo("lava-pool"), "it runs downhill");
            Assert.That(w.At(4, 3).T, Is.EqualTo("rock"), "leaving a crust to walk on");
            Run(w, 13);
            Assert.That(w.At(7, 3).T, Is.EqualTo("rock"), "and sets at once in the water");
            Assert.That(w.Log.Any(l => l.Text == "The lava hisses and sets into rock"));
            Run(w, 6);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("811d9fa2 2967354774"), "the golden");
        }

        [Test]
        public void ScaldingSteamRustsMetalFaster()
        {
            var w = new World(R, 8, 5, 16, "dirt");
            w.At(2, 2).T = "metal";
            w.At(2, 2).Water = 1;
            w.At(5, 2).T = "metal";
            w.At(5, 2).Water = 1;
            w.AddGas(w.At(5, 2), "steam", 30);
            Run(w, 8);
            Assert.That(w.At(5, 2).Rust, Is.GreaterThan(2 * w.At(2, 2).Rust), "faster under steam");
            Assert.That(w.At(2, 2).Rust, Is.GreaterThan(0));
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("3bf05b30 2775531184"), "the golden");
        }

        [Test]
        public void CreateWaterWatersBurntGround()
        {
            var w = new World(R, 8, 6, 17, "dirt");
            for (int y = 1; y <= 4; y++)
                for (int x = 1; x <= 4; x++) w.At(x, y).Cover = "ash";
            w.Cast("create-water", 2, 2, 0);
            Assert.That(w.At(2, 2).Cover, Is.Null);
            Assert.That(w.At(2, 2).Water, Is.GreaterThan(0));
            Run(w, 80);
            Assert.That(w.At(2, 2).T, Is.EqualTo("sprouts").Or.EqualTo("grass"), "green shoots in the ash");
            Assert.That(w.At(4, 4).Cover, Is.EqualTo("ash"), "only where the water landed");
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("49ebceb 3599190446"), "the golden");
        }

        [Test]
        public void PlainFireBakesNothing()
        {
            var w = new World(R, 8, 5, 4, "dirt");
            w.At(2, 2).T = "sand";
            w.At(5, 2).T = "mud";
            w.Cast("fire-bolt", 2, 2, 0);
            w.Cast("fire-bolt", 5, 2, 0);
            Assert.That(w.At(2, 2).T, Is.EqualTo("sand"));
            Assert.That(w.At(5, 2).T, Is.EqualTo("mud"));
        }

        [Test]
        public void AFallingStarMarksItsTargetThenStrikes()
        {
            var w = new World(R, 15, 9, 6, "dirt");
            w.At(8, 4).Cover = "snow";
            w.At(7, 3).Water = 2;
            w.At(7, 3).Frozen = true;
            w.At(6, 4).Water = 2;
            Assert.That(R.Combos.Combo("fire+air").Spell, Is.EqualTo("falling-star"));
            string before = w.CellHash();
            w.Cast("falling-star", 7, 4, 0);
            Assert.That(w.At(7, 4).Star, Is.EqualTo(1));
            Assert.That(w.At(6, 3).Star, Is.EqualTo(1));
            Assert.That(w.At(5, 4).Star, Is.EqualTo(0));
            Assert.That(w.CellHash(), Is.Not.EqualTo(before), "the mark is shared state");
            Run(w, 5);
            Assert.That(w.At(7, 4).Height, Is.EqualTo(0), "still falling");
            Assert.That(w.At(7, 4).Star, Is.EqualTo(1));
            w.Step();
            Assert.That(w.At(7, 4).Star, Is.EqualTo(0), "the mark goes when it lands");
            Assert.That(w.Stars, Is.Empty);
            Assert.That(w.At(7, 4).Height, Is.LessThan(0));
            Assert.That(w.At(7, 4).T, Is.EqualTo("lava-pool"));
            Assert.That(w.At(7, 4).Heat, Is.GreaterThan(0));
            Assert.That(w.At(8, 4).Cover, Is.Not.EqualTo("snow"));
            Assert.That(w.At(7, 3).Frozen, Is.False);
            Assert.That(w.At(6, 4).Gas, Is.EqualTo("steam"));
            Assert.That(w.AllCells().Count(c => c.Cover == "embers" && c.Fire > 0), Is.GreaterThan(0));
            Run(w, 12);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("c91f14e2 657895373"), "the golden");
        }

        [Test]
        public void TheWaterAndEarthCombosCastTheirOwnSpells()
        {
            foreach (var (combo, spell) in new[] { ("water+earth", "mud") })
                Assert.That(R.Combos.Combo(combo).Spell, Is.EqualTo(spell), combo);
            var w = new World(R, 20, 12, 9, "dirt");
            w.At(5, 2).T = "pit";
            w.At(6, 2).T = "pit";
            for (int x = 3; x <= 5; x++) w.At(x, 7).T = "grass";
            w.Cast("flood", 1, 2, 1);
            w.Cast("mud", 4, 7, 0);
            w.Cast("molten-boulder", 14, 3, 0);
            w.Cast("dust-storm", 14, 9, 0);
            Assert.That(w.At(5, 2).Water > 0 && w.At(6, 2).Water > 0, "and fills the pits");
            Assert.That(new[] { 3, 4, 5 }.Select(x => w.At(x, 7).T), Is.EqualTo(new[] { "mud", "mud", "mud" }));
            Assert.That(w.At(14, 3).T, Is.EqualTo("lava"));
            Assert.That(w.At(14, 9).Gas, Is.EqualTo("dust"));
            Run(w, 12);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("7a38bb69 939719861"), "the golden");
        }

        [Test]
        public void CreaturesBurnFreezeAndSoakByTheirMaterial()
        {
            var w = new World(R, 16, 7, 5, "dirt");
            var them = new[] { w.SpawnRaider("goblin", 3, 3, 10), w.SpawnRaider("bone-hound", 7, 3, 6), w.SpawnRaider("ash-crab", 11, 3, 9) };
            var states = new List<string>();
            foreach (var element in new[] { "fire", "water", "cold" })
            {
                foreach (var r in them) w.Apply(r.X, r.Y, element, 1, -1, false);
                states.Add(string.Join(" ", them.Select(r => $"{r.StatusOf("burning")},{r.StatusOf("wet")},{r.StatusOf("frozen")}")));
            }
            Assert.That(states, Is.EqualTo(new[] { "9,0,0 0,0,0 9,0,0", "0,60,0 0,60,0 0,60,0", "0,0,30 0,0,30 0,60,0" }));
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("7cbe626d 5"), "the golden");
            var ex = Assert.Throws<ModException>(() => RuleSet.Plain(R).WithMods(new[] { new ActiveMod("bad", "{ \"reactions\": { \"addEntity\": [ { \"id\": \"e-odd\", \"element\": \"fire\", \"if\": { \"verb\": \"melt\" }, \"do\": [{ \"statusOf\": \"glow\" }] } ] } }") }));
            Assert.That(ex.Errors, Is.EquivalentTo(new[] { "rule e-odd: statusOf must be burn, freeze or soak", "rule e-odd: verb must be burn, freeze or soak" }));
        }

        [Test]
        public void WaterSoaksDirtToMudWhichDriesBack()
        {
            Assert.That(R.Terrain["mud"].Speed, Is.EqualTo(0.5));
            var w = new World(R, 10, 5, 1, "dirt");
            w.At(6, 2).T = "grass";
            w.At(7, 2).T = "rock";
            for (int x = 3; x <= 7; x++) w.At(x, 2).Water = 2;
            w.At(4, 2).Cover = "oil";
            Run(w, 200);
            Assert.That(new[] { 3, 4, 5, 6, 7 }.Select(x => w.At(x, 2).T), Is.EqualTo(new[] { "mud", "mud", "mud", "grass", "rock" }));
            w.Apply(4, 2, "fire", 3, -1, true);
            Assert.That(w.At(4, 2).Fire, Is.EqualTo(0), "the mud smothers it, oil or no");
            string wet = w.CellHash();
            Run(w, 800);
            Assert.That(new[] { 3, 4, 5 }.Select(x => w.At(x, 2).T), Is.EqualTo(new[] { "dirt", "dirt", "dirt" }), "dried back to dirt");
            Assert.That($"{wet} {w.CellHash()} {w.RngState}", Is.EqualTo("f44639dc 668f5350 1"), "the golden");
        }

        [Test]
        public void AcidSplashEatsBramblesThornsAndOilAndBurnsAGoblin()
        {
            var w = new World(R, 12, 9, 3, "dirt");
            for (int x = 5; x <= 7; x++) w.At(x, 3).T = "brush";
            w.At(5, 4).Cover = "grease";
            w.At(7, 4).Cover = "thorns";
            w.At(6, 5).Cover = "oil";
            w.At(4, 4).Cover = "oil";
            var gob = w.SpawnRaider("goblin", 6, 4, 10);
            w.Cast("acid-splash", 6, 4, 0);
            for (int x = 5; x <= 7; x++) Assert.That(w.At(x, 3).T, Is.EqualTo("dirt"), $"brambles at {x} eaten");
            Assert.That(new[] { w.At(5, 4).Cover, w.At(7, 4).Cover, w.At(6, 5).Cover, w.At(4, 4).Cover }, Is.EqualTo(new[] { null, null, null, "oil" }));
            Assert.That(w.Log.Any(l => l.Text == "The acid eats the brambles away"));
            Assert.That($"{w.CellHash()} {w.RngState} {gob.Hp}", Is.EqualTo("d9c38f7d 1831565816 5"), "the golden");
            var ex = Assert.Throws<ModException>(() => RuleSet.Plain(R).WithMods(new[] { new ActiveMod("bad", "{ \"covers\": { \"slime\": { \"name\": \"Slime\", \"tags\": [], \"dissolve\": { \"by\": \"vinegar\", \"leaves\": \"goo\" } } } }") }));
            Assert.That(ex.Errors, Is.EqualTo(new[] { "cover slime: dissolves by unknown element vinegar", "cover slime: dissolvesTo goo unknown" }));
        }

        static (World w, string panes) WindowGlass()
        {
            var w = new World(R, 16, 10, 5, "dirt");
            for (int x = 3; x <= 7; x++)
                for (int y = 3; y <= 5; y++) w.At(x, y).T = "logwall";
            foreach (var (x, y) in new[] { (5, 3), (3, 4), (7, 4), (5, 5), (11, 3) }) w.At(x, y).T = "pane";
            w.At(11, 4).T = "logwall";
            w.Apply(5, 2, "fire", 2, -1, true);
            string panes = w.At(5, 3).T;
            w.Cast("fireball", 5, 1, 0);
            Assert.That(w.At(5, 3).T, Is.EqualTo("pane"), "one blast cracks it");
            Assert.That(w.At(5, 3).Cracked, Is.Not.EqualTo(0));
            Run(w, 1);
            w.Cast("fireball", 5, 1, 0);
            panes += " " + w.At(5, 3).T + " " + w.At(7, 4).T;
            w.Reshape(3, 4, 1);
            Run(w, 1);
            w.Reshape(3, 4, 1);
            panes += " " + w.At(3, 4).T;
            w.ApplyEffect(new EffectCommand(EffectKind.Shatter, 5, 5, ""));
            Run(w, 1);
            w.ApplyEffect(new EffectCommand(EffectKind.Shatter, 5, 5, ""));
            panes += " " + w.At(5, 5).T;
            w.Apply(11, 4, "fire", 3, -1, true);
            Run(w, 400);
            return (w, panes + " " + w.At(11, 4).T + " " + w.At(11, 3).T);
        }

        [Test]
        public void GlassAndIceGoIntactCrackedShattered()
        {
            var w = new World(R, 14, 7, 3, "dirt");
            w.At(2, 3).T = "glass";
            w.At(5, 3).T = "pane";
            w.At(5, 2).T = "logwall";
            w.At(9, 3).Water = 2;
            w.At(9, 3).Frozen = true;
            w.At(12, 3).Water = 2;
            w.At(12, 3).Frozen = true;
            w.SpawnRaider("goblin", 9, 3, 10);
            foreach (var (x, y) in new[] { (2, 3), (5, 3), (12, 3) })
            {
                Assert.That(w.Shatter(x, y), Is.False, "the first hit cracks it");
                Assert.That(w.Shatter(x, y), Is.False, "one hit a tick");
            }
            w.Cast("shocking-grasp", 9, 3, 0);
            Assert.That(w.At(2, 3).T, Is.EqualTo("glass"));
            Assert.That(w.At(5, 3).T, Is.EqualTo("pane"));
            Assert.That(w.At(9, 3).Frozen, "lightning on ice cracks it");
            Assert.That(w.At(9, 3).Cracked, Is.Not.EqualTo(0));
            Assert.That(w.Log.Any(l => l.Text == "The ice cracks under Goblin"));
            string cracked = w.CellHash();
            Run(w, 1);
            foreach (var (x, y) in new[] { (2, 3), (5, 3), (9, 3) }) Assert.That(w.Shatter(x, y), "the next shatters it");
            Assert.That(w.At(2, 3).T, Is.EqualTo("sand"));
            Assert.That(w.At(5, 3).T, Is.EqualTo("rubble"));
            Assert.That(w.At(9, 3).Frozen, Is.False, "open water under the goblin");
            Assert.That(new[] { w.At(2, 3).Cracked, w.At(5, 3).Cracked, w.At(9, 3).Cracked }, Is.All.EqualTo(0));
            w.Apply(12, 3, "fire", 1, -1, true);
            Run(w, 1);
            Assert.That(w.At(12, 3).Cracked, Is.EqualTo(0), "a crack goes with its ice");
            Run(w, 12);
            Assert.That($"{cracked} {w.CellHash()} {w.RngState}", Is.EqualTo("17d9616c 4fcd46bc 1831565816"), "the golden");
        }

        [Test]
        public void ACrackedCellAndThingComeThroughASave()
        {
            var glow = R.Item("glow-ball-amber");
            Assert.That(R.ItemCracks(glow.Id), "a glow ball's glass cracks");
            Assert.That(R.ItemCracks("bullseye-lantern"), Is.False);
            var w = new World(R, 8, 8, 1, "dirt");
            w.At(2, 2).T = "glass";
            w.Shatter(2, 2);
            var thing = new ThingId(0, 0, 3);
            string whole = w.CellHash();
            w.ApplyEffect(new EffectCommand(EffectKind.Crack, 1, 1, glow.Id, thing));
            Assert.That(w.IsCracked(thing));
            Assert.That(w.CellHash(), Is.Not.EqualTo(whole), "a cracked thing is hashed");
            var back = World.Load(w.Save(new Blanks()), R, new Blanks());
            Assert.That(back.IsCracked(thing));
            Assert.That(back.At(2, 2).Cracked, Is.EqualTo(w.At(2, 2).Cracked));
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
        }

        sealed class Blanks : IChunkSource
        {
            public string Version => "blank";
            public void Make(int x, int y, Cell cell) { }
        }

        [Test]
        public void WindowGlassShattersUnderForceAndFallsWithItsWall()
        {
            var (w, panes) = WindowGlass();
            Assert.That(panes, Is.EqualTo("pane rubble pane rubble rubble rubble rubble"), "fire leaves it; a blast and shaken ground break it; a pane out of the blast stands; it falls with its wall");
            Assert.That(w.Log.Any(l => l.Text == "The window pane shatters"));
            Assert.That(w.IsBlocking(w.At(5, 3)), Is.False, "an open gap");
            Assert.That(R.Terrain["pane"].Has("blocking") && !R.Terrain["pane"].Burns && !R.Terrain["pane"].Conducts);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("e425b302 3234871980"), "the golden");
        }

        static string FrostThenSpark(bool wet)
        {
            int gone = 0;
            uint rng = 0;
            for (uint seed = 1; seed <= 40; seed++)
            {
                var w = new World(R, 20, 9, seed, "dirt");
                var r = w.SpawnRaider("goblin", 8, 4, 10);
                if (wet) r.Status["wet"] = 60;
                w.Cast("ray-of-frost", 8, 4, 0);
                Assert.That(r.Alive, "the cold that froze it doesn't shatter it (status.shatter notBy)");
                w.Cast("shocking-grasp", 8, 4, 0);
                if (wet) Assert.That(r.Alive, "the first spark only cracks it");
                w.Cast("shocking-grasp", 8, 4, 0);
                if (!r.Alive) gone++;
                rng += w.RngState;
            }
            return $"{gone} {rng}";
        }

        static (World w, Raider swimmer, Raider wetBank, Raider dryBank) ShockWater(Rules rules)
        {
            var w = new World(rules, 14, 8, 3, "dirt");
            for (int x = 3; x <= 8; x++)
                for (int y = 2; y <= 4; y++)
                {
                    var c = w.At(x, y);
                    c.T = "pit"; c.Water = 3;
                }
            var swimmer = w.SpawnRaider("goblin", 8, 3, 10);
            var wetBank = w.SpawnRaider("goblin", 9, 3, 10);
            wetBank.Status["wet"] = 60;
            var dryBank = w.SpawnRaider("goblin", 6, 5, 10);
            w.Cast("shocking-grasp", 3, 3, 1);
            w.Step();
            return (w, swimmer, wetBank, dryBank);
        }

        static Rules FleshChainsWet(string chainsWet) => R.WithMods(new[] { ModPatch.Parse(
            "{ \"terrain\": { \"flesh\": { \"name\": \"Flesh\", \"tags\": [], \"burn\": { \"status\": 9 }, \"freeze\": { \"status\": 30 }, \"soak\": { \"status\": 60 }, \"conduct\": { \"chainsWet\": " + chainsWet + " } } } }") });

        [Test]
        public void ShockWaterReachesSwimmersAndTheWetOnTheBank()
        {
            var (w, swimmer, wetBank, dryBank) = ShockWater(R);
            int zap = R.Sim.ZapDamage;
            Assert.That(10 - swimmer.Hp, Is.EqualTo(zap * 2), "the swimmer is wet, so twice");
            Assert.That(10 - wetBank.Hp, Is.EqualTo(zap * 2), "wet beside the water: shocked too");
            Assert.That(dryBank.Hp, Is.EqualTo(10), "dry on the bank: not");
            Assert.That($"{w.CellHash()} {w.RngState} {swimmer.Hp} {wetBank.Hp} {dryBank.Hp}", Is.EqualTo("d2510429 3 4 4 10"), "the golden");
            Assert.That(ShockWater(FleshChainsWet("false")).wetBank.Hp, Is.EqualTo(10), "without chainsWet the bank is safe");
            var ex = Assert.Throws<ModException>(() => RuleSet.Plain(R).WithMods(new[] { new ActiveMod("bad", "{ \"terrain\": { \"flesh\": { \"name\": \"Flesh\", \"tags\": [], \"conduct\": { \"chainsWet\": 2 } } } }") }));
            Assert.That(ex.Errors, Is.EqualTo(new[] { "terrain flesh: conduct.chainsWet must be true or false" }));
        }

        [Test]
        public void AStruckFrozenGoblinShatters()
        {
            Assert.That($"{FrostThenSpark(true)} | {FrostThenSpark(false)}", Is.EqualTo("40 744566284 | 33 2136120018"), "the golden");
            var w = new World(R, 20, 9, 1, "dirt");
            var r = w.SpawnRaider("goblin", 8, 4, 10);
            r.Status["wet"] = 60;
            w.Cast("ray-of-frost", 8, 4, 0);
            int hp = r.Hp;
            w.Cast("shocking-grasp", 8, 4, 0);
            Assert.That(r.StatusOf(World.CrackedStatus), Is.GreaterThan(0));
            Assert.That(r.Hp, Is.EqualTo(hp), "a crack takes no harm yet");
            Assert.That(w.Log.Any(l => l.Text == "Goblin cracks"));
            w.Cast("shocking-grasp", 8, 4, 0);
            Assert.That(w.Log.Any(l => l.Text == "Goblin shatters"));
            var ex = Assert.Throws<ModException>(() => RuleSet.Plain(R).WithMods(new[] { new ActiveMod("bad", "{ \"sim\": { \"status\": { \"shatter\": { \"when\": 3 } } } }") }));
            Assert.That(ex.Errors, Is.EqualTo(new[] { "sim status.shatter.when must be a name" }));
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

        [Test]
        public void WaterFlowsDownhillIntoAPit()
        {
            var w = Room();
            w.Paint(5, 4, "pit");
            w.AddWater(4, 4, 3);
            Run(w, 10);
            Assert.That(w.At(5, 4).Water, Is.EqualTo(3), "all of it ran into the pit");
            Assert.That(w.At(4, 4).Water, Is.EqualTo(0));
            Assert.That(w.AllCells().Sum(c => c.Water), Is.EqualTo(3), "water is conserved");
        }

        [Test]
        public void CreateWaterRunsDownAChannel()
        {
            var w = Room(16, 5, rules: NoSoak);
            for (int x = 2; x <= 13; x++) w.At(x, 2).T = "pit";
            w.Cast("create-water", 3, 2, 0);
            int total = w.AllCells().Sum(c => c.Water);
            Run(w, 80);
            Assert.That(w.At(9, 2).Water, Is.GreaterThan(0), "water reached the wheel's square (x=9 in sim.test.ts)");
            Assert.That(w.AllCells().Sum(c => c.Water), Is.EqualTo(total));
            CollectionAssert.AreEqual(new[] { 0, 2, 7, 6, 6, 5, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0 },
                Enumerable.Range(0, 16).Select(x => w.At(x, 2).Water).ToArray());
        }

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
            Assert.That(w.AllCells().Sum(c => c.Water), Is.EqualTo(4));
        }

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
