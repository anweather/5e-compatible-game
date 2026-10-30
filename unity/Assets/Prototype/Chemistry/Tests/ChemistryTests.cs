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
        static Rules noSoak;
        /// <summary>The rules without soaking (sim.water.soakPass 0), for tests of the flow alone.</summary>
        internal static Rules NoSoak => noSoak ??= R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"water\": { \"soakPass\": 0 } } }") });

        /// <summary>
        /// The repo's rules/ folder. In Unity it sits next to the project; under
        /// `dotnet test` (core/Game.Core.Tests) we walk up from the test binary.
        /// </summary>
        internal static string RulesDir()
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

        // ---------- line of sight (Misty Step) ----------

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

        // ---------- rules + rng ----------

        [Test]
        public void RulesLoadAllFiles()
        {
            Assert.That(R.Terrain.ContainsKey("grass"));
            Assert.That(R.Terrain["pit"].Elevation, Is.EqualTo(-1));
            Assert.That(R.Terrain["grass"].Has("flammable"));
            Assert.That(R.Covers["oil"].BurnIntensity, Is.EqualTo(3));
            Assert.That(R.Gases.ContainsKey("steam"));
            Assert.That(R.CellRules.Count, Is.EqualTo(46));
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

            Assert.That(w.CellHash(), Is.EqualTo("19ccbc27"));
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
                "60|info|1|The ice insulates and the spark fizzles",
                "60|info|1|You cast Thunderwave",
                "60|info|1|A pillar of earth rises",
                "60|info|1|Earth fills the pit",
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

        // 12 units poured mid-way along a 16×9 meadow on this land; the water west and east of it after 300 ticks.
        static int Spread(int[] land, out int east)
        {
            var w = new World(NoSoak, 16, 9, 1, "grass");
            w.SetLand(land);
            w.At(8, 4).Water = 12;
            Run(w, 300);
            int west = w.Cells.Where((c, k) => k % 16 < 8).Sum(c => c.Water);
            east = w.Cells.Where((c, k) => k % 16 > 8).Sum(c => c.Water);
            return west;
        }

        [Test]
        public void WaterRunsDownTheLandsSlope()
        {
            // Playtest #31: Create Water on a hillside pooled where it landed. The hill is the land's
            // (the valley's baked ground), which the cells don't hold: SetLand tells the water.
            var slope = new int[16 * 9];
            for (int k = 0; k < slope.Length; k++) slope[k] = (k % 16) * 3; // rising east, 0.375 m a cell
            int westOnSlope = Spread(slope, out int eastOnSlope);
            Assert.That(eastOnSlope, Is.EqualTo(0), "none runs uphill");
            Assert.That(westOnSlope, Is.GreaterThanOrEqualTo(10), "it runs down the slope");
            int westOnFlat = Spread(null, out int eastOnFlat);
            Assert.That(eastOnFlat, Is.GreaterThan(0), "on the flat it spreads both ways");
            Assert.That(westOnFlat, Is.LessThan(westOnSlope));
            // A water body's cells keep level: no land between them and the slope.
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
            // A pond on low land among high land: as a water body it keeps the flat rules and spills
            // out; were its cell dry land, the high land round it would hold the water in.
            var w = new World(R, 7, 5, 1, "grass");
            w.At(3, 2).Water = 6;
            var high = new int[7 * 5];
            for (int k = 0; k < high.Length; k++) high[k] = 400;
            high[2 * 7 + 3] = 0;
            w.SetLand(high);
            high[0] = 1; // a copy: changing the array after doesn't move the land
            Run(w, 20);
            Assert.That(w.At(2, 2).Water + w.At(4, 2).Water + w.At(3, 1).Water + w.At(3, 3).Water, Is.GreaterThan(0), "the pond spills as on the flat");
        }

        [Test]
        public void TheValleysLandIsPinned()
        {
            // world/land.json is the same integers on every peer (lockstep): a re-bake that moves them
            // shows here first. Rebake (Prototype > Bake Valley Land) and update these if the valley's
            // shape changed on purpose.
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(RulesDir()), "world", "land.json");
            var land = LandFile.Read(System.IO.File.ReadAllText(path), 576, 576);
            Assert.That(land.Length, Is.EqualTo(576 * 576));
            Assert.That(land[0], Is.EqualTo(665), "the north-west corner, up in the hills");
            Assert.That(land[418 * 576 + 202], Is.EqualTo(103), "the slope probe's hillside");
            Assert.That(land[418 * 576 + 208], Is.EqualTo(64), "six cells down it");
            Assert.That(land[288 * 576 + 288], Is.EqualTo(0), "the middle");
            Assert.That(land[575 * 576 + 575], Is.EqualTo(-174), "the south-east corner, by the sea");
        }

        // A dirt field with a puddle (a unit: too little to run on the flat) at (3, 3), another at (3, 6),
        // and a pond (4 units a cell, as deep as the field is high) in a 3 × 3 pit at (8..10, 3..5).
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
            // Andrew, 2026-10-28: thin water on open ground soaks in and dries up (materials.json
            // soakEvery), more slowly on damp ground (soakEveryWet), and leaves the ground damp.
            var w = Puddles();
            w.At(3, 6).DampUntil = 100000; // already damp
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
            Run(w, 20); // part way: some puddles gone, some not
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("20661f72 1"));
        }

        [Test]
        public void AridGroundSoaksWaterUpFast()
        {
            var w = new World(R, 6, 6, 1, "arid");
            w.At(2, 2).Water = 2;
            Run(w, 40);
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(0), "gone in a few passes (dirt takes over a hundred ticks)");
        }

        [Test]
        public void WaterOnArcticGroundFreezes()
        {
            var w = new World(R, 6, 6, 1, "arctic");
            w.At(2, 2).Water = 6;
            Run(w, 16);
            var wet = w.Cells.Where(c => c.Water > 0).ToList();
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
                steamed |= w.Cells.Any(c => c.Gas == "steam");
            }
            Assert.That(steamed, "it boils off as steam");
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(0), "all of it");
        }

        // Thatch at (2, 1) and (3, 1) against a log wall at (4, 1), on dirt; the thatch set alight.
        static World ThatchByLogs(uint seed, Rules rules = null)
        {
            var w = new World(rules ?? R, 8, 3, seed, "dirt");
            w.At(2, 1).T = "thatch";
            w.At(3, 1).T = "thatch";
            w.At(4, 1).T = "logwall";
            w.Apply(2, 1, "fire", 3, -1, false);
            return w;
        }

        // The tick the thatch burnt out, the logs caught (-1 never) and they burnt out.
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
            // Andrew, 2026-10-28: thatch catches easily, burns hot and fast and is soon out; wood is slow to
            // catch and burns long; the thatch's heat (materials.json "heat") is what lights the wood.
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
            Assert.That($"{hot} {plain}", Is.EqualTo("32 23"), "the same in the sandbox");

            // The first of those runs where the logs caught: when the thatch was out, the logs lit, and they burnt out.
            var (thatchOut, logsLit, logsOut) = (-1, -1, -1);
            for (uint seed = 1; seed <= 40 && logsLit < 0; seed++) (thatchOut, logsLit, logsOut) = BurnTimes(seed);
            Assert.That(thatchOut, Is.InRange(12, 24), "thatch burns a few seconds, then is out");
            Assert.That(logsLit, Is.GreaterThan(0), "the logs catch from it");
            Assert.That(logsOut - logsLit, Is.GreaterThanOrEqualTo(100), $"and burn long: lit at {logsLit}, out at {logsOut}");
        }

        // Over 40 seeds, how often the second thatch (3, 1) caught from the first in one tick; damp sets
        // its Cell.DampUntil, and the fire is lit at tick lightAt.
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
            // docs/MATERIALS.md "Soaked burns poorly": a damp cell's catch is scaled by 1 - soak.dampens (thatch 0.5).
            Assert.That(R.Terrain["thatch"].Dampens, Is.EqualTo(0.5));
            int dry = ThatchCaught(0, 0), damp = ThatchCaught(int.MaxValue, 0);
            Assert.That(damp, Is.LessThan(dry), $"damp thatch caught {damp} times in 40, dry {dry}");
            Assert.That($"{dry} {damp}", Is.EqualTo("21 10"), "the same in the sandbox");
            Assert.That(ThatchCaught(5, 10), Is.EqualTo(ThatchCaught(0, 10)), "once it has dried, it catches as dry thatch does");
        }

        // How many ticks a lone thatch (3, 1) on dirt burns once lit; damp sets its Cell.DampUntil.
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
            // docs/MATERIALS.md "Soaked burns poorly": caught damp, its fuel is scaled by 1 - soak.dampens too.
            int dry = ThatchBurnTicks(0), damp = ThatchBurnTicks(int.MaxValue);
            Assert.That(damp, Is.LessThan(dry), $"damp thatch burnt {damp} ticks, dry {dry}");
            Assert.That($"{dry} {damp}", Is.EqualTo("18 9"), "the same in the sandbox");
        }

        // Metal 8 x 4, its left half under 2 water, run for ticks; and how many cells rusted through and log lines it gave.
        static (World w, int rusted, int lines) WetMetal(int ticks)
        {
            var w = new World(R, 8, 4, 1, "metal");
            for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) w.At(x, y).Water = 2;
            Run(w, ticks);
            return (w, w.Cells.Count(c => c.T == "rusted-metal"), w.Log.Count(l => l.Text == "The metal rusts through"));
        }

        [Test]
        public void WetMetalRustsThroughAndDryMetalDoesNot()
        {
            // docs/MATERIALS.md: metal's rust { every 2000, leaves rusted-metal }, counted on the soak pass while wet.
            Assert.That(R.Terrain["metal"].RustEvery, Is.EqualTo(2000));
            var (early, none, _) = WetMetal(1000);
            Assert.That(none, Is.EqualTo(0), "not yet");
            Assert.That(early.Cells.Count(c => c.Rust > 0), Is.GreaterThan(0), "but counting");
            var (w, rusted, lines) = WetMetal(2400);
            Assert.That(rusted, Is.GreaterThan(0));
            Assert.That(lines, Is.InRange(1, rusted - 1), "a line a pass, not a line a cell");
            Assert.That(R.Terrain["rusted-metal"].Hardness, Is.LessThan(R.Terrain["metal"].Hardness), "weaker");
            Assert.That(R.Terrain["rusted-metal"].Has("conductive"), Is.False, "and it doesn't conduct");
            Assert.That($"{rusted} {lines} {w.CellHash()}", Is.EqualTo("20 1 1dc2aae5"), "the same in the sandbox");

            var dry = new World(R, 8, 4, 1, "metal");
            Run(dry, 2400);
            Assert.That(dry.Cells.All(c => c.T == "metal" && c.Rust == 0), "dry metal keeps");
        }

        [Test]
        public void BrokenRustIsRefusedWithTheTsWording()
        {
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(
                "{ 'terrain': { 'tin': { 'name': 'Tin', 'tags': ['ground'], 'rust': { 'every': 0.5, 'leaves': 'nope' } } } }".Replace('\'', '"')) })).Errors;
            Assert.That(errors, Is.EqualTo(new[] { "terrain tin: rustsTo nope unknown", "terrain tin: rustEvery must be a whole number from 1" }));
        }

        // A 10 x 5 pond (pit under 3 water) with oil spilt at (1, 2), left spreadTicks to float out.
        static World OilyPond(int spreadTicks)
        {
            var w = new World(R, 10, 5, 1, "pit");
            foreach (var c in w.Cells) c.Water = 3;
            w.At(1, 2).Cover = "oil";
            Run(w, spreadTicks);
            return w;
        }

        [Test]
        public void OilFloatsOutOverWaterAndBurnsAcrossItUntilItIsUsedUp()
        {
            // Oil floats (materials.json float.every 16): the slick grows a cell a turn over open water.
            var w = OilyPond(80);
            int slick = w.Cells.Count(c => c.Cover == "oil");
            Assert.That(slick, Is.GreaterThan(5), "it spread across the surface");
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(150), "and the water stays as it was");

            // Lit, the fire runs across the slick; the water doesn't burn, and the fire dies with the oil.
            w.Apply(1, 2, "fire", 1, -1, false);
            int most = 0;
            for (int t = 0; t < 120; t++)
            {
                w.Step();
                most = Math.Max(most, w.Cells.Count(c => c.Fire > 0));
            }
            Assert.That(most, Is.GreaterThan(3), "the fire spread over the water");
            Assert.That(w.Cells.Count(c => c.Fire > 0), Is.EqualTo(0), "and went out");
            Assert.That(w.Cells.Count(c => c.Cover != null), Is.EqualTo(0), "with the oil used up, and no ash on the water");
            int water = w.Cells.Sum(c => c.Water);
            Assert.That(water, Is.InRange(1, 149), "the heat boiled some off (sim.fire.boilChance), but the water itself never burned");
            Assert.That($"{slick} {most} {water} {w.CellHash()} {w.RngState}", Is.EqualTo("29 28 52 a1f460b7 189079474"), "the same in the sandbox");
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
            // A strike on sand (tag fuses) fuses it into glass (reactions.json lightning-fuse): glass is
            // brittle, and doesn't burn or conduct. The glow balls are glass.
            var w = new World(R, 9, 5, 1, "sand");
            w.At(6, 2).Water = 2;
            w.Cast("shocking-grasp", 2, 2, 2);
            int glass = w.Cells.Count(c => c.T == "glass");
            Assert.That(glass, Is.GreaterThan(0), "the strike fused some of the sand");
            Assert.That(w.Log.Any(l => l.Text == "The strike fuses the sand into glass"));
            var g = R.Terrain["glass"];
            Assert.That(g.Has("brittle") && !g.Burns && !g.Has("conductive"), "brittle; doesn't burn or conduct");
            Assert.That(new[] { "glow-ball-amber", "glow-ball-azure", "glow-ball-rose" }.All(id => R.Items[id].Material == "glass"));
            Run(w, 40);
            Assert.That($"{glass} {w.CellHash()} {w.RngState}", Is.EqualTo("1 e8b4e694 1"), "the same in the sandbox");
        }

        [Test]
        public void AWetThingOfADampenedMaterialCatchesPoorly()
        {
            // Fire on a wet thing only sizzles (e-fire-wet); a spark still lights it, with its material's
            // flammability less its soak.dampens.
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
            Assert.That($"{dry} {soaked}", Is.EqualTo("25 10"), "the same in the sandbox");
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
        public void WaterSoaksDirtToMudWhichDriesBack()
        {
            // docs/MATERIALS.md soak.leaves, soak.dries: shallow water over dirt, grass and rock; the dirt turns
            // to mud as it soaks in (grass and rock don't), fire on the mud is smothered (fire-mud) even where
            // oil lies, and once it's dry the mud is dirt again. Mirrors sandbox/test/terraform.test.ts (which
            // also walks a goblin into it: Thorns snags it for 1/speed - 1 of a second).
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
            Assert.That($"{wet} {w.CellHash()} {w.RngState}", Is.EqualTo("efb1f804 2143a548 1"), "the same in the sandbox");
        }

        [Test]
        public void AcidSplashEatsBramblesThornsAndOilAndBurnsAGoblin()
        {
            // docs/MATERIALS.md dissolve: a row of brambles, thorns, grease and oil round a goblin; Acid Splash
            // on the goblin eats them all (dissolve.by acid) and hurts it. Mirrors sandbox/test/terraform.test.ts.
            var w = new World(R, 12, 9, 3, "dirt");
            for (int x = 5; x <= 7; x++) w.At(x, 3).T = "brush";
            w.At(5, 4).Cover = "grease";
            w.At(7, 4).Cover = "thorns";
            w.At(6, 5).Cover = "oil";
            w.At(4, 4).Cover = "oil"; // outside the splash
            var gob = w.SpawnRaider("goblin", 6, 4, 10);
            w.Cast("acid-splash", 6, 4, 0);
            for (int x = 5; x <= 7; x++) Assert.That(w.At(x, 3).T, Is.EqualTo("dirt"), $"brambles at {x} eaten");
            Assert.That(new[] { w.At(5, 4).Cover, w.At(7, 4).Cover, w.At(6, 5).Cover, w.At(4, 4).Cover }, Is.EqualTo(new[] { null, null, null, "oil" }));
            Assert.That(w.Log.Any(l => l.Text == "The acid eats the brambles away"));
            Assert.That($"{w.CellHash()} {w.RngState} {gob.Hp}", Is.EqualTo("d9c38f7d 1831565816 5"), "the same in the sandbox");
            var ex = Assert.Throws<ModException>(() => RuleSet.Plain(R).WithMods(new[] { new ActiveMod("bad", "{ \"covers\": { \"slime\": { \"name\": \"Slime\", \"tags\": [], \"dissolve\": { \"by\": \"vinegar\", \"leaves\": \"goo\" } } } }") }));
            Assert.That(ex.Errors, Is.EqualTo(new[] { "cover slime: dissolves by unknown element vinegar", "cover slime: dissolvesTo goo unknown" }));
        }

        // Over 40 seeds, a goblin (wet or not) hit by Ray of Frost, then Shocking Grasp: how many are gone,
        // and the sum of the worlds' RngStates.
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
                if (!r.Alive) gone++;
                rng += w.RngState;
            }
            return $"{gone} {rng}";
        }

        [Test]
        public void AStruckFrozenGoblinShatters()
        {
            // docs/MATERIALS.md "Frozen things shatter": a wet goblin freezes solid under Ray of Frost, and
            // the spark that follows (any hurt but cold) shatters it; a dry one only takes the damage.
            Assert.That($"{FrostThenSpark(true)} | {FrostThenSpark(false)}", Is.EqualTo("40 496377796 | 18 496377796"), "the same in the sandbox");
            var w = new World(R, 20, 9, 1, "dirt");
            var r = w.SpawnRaider("goblin", 8, 4, 10);
            r.Status["wet"] = 60;
            w.Cast("ray-of-frost", 8, 4, 0);
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
            var w = Room(16, 5, rules: NoSoak);
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
